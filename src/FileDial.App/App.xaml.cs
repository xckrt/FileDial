using System.Windows;
using System.Windows.Threading;
using FileDial.App.Documents;
using FileDial.App.Hotkey;
using FileDial.App.Interop;
using FileDial.App.Selection;
using FileDial.App.Setup;
using FileDial.App.Settings;
using FileDial.App.Tray;
using FileDial.App.UI;
using FileDial.App.Updates;
using FileDial.Core;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Custom;
using FileDial.Core.Localization;
using FileDial.Core.Documents;
using FileDial.Core.History;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Selection;
using FileDial.Core.Settings;

namespace FileDial.App;

/// <summary>Приложение в трее: без видимых окон, пока не нажата горячая клавиша.</summary>
public partial class App : Application
{
    private static readonly TimeSpan SelectionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MediaProbeTimeout = TimeSpan.FromSeconds(2);

    private Mutex? _singleInstance;
    private EventWaitHandle? _exitSignal;
    private EventWaitHandle? _dialSignal;
    private HotkeyManager? _hotkey;
    private TrayIcon? _tray;
    private AppUpdater? _updater;
    private RadialWindow? _radial;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private AppSettings _settings = new();
    /// <summary>Есть ли фильтр whisper в текущем ffmpeg (проверяется в фоне при запуске и после настроек).</summary>
    private bool _speechFilter;
    private bool _readingSelection;
    private readonly List<JobWindow> _jobs = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ErrorLog.Write("Необработанное исключение", (Exception)args.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write("Задача", args.Exception);
            args.SetObserved();
        };

        // Тот же exe — и установщик: «FileDial-Setup-….exe», --install, --uninstall.
        if (SetupLauncher.Detect(e.Args, Environment.ProcessPath) is { } setupMode)
        {
            SetupLauncher.Run(this, setupMode, e.Args);
            return;
        }

