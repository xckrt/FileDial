using System.Globalization;
using System.Runtime.Versioning;
using FileDial.Core.Localization;
using FileDial.Core.Settings;
using FileDial.Core.Text;
using Microsoft.Win32;

namespace FileDial.Core.Setup;

/// <summary>Где установщик оставляет следы. В тестах — временные папки и отдельный раздел реестра.</summary>
[SupportedOSPlatform("windows")]
public sealed record InstallEnvironment
{
    /// <summary>Запись в «Приложениях» Windows (относительно HKCU).</summary>
    public required string UninstallKey { get; init; }
    public required string RunKey { get; init; }
    public string RunValue { get; init; } = "FileDial";
    public required string StartMenuFolder { get; init; }
    public required string DesktopFolder { get; init; }
    public required string SettingsPath { get; init; }
    /// <summary>Настройки, история, ffmpeg, модели — удаляются только по галочке.</summary>
    public required IReadOnlyList<string> DataFolders { get; init; }

    public static InstallEnvironment Current { get; } = new()
    {
        UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\FileDial",
        RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run",
        StartMenuFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        DesktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        SettingsPath = SettingsStore.DefaultPath,
        DataFolders =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileDial"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileDial"),
        ],
    };
}

public sealed record InstallOptions(
    string Directory,
    bool Autostart = true,
    bool StartMenuShortcut = true,
    bool DesktopShortcut = false,
    string? Theme = null)
{
    /// <summary>Справка на всех языках для папки программы; по умолчанию — README*.md рядом с установщиком.</summary>
    public IReadOnlyList<string>? ReadmePaths { get; init; }
}

[SupportedOSPlatform("windows")]
public sealed record InstalledApp(string Directory, string Version)
{
    public string ExePath => Path.Combine(Directory, Installer.ExeName);
}

public sealed record InstallProgress(string Stage, double Fraction);

