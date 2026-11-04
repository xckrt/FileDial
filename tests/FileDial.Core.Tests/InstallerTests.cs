using System.Runtime.Versioning;
using FileDial.Core.Localization;
using FileDial.Core.Settings;
using FileDial.Core.Setup;
using Microsoft.Win32;

namespace FileDial.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class InstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialSetup_" + Guid.NewGuid().ToString("N"));
    private readonly string _registry = @"Software\FileDialTests\" + Guid.NewGuid().ToString("N");
    private readonly InstallEnvironment _env;
    private readonly Installer _installer;
    private readonly string _source;

    public InstallerTests()
    {
        Directory.CreateDirectory(_root);
        _env = new InstallEnvironment
        {
            UninstallKey = _registry + @"\Uninstall\FileDial",
            RunKey = _registry + @"\Run",
            ClassesKey = _registry + @"\Classes",
            StartMenuFolder = Path.Combine(_root, "Пуск"),
            DesktopFolder = Path.Combine(_root, "Рабочий стол"),
            SettingsPath = Path.Combine(_root, "Roaming", "FileDial", "settings.json"),
            DataFolders = [Path.Combine(_root, "Roaming", "FileDial"), Path.Combine(_root, "Local", "FileDial")],
        };
        Directory.CreateDirectory(_env.DesktopFolder);
        _installer = new Installer(_env);

        // «Установщик»: exe и README рядом, как в dist.
        var dist = Directory.CreateDirectory(Path.Combine(_root, "dist")).FullName;
        _source = Path.Combine(dist, "FileDial-Setup-1.0.exe");
        File.WriteAllBytes(_source, Enumerable.Range(0, 5_000_000).Select(i => (byte)i).ToArray());
        File.WriteAllText(Path.Combine(dist, "README.md"), "readme");
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\FileDialTests", throwOnMissingSubKey: false);
        Directory.Delete(_root, recursive: true);
    }

    private string Target => Path.Combine(_root, "Программы", "FileDial");

    private object? Value(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name);
    }

    [Theory]
    [InlineData(@"D:\Программы", @"D:\Программы\FileDial")]
    [InlineData(@"D:\Программы\", @"D:\Программы\FileDial")]
    [InlineData(@"D:\Apps\filedial", @"D:\Apps\filedial")]
    [InlineData(@"D:\", @"D:\FileDial")]
    public void NormalizeDirectory_AppendsAppFolder(string chosen, string expected) =>
        Assert.Equal(expected, Installer.NormalizeDirectory(chosen));

    [Fact]
    public void WhyNot_ExplainsBadFolders()
    {
        Assert.Null(Installer.WhyNot(Target, 1000));
        Assert.Contains("Сетевые", Installer.WhyNot(@"\\server\share\FileDial", 1000));
        Assert.Contains("Windows", Installer.WhyNot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "FileDial"), 1000));
        Assert.Contains("Мало места", Installer.WhyNot(Target, long.MaxValue / 2));
        Assert.Contains("полный путь", Installer.WhyNot(@"FileDial", 1000));
    }

    [Fact]
    public async Task Install_CopiesRegistersAndCreatesShortcuts()
    {
        var progress = new List<double>();
        var exe = await _installer.InstallAsync(_source, "1.0",
            new InstallOptions(Target, Autostart: true, StartMenuShortcut: true, DesktopShortcut: true, Theme: "brass"),
            new SyncProgress(p => progress.Add(p.Fraction)), CancellationToken.None);

        Assert.Equal(Path.Combine(Target, "FileDial.exe"), exe);
        Assert.Equal(File.ReadAllBytes(_source), File.ReadAllBytes(exe));
        Assert.True(File.Exists(Path.Combine(Target, "README.md")));
        Assert.False(File.Exists(exe + ".new"));
        Assert.Equal(1, progress[^1]);
        Assert.True(progress.SequenceEqual(progress.Order()), "прогресс не идёт назад");

        Assert.Equal($"\"{exe}\" --uninstall", Value(_env.UninstallKey, "UninstallString"));
        Assert.Equal("1.0", Value(_env.UninstallKey, "DisplayVersion"));
        Assert.True(_installer.AutostartPointsTo(exe));
        Assert.True(_installer.ShortcutExists(desktop: false, exe));
        Assert.True(_installer.ShortcutExists(desktop: true, exe));
        Assert.Equal("brass", SettingsStore.Load(_env.SettingsPath).Theme);
        Assert.Equal(new InstalledApp(Target, "1.0"), _installer.FindInstalled());
    }

    [Fact]
    public async Task Update_KeepsSettings_AppliesUncheckedOptions()
    {
        SettingsStore.Save(_env.SettingsPath, new AppSettings { Hotkey = "Ctrl+Shift+D" });
        await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target, DesktopShortcut: true), null, CancellationToken.None);
        await _installer.InstallAsync(_source, "1.1",
            new InstallOptions(Target, Autostart: false, DesktopShortcut: false, Theme: "paper"), null, CancellationToken.None);

        var exe = Path.Combine(Target, "FileDial.exe");
        Assert.False(_installer.AutostartPointsTo(exe));
        Assert.False(File.Exists(Path.Combine(_env.DesktopFolder, "FileDial.lnk")));
        Assert.True(_installer.ShortcutExists(desktop: false, exe));
        var settings = SettingsStore.Load(_env.SettingsPath);
        Assert.Equal("Ctrl+Shift+D", settings.Hotkey); // своё сочетание пользователя не сбрасывается
        Assert.Equal("paper", settings.Theme);
        Assert.Equal("1.1", _installer.FindInstalled()!.Version);
    }

    [Fact]
    public async Task Cancel_LeavesNoHalfCopiedExe()
    {
        using var cts = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _installer.InstallAsync(_source, "1.0",
            new InstallOptions(Target), new SyncProgress(_ => cts.Cancel()), cts.Token));
        Assert.False(File.Exists(Path.Combine(Target, "FileDial.exe")));
        Assert.False(File.Exists(Path.Combine(Target, "FileDial.exe.new")));
        Assert.Null(_installer.FindInstalled());
    }

    [Fact]
    public async Task Uninstall_RemovesOnlyOwnFiles_DataByChoice()
    {
        await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target, DesktopShortcut: true, Theme: "brass"),
            null, CancellationToken.None);
        File.WriteAllText(Path.Combine(Target, "чужой.txt"), "не наш");
        var app = _installer.FindInstalled()!;

        var leftovers = _installer.Uninstall(app, removeData: false);

        Assert.Empty(leftovers);
        Assert.False(File.Exists(app.ExePath));
        Assert.True(File.Exists(Path.Combine(Target, "чужой.txt")));
        Assert.Null(_installer.FindInstalled());
        Assert.Null(Value(_env.RunKey, "FileDial"));
        Assert.False(File.Exists(Path.Combine(_env.StartMenuFolder, "FileDial.lnk")));
        Assert.False(File.Exists(Path.Combine(_env.DesktopFolder, "FileDial.lnk")));
        Assert.True(File.Exists(_env.SettingsPath)); // настройки остались

        // Повторная установка и удаление с данными.
        File.Delete(Path.Combine(Target, "чужой.txt"));
        await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target), null, CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(_env.DataFolders[1], "ffmpeg"));
        Assert.True(_installer.DataSize() > 0);
        _installer.Uninstall(_installer.FindInstalled()!, removeData: true);
        Assert.False(Directory.Exists(_env.DataFolders[0]));
        Assert.False(Directory.Exists(_env.DataFolders[1]));
        Assert.False(Directory.Exists(Target)); // пустая папка программы удалена
    }

    [Fact]
    public async Task Uninstall_KeepsForeignAutostart()
    {
        await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target, Autostart: false), null, CancellationToken.None);
        using (var run = Registry.CurrentUser.CreateSubKey(_env.RunKey))
            run.SetValue("FileDial", @"""D:\portable\FileDial.exe""");
        _installer.Uninstall(_installer.FindInstalled()!, removeData: false);
        Assert.Equal(@"""D:\portable\FileDial.exe""", Value(_env.RunKey, "FileDial"));
    }

    [Fact]
    public async Task Uninstall_RunningExe_ReportedAsLeftover()
    {
        await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target), null, CancellationToken.None);
        var app = _installer.FindInstalled()!;
        // Открытый без общего доступа файл — как запущенный exe.
        using (new FileStream(app.ExePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal([app.ExePath], _installer.Uninstall(app, removeData: false));
        Assert.True(Directory.Exists(Target));
    }

    [Fact]
    public async Task Install_RegistersContextMenuForInstalledExe()
    {
        var exe = await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target), null, CancellationToken.None);

        foreach (var cls in new[] { "*", "Directory" })
        {
            var verb = $@"{_env.ClassesKey}\{cls}\shell\FileDial";
            Assert.Equal(Lang.T.CtxVerb, Value(verb, ""));
            Assert.Equal($"\"{exe}\",0", Value(verb, "Icon"));
            Assert.Equal("Player", Value(verb, "MultiSelectModel"));
            Assert.Equal($"\"{exe}\" --dial", Value(verb + @"\command", ""));
        }
        Assert.True(_installer.ContextMenuPointsTo(exe));
        Assert.True(SettingsStore.Load(_env.SettingsPath).ContextMenu);
    }

    [Fact]
    public async Task ContextMenu_Register_IsIdempotent_AndFollowsExe()
    {
        ContextMenuEntry.Register(_env.ClassesKey, @"D:\portable\FileDial.exe", "Old");
        var exe = await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target), null, CancellationToken.None);
        await _installer.InstallAsync(_source, "1.1", new InstallOptions(Target), null, CancellationToken.None);

        Assert.True(_installer.ContextMenuPointsTo(exe)); // переносная копия уступила установленной
        Assert.Equal(Lang.T.CtxVerb, Value($@"{_env.ClassesKey}\Directory\shell\FileDial", ""));
    }

    [Fact]
    public async Task Update_UncheckedContextMenu_RemovesEntry_AndRemembersChoice()
    {
        var exe = await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target), null, CancellationToken.None);
        await _installer.InstallAsync(_source, "1.1", new InstallOptions(Target, ContextMenu: false), null, CancellationToken.None);

        Assert.False(_installer.ContextMenuPointsTo(exe));
        Assert.Null(Value($@"{_env.ClassesKey}\*\shell\FileDial\command", ""));
        Assert.Null(Value($@"{_env.ClassesKey}\Directory\shell\FileDial", ""));
        // Программа сверяет пункт с настройкой при запуске — снятая галочка не должна вернуться.
        Assert.False(SettingsStore.Load(_env.SettingsPath).ContextMenu);
    }

    [Fact]
    public async Task Uninstall_RemovesOwnContextMenu_KeepsForeign()
    {
        await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target), null, CancellationToken.None);
        _installer.Uninstall(_installer.FindInstalled()!, removeData: false);
        Assert.Null(ContextMenuEntry.TargetOf(_env.ClassesKey));
        Assert.Null(Value($@"{_env.ClassesKey}\Directory\shell\FileDial", ""));

        await _installer.InstallAsync(_source, "1.0", new InstallOptions(Target), null, CancellationToken.None);
        ContextMenuEntry.Register(_env.ClassesKey, @"D:\portable\FileDial.exe", "FileDial");
        _installer.Uninstall(_installer.FindInstalled()!, removeData: false);
        Assert.Equal(@"D:\portable\FileDial.exe", ContextMenuEntry.TargetOf(_env.ClassesKey));
    }

    private sealed class SyncProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }
}