        // «Открыть в FileDial…» из меню Проводника: круг открывает уже запущенная копия, эта сразу выходит.
        var dial = ContextMenu.IsDialLaunch(e.Args);
        _singleInstance = new Mutex(true, @"Local\FileDial.Prototype", out var created);
        if (!created && dial)
        {
            ContextMenu.RequestDial();
            Shutdown();
            return;
        }
        if (!created)
        {
            Lang.Set(SettingsStore.Load(SettingsStore.DefaultPath).Language);
            MessageBox.Show(Lang.T.ShellAlreadyRunning, "FileDial",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Установщик просит закрыться перед обновлением или удалением — выходим штатно, как из меню трея.
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, RunningCopies.ExitEventName);
        ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(ExitApp), null, -1, executeOnlyOnce: true);
        // Пункт меню Проводника при запущенной копии — то же, что горячая клавиша (ждём каждый раз, а не однажды).
        _dialSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ContextMenu.DialEventName);
        ThreadPool.RegisterWaitForSingleObject(_dialSignal, (_, _) => Dispatcher.BeginInvoke(OpenDial), null, -1, executeOnlyOnce: false);

        _settings = SettingsStore.Load(SettingsStore.DefaultPath);
        Motion.UserEnabled = _settings.Animations;
        Palette.Apply(Theme.Resolve(_settings.Theme));
        Lang.Set(_settings.Language);
        ContextMenu.Set(_settings.ContextMenu); // пункт меню — на этот exe и на текущем языке

        _ = RefreshSpeechSupportAsync();
        var noFfmpeg = FfmpegLocator.LocateDefault(_settings.FfmpegFolder) is null;

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += OnHotkeyPressed;
        var gesture = _settings.HotkeyGesture;
        var error = _hotkey.Register(gesture);
        _tray = new TrayIcon(gesture.Format(), OpenSettings, OpenHistory, ShellActions.OpenReadme, ExitApp);
        // Первый запуск без ffmpeg: сразу подсказываем, что его можно поставить одной кнопкой.
        if (noFfmpeg)
            _tray.Notify(Lang.T.ShellNeedsFfmpegTitle, Lang.T.ShellNeedsFfmpegText,
                OpenFfmpegSetup);
        if (AppUpdater.Enabled)
        {
            _updater = new AppUpdater(_tray, () => _settings);
            _tray.AddCommand(() => Lang.T.UpdTrayCheckNow, _updater.CheckNow);
            _ = _updater.CheckOnStartupAsync();
        }
        // Сжатый exe распаковывается в %TEMP%\.net: прежние версии там больше не нужны.
        if (AppPaths.IsSelfExtracted)
            _ = Task.Run(() => AppPaths.CleanOldExtractions(AppContext.BaseDirectory, DateTime.UtcNow));

        if (error != 0)
        {
            // Без клавиши программа бесполезна, но выходить не нужно: даём сразу выбрать другое сочетание.
            _tray.SetHotkey(null);
            MessageBox.Show(
                Lang.T.ShellHotkeyStartupProblem(HotkeyManager.Describe(error, gesture)),
                "FileDial", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenSettings();
        }
        else if (dial)
        {
            // Запущены из меню Проводника, а копии в трее не было: открываем круг, когда всё готово.
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, OpenDial);
        }
    }

    /// <summary>Круг по пункту меню Проводника: тот же путь, что и по горячей клавише.</summary>
    private void OpenDial() => OnHotkeyPressed(this, EventArgs.Empty);

    private async Task RefreshSpeechSupportAsync()
    {
        var folder = _settings.FfmpegFolder;
        _speechFilter = await Task.Run(async () =>
            FfmpegLocator.LocateDefault(folder) is { } tools && await Speech.FilterAvailableAsync(tools, CancellationToken.None));
    }

    private void OpenHistory()
    {
        if (_historyWindow is not null)
        {
            _historyWindow.Refresh();
            _historyWindow.Activate();
            return;
        }
        _historyWindow = new HistoryWindow(
            () => HistoryStore.Load(HistoryStore.DefaultPath),
            ClearHistory,
            () => _settings.KeepHistory,
            WindowPlacement.CursorPosition());
        _historyWindow.Closed += (_, _) => _historyWindow = null;
        _historyWindow.Show();
        _historyWindow.Activate();
    }

    private static void ClearHistory()
    {
        try
        {
            HistoryStore.Clear(HistoryStore.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Очистка истории", ex);
        }
    }

    /// <summary>Итог действия — в историю (если она включена). Сбой записи истории не мешает работе.</summary>
    private void RecordHistory(HistoryEntry entry)
    {
        if (!_settings.KeepHistory)
            return;
        try
        {
            HistoryStore.Append(HistoryStore.DefaultPath, entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Запись истории", ex);
        }
        _historyWindow?.Refresh();
    }

    private void OpenSettings() => OpenSettings(SettingsSection.None);

    /// <summary>Открыть настройки на разделе установки ffmpeg (из окна действия или уведомления).</summary>
    internal void OpenFfmpegSetup() => OpenSettings(SettingsSection.Ffmpeg);

    private void OpenSettings(SettingsSection focus)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, Autostart.IsEnabled(), WindowPlacement.CursorPosition(),
            ApplySettings, focus, _speechFilter, () => _ = RefreshSpeechSupportAsync());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Применяет настройки из окна. Сначала то, что может не получиться (клавиша, реестр), потом сохранение.</summary>
    private string? ApplySettings(AppSettings settings, bool autostart)
    {
        var gesture = settings.HotkeyGesture;
        var error = _hotkey!.Register(gesture);
        if (error != 0)
            return Lang.T.ShellHotkeyChooseAnother(HotkeyManager.Describe(error, gesture));

        if (autostart != Autostart.IsEnabled() && Autostart.Set(autostart) is { } autostartError)
            return autostartError;

        var language = Lang.T.Code;
        if (ContextMenu.Set(settings.ContextMenu) is { } menuError)
            return menuError;

        try
        {
            SettingsStore.Save(SettingsStore.DefaultPath, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Сохранение настроек", ex);
            return Lang.T.ShellSettingsSaveFailed;
        }

        _settings = settings.Normalized();
        Motion.UserEnabled = _settings.Animations;
        Palette.Apply(Theme.Resolve(_settings.Theme));
        Lang.Set(_settings.Language);
        if (_settings.ContextMenu && Lang.T.Code != language)
            ContextMenu.Set(true); // текст пункта — на новом языке
        _tray?.SetHotkey(_hotkey.Current?.Format());
        _ = RefreshSpeechSupportAsync(); // могла смениться папка ffmpeg
        return null;
    }

    private async void OnHotkeyPressed(object? sender, EventArgs e)
    {
        // Пока в настройках записывается новое сочетание, нажатия — это ввод, а не вызов круга.
        if (_settingsWindow?.IsCapturingHotkey == true)
            return;
        // Повторное нажатие при открытом круге закрывает его.
        if (_radial is not null)
        {
            _radial.Dismiss();
            return;
        }
        if (_readingSelection)
            return;

        var cursor = WindowPlacement.CursorPosition();
        var foreground = NativeMethods.GetForegroundWindow();
        _readingSelection = true;
        try
        {
            var selection = await StaRunner.RunAsync(
                () => ShellSelectionReader.Read(foreground),
                SelectionTimeout,
                () => SelectionResult.Fail(Lang.T.ShellExplorerNotResponding));

            // Проверка файла и ffprobe могут занять время (сетевой диск, процесс) — не в UI-потоке.
            var settings = _settings;
            var speechFilter = _speechFilter;
            var outcome = await Task.Run(() =>
            {
                var tools = FfmpegLocator.LocateDefault(settings.FfmpegFolder);
                var media = tools is null ? null : new FfprobeMediaProbe(tools, MediaProbeTimeout);
                return SelectionInterpreter.Interpret(selection, new FileSystemProbe(), media,
                    settings.Jobs with
                    {
                        // Модель из настроек или найденная сама (рядом с программой / скачанная).
                        WhisperModel = SpeechModels.FindDefault(settings.WhisperModel),
                        SpeechFilterAvailable = speechFilter,
                        BackgroundModel = BackgroundModels.FindDefault(),
                    });
            });

            switch (outcome)
            {
                case HotkeyOutcome.ShowToast toast:
                    ToastWindow.ShowAt(toast.Message, cursor);
                    break;
                case HotkeyOutcome.ShowMenu menu:
                    ShowRadial(menu.Menu, cursor);
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Горячая клавиша", ex);
            ToastWindow.ShowAt(Lang.T.ShellSomethingWrong, cursor);
        }
        finally
        {
            _readingSelection = false;
        }
    }

    private void ShowRadial(DialMenu menu, NativeMethods.POINT cursor)
    {
        var radial = new RadialWindow(menu, cursor);
        radial.Closed += (_, _) =>
        {
            _radial = null;
            if (radial.Chosen is { Id: ActionIds.SetupSpeech })
            {
                OpenSettings(SettingsSection.Speech);
                return;
            }
            if (radial.Chosen is { Id: ActionIds.SetupBackground })
            {
                OpenSettings(SettingsSection.Background);
                return;
            }
            if (radial.Chosen is { Id: ActionIds.ArchiveContents } && menu.SourcePath is { } archive)
            {
                // «Содержимое» — не задача над файлом, а окно просмотра; оттуда можно сразу распаковать.
                var contents = new ContentsWindow(archive, WindowPlacement.CursorPosition(), () => StartJob(
                    new DialAction(ActionIds.ExtractHere, Lang.T.ExtractHere, true), [archive]));
                contents.Show();
                contents.Activate();
                return;
            }
            // Своё действие: программа из настроек, для пакета — по каждому файлу.
            if (radial.Chosen is { } chosen && CustomActions.Find(_settings.CustomActions, chosen.Id) is { } custom
                && menu.SourcePaths.Count > 0)
            {
                var output = _settings.Jobs.OutputFolder;
                StartJob(chosen, menu.SourcePaths, tools => new CustomActionJob(custom, tools, output));
                return;
            }
            if (radial.Chosen is not { } action || menu.SourcePaths.Count == 0 || !FileJobs.IsImplemented(action.Id))
                return;
            if (FileJobs.NeedsTrimInput(action.Id) && !menu.IsBatch)
                AskTrimRange(action, menu.SourcePaths[0]);
            else if (FileJobs.NeedsFolderInput(action.Id))
                AskExtractFolder(action, menu.SourcePaths);
            else if (FileJobs.NeedsNewPassword(action.Id))
                AskNewPassword(action, menu.SourcePaths);
            else if (FileJobs.NeedsPartSize(action.Id))
                AskPartSize(action, menu.SourcePaths);
            else if (FileJobs.NeedsPageRange(action.Id))
                AskPageRange(action, menu.SourcePaths[0]);
            // Отрисовка и распознавание PDF — средствами Windows, поэтому задачи живут здесь, а не в ядре.
            else if (action.Id == ActionIds.PdfToImages)
                StartJob(action, menu.SourcePaths, _ => new PdfImagesJob(_settings.Jobs.OutputFolder));
            else if (action.Id == ActionIds.PdfOcr)
                StartJob(action, menu.SourcePaths, _ => new PdfOcrJob(_settings.Jobs.OutputFolder));
            else
                StartJob(action, menu.SourcePaths);
        };
        _radial = radial;
        radial.Show();
    }

    /// <summary>«Разбить на части»: выбор размера части (для пакета — по самому большому архиву), затем окно выполнения.</summary>
    private void AskPartSize(DialAction action, IReadOnlyList<string> files)
    {
        var largest = files.Max(f => new FileInfo(f).Length);
        var title = files.Count == 1 ? Path.GetFileName(files[0]) : Lang.T.ShellArchivesCount(files.Count);
        if (SplitWindow.Ask(title, largest) is not { } partSize)
            return;
        var output = _settings.Jobs.OutputFolder;
        StartJob(action, files, _ => new FileSplitJob(partSize, output));
    }

    /// <summary>«Защитить паролем» (архив, PDF, документ): пароль вводится дважды, затем обычное окно выполнения.</summary>
    private void AskNewPassword(DialAction action, IReadOnlyList<string> files)
    {
        var name = files.Count == 1 ? Path.GetFileName(files[0]) : SelectionInterpreter.KindCount(FileKinds.Of(files[0]), files.Count);
        if (PasswordWindow.AskNew(name, files[0]) is not { } password)
            return;
        var output = _settings.Jobs.OutputFolder;
        StartJob(action, files, _ => action.Id switch
        {
            ActionIds.PdfProtect => new PdfProtectJob(password, output),
            ActionIds.DocProtect => new DocumentProtectJob(password, output),
            _ => new ArchiveProtectJob(password, output),
        });
    }

    /// <summary>«Извлечь страницы»: запись страниц, затем обычное окно выполнения.</summary>
    private void AskPageRange(DialAction action, string pdf)
    {
        var count = PdfFiles.TryPageCount(pdf, TimeSpan.FromSeconds(5));
        if (PagesWindow.Ask(Path.GetFileName(pdf), count) is not { } pages)
            return;
        var output = _settings.Jobs.OutputFolder;
        StartJob(action, [pdf], _ => new PdfExtractJob(pages, output));
    }

    /// <summary>«Распаковать в…»: сначала выбор папки, потом обычное окно выполнения (для пакета — одна папка на всех).</summary>
    private void AskExtractFolder(DialAction action, IReadOnlyList<string> archives)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Lang.T.ShellExtractWhere,
            InitialDirectory = Path.GetDirectoryName(archives[0]),
        };
        if (dialog.ShowDialog() != true)
            return;
        var folder = dialog.FolderName;
        StartJob(action, archives, _ => new ArchiveExtractJob(ExtractMode.ToChosen, folder));
    }

    /// <summary>Обрезка: сначала окно с границами, потом обычное окно выполнения.</summary>
    private void AskTrimRange(DialAction action, string sourcePath)
    {
        var settings = _settings;
        var dialog = new TrimWindow(sourcePath, WindowPlacement.CursorPosition(), settings.FfmpegFolder);
        dialog.Confirmed += request =>
            StartJob(action, [sourcePath], tools => FileJobs.CreateTrim(tools!, sourcePath, request, settings.Jobs));
        dialog.Show();
        dialog.Activate();
    }

    private void StartJob(DialAction action, IReadOnlyList<string> sourcePaths, Func<FfmpegTools?, IFileJob>? jobFactory = null)
    {
        var sourcePath = sourcePaths[0];
        // То же действие над тем же файлом параллельно бессмысленно — показываем уже идущее.
        var running = _jobs.FirstOrDefault(j => j.IsBusy && j.ActionId == action.Id &&
            string.Equals(j.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (running is not null)
        {
            running.Activate();
            ToastWindow.ShowAt(Lang.T.ShellAlreadyRunningForFile, WindowPlacement.CursorPosition());
            return;
        }

        var effective = _settings with { WhisperModel = SpeechModels.FindDefault(_settings.WhisperModel) };
        var window = new JobWindow(action.Id, action.Title, sourcePaths, WindowPlacement.CursorPosition(), effective,
            jobFactory);
        window.Closed += (_, _) => _jobs.Remove(window);
        window.Finished += RecordHistory;
        _jobs.Add(window);
        window.Show();
        window.Activate();
    }

    private void ExitApp()
    {
        // Отменяем активные задачи: процессы ffmpeg завершаются, временные файлы удаляются.
        foreach (var job in _jobs.ToList())
            job.CancelAndClose();

        if (_jobs.Count == 0)
        {
            Shutdown();
            return;
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        var deadline = DateTime.UtcNow.AddSeconds(10);
        timer.Tick += (_, _) =>
        {
            if (_jobs.Count == 0 || DateTime.UtcNow > deadline)
            {
                timer.Stop();
                Shutdown();
            }
        };
        timer.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        _singleInstance?.Dispose();
        _exitSignal?.Dispose();
        _dialSignal?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("UI", e.Exception);
        e.Handled = true; // ошибка не должна ронять приложение из трея
    }
}
