using System.Windows;
using FileDial.App.UI;
using FileDial.Core.Localization;
using FileDial.Core.Settings;
using FileDial.Core.Setup;

namespace FileDial.App.Setup;

/// <summary>
/// Один exe — и программа, и установщик. Режим выбирается по имени файла и ключам:
/// «FileDial-Setup-….exe» или <c>--install</c> — установка, <c>--uninstall</c> — удаление (так его зовут «Приложения»),
/// иначе — обычная работа в трее. <c>--quiet</c> — без окон (для администраторов): <c>--install --quiet [--dir папка] [--start]</c>;
/// <c>--start</c> — запустить установленную программу (так ставится обновление из трея).
/// </summary>
internal static class SetupLauncher
{
    public enum Mode { Install, Uninstall }

    public static Mode? Detect(IReadOnlyList<string> args, string? processPath)
    {
        if (Has(args, "--uninstall"))
            return Mode.Uninstall;
        if (Has(args, "--install"))
            return Mode.Install;
        var name = Path.GetFileNameWithoutExtension(processPath ?? "");
        return name.Contains("setup", StringComparison.OrdinalIgnoreCase) ? Mode.Install : null;
    }

    public static string Version
    {
        get
        {
            var v = typeof(SetupLauncher).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    public static void Run(Application app, Mode mode, IReadOnlyList<string> args)
    {
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Installer.ExeName);
        var installer = new Installer(InstallEnvironment.Current);
        var installed = installer.FindInstalled();

        if (Has(args, "--quiet"))
        {
            // В фоновом потоке: синхронное ожидание в UI-потоке заблокировало бы продолжения await.
            app.Shutdown(Task.Run(() => RunQuiet(installer, mode, exe, installed, args)).GetAwaiter().GetResult());
            return;
        }

        var settings = SettingsStore.Load(SettingsStore.DefaultPath);
        Motion.UserEnabled = settings.Animations;
        Palette.Apply(Theme.Resolve(settings.Theme));
        Lang.Set(settings.Language);

        // Удаление, запущенное не из «Приложений» (например, из папки рядом), — удаляем ту копию, из которой запущены.
        var target = mode == Mode.Uninstall
            ? installed ?? new InstalledApp(Path.GetDirectoryName(exe)!, Version)
            : installed;
        var window = new SetupWindow(mode == Mode.Uninstall, exe, Version, target);
        window.Closed += (_, _) => app.Shutdown();
        window.Show();
        window.Activate();
    }

    private static int RunQuiet(Installer installer, Mode mode, string exe, InstalledApp? installed, IReadOnlyList<string> args)
    {
        try
        {
            if (mode == Mode.Uninstall)
            {
                var app = installed ?? new InstalledApp(Path.GetDirectoryName(exe)!, Version);
                RunningCopies.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                ScheduleRemoval(installer.Uninstall(app, removeData: Has(args, "--remove-data")), app.Directory);
                return 0;
            }

            var directory = Value(args, "--dir") is { } dir ? Installer.NormalizeDirectory(dir)
                : installed?.Directory ?? Installer.DefaultDirectory;
            if (Installer.WhyNot(directory, new FileInfo(exe).Length) is { } problem)
            {
                ErrorLog.Write("Тихая установка", new InvalidOperationException(problem));
                return 2;
            }
            RunningCopies.StopAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            // Обновление сохраняет то, что пользователь уже выбрал (автозапуск, ярлыки); новая установка — по умолчанию.
            var options = installed is { } current
                ? new InstallOptions(directory,
                    Autostart: installer.AutostartPointsTo(current.ExePath) && !Has(args, "--no-autostart"),
                    StartMenuShortcut: installer.ShortcutExists(desktop: false, current.ExePath),
                    DesktopShortcut: installer.ShortcutExists(desktop: true, current.ExePath))
                : new InstallOptions(directory, Autostart: !Has(args, "--no-autostart"));
            var installedExe = installer.InstallAsync(exe, Version, options with { ReadmePaths = UI.ShellActions.AllReadmes() },
                null, CancellationToken.None).GetAwaiter().GetResult();
            if (Has(args, "--start"))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installedExe)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(installedExe)!,
                })?.Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Тихая установка", ex);
            return 1;
        }
    }

    /// <summary>
    /// Запущенный exe нельзя удалить самому себе: короткая команда в фоне дождётся выхода и уберёт его,
    /// а затем папку — только если в ней больше ничего нет.
    /// </summary>
    public static void ScheduleRemoval(IReadOnlyList<string> leftovers, string directory)
    {
        if (leftovers.Count == 0)
            return;
        var files = string.Join(" ", leftovers.Select(f => $"\"{f}\""));
        var command = $"/d /c ping 127.0.0.1 -n 3 >nul & del /f /q {files} & rmdir \"{directory}\"";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", command)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ErrorLog.Write("Удаление", ex);
        }
    }

    private static bool Has(IReadOnlyList<string> args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? Value(IReadOnlyList<string> args, string flag)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
