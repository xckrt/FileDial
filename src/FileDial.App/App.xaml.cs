using System.Windows;
using System.Windows.Threading;
using FileDial.App.Hotkey;
using FileDial.App.Interop;
using FileDial.App.Selection;
using FileDial.App.Tray;
using FileDial.App.UI;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Selection;

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

        _hotkey = new HotkeyManager();
        var error = _hotkey.Register();
        if (error != 0)
        {
            var reason = error == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"Сочетание {HotkeyManager.DisplayName} уже занято другой программой."
                : $"Не удалось зарегистрировать {HotkeyManager.DisplayName} (код ошибки {error}).";
            MessageBox.Show(
                $"{reason}\n\nЗакройте программу, которая использует это сочетание, и запустите FileDial снова.",
                "FileDial", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        _hotkey.Pressed += OnHotkeyPressed;
        _tray = new TrayIcon(HotkeyManager.DisplayName, ShellActions.OpenReadme, ExitApp);
    }

    private async void OnHotkeyPressed(object? sender, EventArgs e)
    {
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
            var outcome = await Task.Run(() =>
            {
                var tools = FfmpegLocator.LocateDefault();
                var media = tools is null ? null : new FfprobeMediaProbe(tools, MediaProbeTimeout);
                return SelectionInterpreter.Interpret(selection, new FileSystemProbe(), media);
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
            if (radial.Chosen is { } action && menu.SourcePath is not null && FileJobs.IsImplemented(action.Id))
                StartJob(action, menu.SourcePath);
        };
        _radial = radial;
        radial.Show();
    }

    private void StartJob(DialAction action, string sourcePath)
    {
        // То же действие над тем же файлом параллельно бессмысленно — показываем уже идущее.
        var running = _jobs.FirstOrDefault(j => j.IsBusy && j.ActionId == action.Id &&
            string.Equals(j.SourcePath, sourcePath, StringComparison.OrdinalIgnoreCase));
        if (running is not null)
        {
            running.Activate();
            ToastWindow.ShowAt("Это уже выполняется для этого файла", WindowPlacement.CursorPosition());
            return;
        }

        var window = new JobWindow(action.Id, action.Label, sourcePath, WindowPlacement.CursorPosition());
        window.Closed += (_, _) => _jobs.Remove(window);
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
