using System.Windows;
using System.Windows.Threading;
using FileDial.App.Hotkey;
using FileDial.App.Interop;
using FileDial.App.Selection;
using FileDial.App.Settings;
using FileDial.App.Tray;
using FileDial.App.UI;
using FileDial.Core.Compression;
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
    private HotkeyManager? _hotkey;
    private TrayIcon? _tray;
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

        _singleInstance = new Mutex(true, @"Local\FileDial.Prototype", out var created);
        if (!created)
        {
            MessageBox.Show("FileDial уже запущен — значок в области уведомлений.", "FileDial",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _settings = SettingsStore.Load(SettingsStore.DefaultPath);
        Motion.UserEnabled = _settings.Animations;

        _ = RefreshSpeechSupportAsync();

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += OnHotkeyPressed;
        var gesture = _settings.HotkeyGesture;
        var error = _hotkey.Register(gesture);
        _tray = new TrayIcon(gesture.Format(), OpenSettings, OpenHistory, ShellActions.OpenReadme, ExitApp);

        if (error != 0)
        {
            // Без клавиши программа бесполезна, но выходить не нужно: даём сразу выбрать другое сочетание.
            _tray.SetHotkey(null);
            MessageBox.Show(
                $"{HotkeyManager.Describe(error, gesture)}\n\nВыберите другое сочетание в настройках " +
                "или закройте программу, которая его использует.",
                "FileDial", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenSettings();
        }
    }

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

    private void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_settings, Autostart.IsEnabled(), WindowPlacement.CursorPosition(), ApplySettings);
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
            return HotkeyManager.Describe(error, gesture) + " Выберите другое.";

        if (autostart != Autostart.IsEnabled() && Autostart.Set(autostart) is { } autostartError)
            return autostartError;

        try
        {
            SettingsStore.Save(SettingsStore.DefaultPath, settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Сохранение настроек", ex);
            return "Не удалось сохранить настройки: нет доступа к папке настроек.";
        }

        _settings = settings.Normalized();
        Motion.UserEnabled = _settings.Animations;
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
                () => SelectionResult.Fail("Проводник не отвечает"));

            // Проверка файла и ffprobe могут занять время (сетевой диск, процесс) — не в UI-потоке.
            var settings = _settings;
            var speechFilter = _speechFilter;
            var outcome = await Task.Run(() =>
            {
                var tools = FfmpegLocator.LocateDefault(settings.FfmpegFolder);
                var media = tools is null ? null : new FfprobeMediaProbe(tools, MediaProbeTimeout);
                return SelectionInterpreter.Interpret(selection, new FileSystemProbe(), media,
                    settings.Jobs with { SpeechFilterAvailable = speechFilter });
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
            ToastWindow.ShowAt("Что-то пошло не так. Подробности в журнале ошибок.", cursor);
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
            if (radial.Chosen is not { } action || menu.SourcePaths.Count == 0 || !FileJobs.IsImplemented(action.Id))
                return;
            if (FileJobs.NeedsTrimInput(action.Id) && !menu.IsBatch)
                AskTrimRange(action, menu.SourcePaths[0]);
            else
                StartJob(action, menu.SourcePaths);
        };
        _radial = radial;
        radial.Show();
    }

    /// <summary>Обрезка: сначала окно с границами, потом обычное окно выполнения.</summary>
    private void AskTrimRange(DialAction action, string sourcePath)
    {
        var settings = _settings;
        var dialog = new TrimWindow(sourcePath, WindowPlacement.CursorPosition(), settings.FfmpegFolder);
        dialog.Confirmed += request =>
            StartJob(action, [sourcePath], tools => FileJobs.CreateTrim(tools, sourcePath, request, settings.Jobs));
        dialog.Show();
        dialog.Activate();
    }

    private void StartJob(DialAction action, IReadOnlyList<string> sourcePaths, Func<FfmpegTools, IFileJob>? jobFactory = null)
    {
        var sourcePath = sourcePaths[0];
        // То же действие над тем же файлом параллельно бессмысленно — показываем уже идущее.
        var running = _jobs.FirstOrDefault(j => j.IsBusy && j.ActionId == action.Id &&
            string.Equals(j.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (running is not null)
        {
            running.Activate();
            ToastWindow.ShowAt("Это уже выполняется для этого файла", WindowPlacement.CursorPosition());
            return;
        }

        var window = new JobWindow(action.Id, action.Title, sourcePaths, WindowPlacement.CursorPosition(), _settings,
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
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("UI", e.Exception);
        e.Handled = true; // ошибка не должна ронять приложение из трея
    }
}