/// <summary>
/// Установка для текущего пользователя, без прав администратора: программа — в выбранную папку,
/// запись в «Приложениях» и автозапуск — в HKCU, ярлыки — в меню «Пуск» и на рабочем столе пользователя.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Installer(InstallEnvironment environment)
{
    public const string ExeName = "FileDial.exe";
    public const string ShortcutName = "FileDial.lnk";
    /// <summary>Справка: английская и русская.</summary>
    private static readonly string[] ReadmeNames = ["README.md", "README.ru.md"];
    private const long Margin = 50_000_000;

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FileDial");

    /// <summary>Выбрали «D:\Программы» — ставим в «D:\Программы\FileDial», а не прямо в общую папку.</summary>
    public static string NormalizeDirectory(string chosen)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(chosen));
        return string.Equals(Path.GetFileName(full), "FileDial", StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, "FileDial");
    }

    /// <summary>Почему в эту папку ставить нельзя; null — можно.</summary>
    public static string? WhyNot(string directory, long payloadBytes)
    {
        if (!Path.IsPathFullyQualified(directory))
            return Lang.T.InstallNeedFullPath;
        if (directory.StartsWith(@"\\", StringComparison.Ordinal))
            return Lang.T.InstallNoNetwork;

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length > 0 && IsInside(directory, windows))
            return Lang.T.InstallNotWindowsFolder;

        var root = Path.GetPathRoot(directory)!;
        DriveInfo drive;
        try
        {
            drive = new DriveInfo(root);
            if (!drive.IsReady)
                return Lang.T.InstallDriveNotReady;
        }
        catch (ArgumentException)
        {
            return Lang.T.InstallNoDrive;
        }

        // Пишем пробный файл в ближайшую существующую папку: так видно права ещё до установки.
        var existing = directory;
        while (!Directory.Exists(existing))
            existing = Path.GetDirectoryName(existing) ?? root;
        try
        {
            var probe = Path.Combine(existing, $".filedial-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Lang.T.InstallNoWriteAccess;
        }

        var needed = payloadBytes + Margin;
        return drive.AvailableFreeSpace < needed
            ? Lang.T.InstallLowSpace(SizeFormatter.Format(needed), SizeFormatter.Format(drive.AvailableFreeSpace))
            : null;
    }

    private static bool IsInside(string path, string folder)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + Path.DirectorySeparatorChar;
        var f = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Уже установленная копия (по записи в «Приложениях»); null — не установлена или папку удалили вручную.</summary>
    public InstalledApp? FindInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(environment.UninstallKey);
        if (key?.GetValue("InstallLocation") is not string directory || directory.Length == 0)
            return null;
        var app = new InstalledApp(directory, key.GetValue("DisplayVersion") as string ?? "");
        return File.Exists(app.ExePath) ? app : null;
    }

    /// <summary>Включён ли автозапуск именно этой копии.</summary>
    public bool AutostartPointsTo(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(environment.RunKey);
        return key?.GetValue(environment.RunValue) is string value &&
               value.Trim('"').Equals(exePath, StringComparison.OrdinalIgnoreCase);
    }

    public bool ShortcutExists(bool desktop, string exePath) =>
        ShellLink.TargetOf(ShortcutPath(desktop)) is { } target && SamePath(target, exePath);

    private string ShortcutPath(bool desktop) =>
        Path.Combine(desktop ? environment.DesktopFolder : environment.StartMenuFolder, ShortcutName);

    /// <summary>Установка или обновление. Возвращает путь к установленному exe.</summary>
    public async Task<string> InstallAsync(string sourceExe, string version, InstallOptions options,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var directory = options.Directory;
        var exe = Path.Combine(directory, ExeName);
        Directory.CreateDirectory(directory);

        // 1. Программа: копия во временный файл рядом, затем замена — недокопированный exe не остаётся.
        if (!SamePath(sourceExe, exe))
        {
            var temp = exe + ".new";
            try
            {
                await CopyAsync(sourceExe, temp, f => progress?.Report(new InstallProgress(Lang.T.InstallStageCopying, f * 0.85)), ct)
                    .ConfigureAwait(false);
                await ReplaceAsync(temp, exe, ct).ConfigureAwait(false);
            }
            finally
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
        ct.ThrowIfCancellationRequested();

        var readmes = options.ReadmePaths ?? ReadmeNames.Select(n => Path.Combine(Path.GetDirectoryName(sourceExe)!, n)).ToList();
        foreach (var readme in readmes.Where(File.Exists))
        {
            var target = Path.Combine(directory, Path.GetFileName(readme));
            if (!SamePath(readme, target))
                File.Copy(readme, target, overwrite: true);
        }

        // 2. Запись в «Приложениях»: оттуда же — удаление.
        progress?.Report(new InstallProgress(Lang.T.InstallStageRegistering, 0.9));
        using (var key = Registry.CurrentUser.CreateSubKey(environment.UninstallKey, writable: true))
        {
            key.SetValue("DisplayName", "FileDial");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "FileDial");
            key.SetValue("Comments", Lang.T.InstallDescription);
            key.SetValue("InstallLocation", directory);
            key.SetValue("DisplayIcon", exe + ",0");
            key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        // 3. Автозапуск и ярлыки — как выбрано (при обновлении снятая галочка убирает прежний).
        progress?.Report(new InstallProgress(Lang.T.InstallStageShortcuts, 0.95));
        using (var run = Registry.CurrentUser.CreateSubKey(environment.RunKey, writable: true))
        {
            if (options.Autostart)
                run.SetValue(environment.RunValue, $"\"{exe}\"");
            else
                run.DeleteValue(environment.RunValue, throwOnMissingValue: false);
        }
        SetShortcut(desktop: false, options.StartMenuShortcut, exe);
        SetShortcut(desktop: true, options.DesktopShortcut, exe);

        // 4. Тема, выбранная в установщике, — в настройки (остальные настройки при обновлении не трогаем).
        if (options.Theme is { } theme)
        {
            var settings = SettingsStore.Load(environment.SettingsPath);
            if (settings.Theme != theme)
                SettingsStore.Save(environment.SettingsPath, settings with { Theme = theme });
        }

        progress?.Report(new InstallProgress(Lang.T.InstallDone, 1));
        return exe;
    }

    private void SetShortcut(bool desktop, bool wanted, string exe)
    {
        var path = ShortcutPath(desktop);
        if (wanted)
            ShellLink.Create(path, exe, Lang.T.InstallDescription);
        else if (ShellLink.TargetOf(path) is { } target && string.Equals(Path.GetFileName(target), ExeName, StringComparison.OrdinalIgnoreCase))
            File.Delete(path);
    }

    private static async Task CopyAsync(string source, string target, Action<double> report, CancellationToken ct)
    {
        const int chunk = 4 * 1024 * 1024;
        await using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, chunk, useAsync: true);
        await using var to = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, chunk, useAsync: true);
        var buffer = new byte[chunk];
        long copied = 0;
        var total = Math.Max(1, from.Length);
        int read;
        while ((read = await from.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await to.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            copied += read;
            report((double)copied / total);
        }
    }

    /// <summary>Замена exe: только что закрытая программа может ещё пару секунд держать файл.</summary>
    private static async Task ReplaceAsync(string temp, string exe, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, exe, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Сколько занимают настройки, история, ffmpeg и модели.</summary>
    public long DataSize() => environment.DataFolders.Where(Directory.Exists)
        .Sum(d => new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length));

    /// <summary>
    /// Удаление: автозапуск, ярлыки и запись в «Приложениях» — если они указывают на эту копию; файлы программы;
    /// по желанию — данные. Возвращает то, что сейчас удалить нельзя (запущенный exe), — это удалит отложенная команда.
    /// </summary>
    public IReadOnlyList<string> Uninstall(InstalledApp app, bool removeData, IProgress<InstallProgress>? progress = null)
    {
        progress?.Report(new InstallProgress(Lang.T.InstallStageAutostart, 0.2));
        using (var run = Registry.CurrentUser.OpenSubKey(environment.RunKey, writable: true))
            if (run is not null && AutostartPointsTo(app.ExePath))
                run.DeleteValue(environment.RunValue, throwOnMissingValue: false);
        foreach (var desktop in new[] { false, true })
            if (ShortcutExists(desktop, app.ExePath))
                File.Delete(ShortcutPath(desktop));

        progress?.Report(new InstallProgress(Lang.T.InstallStageAppsEntry, 0.4));
        Registry.CurrentUser.DeleteSubKeyTree(environment.UninstallKey, throwOnMissingSubKey: false);

        if (removeData)
        {
            progress?.Report(new InstallProgress(Lang.T.InstallStageData, 0.6));
            foreach (var folder in environment.DataFolders.Where(Directory.Exists))
            {
                // Программу, поставленную внутрь папки данных, удалит шаг ниже — здесь её не трогаем.
                if (IsInside(app.Directory, folder))
                    continue;
                try { Directory.Delete(folder, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        progress?.Report(new InstallProgress(Lang.T.InstallStageFiles, 0.8));
        // Удаляем только своё: в папке, выбранной пользователем, могут лежать и чужие файлы.
        var leftovers = new List<string>();
        foreach (var file in ReadmeNames.Select(n => Path.Combine(app.Directory, n)).Prepend(app.ExePath))
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                leftovers.Add(file);
            }
        }
        if (leftovers.Count == 0)
            TryRemoveEmpty(app.Directory);
        progress?.Report(new InstallProgress(Lang.T.InstallDone, 1));
        return leftovers;
    }

    private static void TryRemoveEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
