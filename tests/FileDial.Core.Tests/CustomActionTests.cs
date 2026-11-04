using System.Security.Cryptography;
using FileDial.Core.Compression;
using FileDial.Core.Custom;
using FileDial.Core.History;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Selection;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

public class CustomCommandTests
{
    [Theory]
    [InlineData("-i {file} -vf scale=-2:720 {out}", new[] { "-i", "{file}", "-vf", "scale=-2:720", "{out}" })]
    [InlineData("  a   b\tc  ", new[] { "a", "b", "c" })]
    [InlineData("-i \"my file.mp4\" -metadata \"title=Мой отпуск\"", new[] { "-i", "my file.mp4", "-metadata", "title=Мой отпуск" })]
    [InlineData("a\"b c\"d", new[] { "ab cd" })]
    [InlineData("\"say \"\"hi\"\"\" x", new[] { "say \"hi\"", "x" })]
    [InlineData("first \"\" last", new[] { "first", "", "last" })]
    [InlineData(@"C:\tools\x.exe ""C:\Program Files\""", new[] { @"C:\tools\x.exe", @"C:\Program Files\" })]
    [InlineData("", new string[0])]
    public void Tokenize_RespectsQuotes(string template, string[] expected) =>
        Assert.Equal(expected, CustomCommand.Tokenize(template));

    [Fact]
    public void UnclosedQuote_IsDetected()
    {
        Assert.True(CustomCommand.HasUnclosedQuote("-i \"{file}"));
        Assert.False(CustomCommand.HasUnclosedQuote("-i \"{file}\" \"a \"\"b\"\"\""));
    }

    [Fact]
    public void Build_SubstitutesPerToken_PathsWithSpacesAndCyrillicStayOneArgument()
    {
        var action = new CustomAction
        {
            Program = "{ffmpeg}",
            Arguments = "-i {file} -vf scale=-2:720 -metadata title={name}.{ext} -y {out} {dir}",
        };
        const string source = @"D:\Мои видео\отпуск 2024 & друзья.mp4";
        const string output = @"D:\Мои видео\отпуск 2024 & друзья_720p.mp4";

        var command = CustomCommand.Build(action, source, output, @"C:\ff mpeg\ffmpeg.exe");

        Assert.Equal(@"C:\ff mpeg\ffmpeg.exe", command.Program);
        Assert.Equal(
            ["-i", source, "-vf", "scale=-2:720", "-metadata", "title=отпуск 2024 & друзья.mp4", "-y", output, @"D:\Мои видео"],
            command.Arguments);
    }

    [Fact]
    public void Build_QuotesInFileNameCannotBreakOutOfArgument()
    {
        // Имя с кавычкой и пробелами не превращается в новые аргументы: подстановка — уже после разбора.
        var action = new CustomAction { Program = "tool.exe", Arguments = "--in {file} --flag" };
        const string evil = "C:\\a\\x\" --delete \"y.txt";

        var command = CustomCommand.Build(action, evil, null, null);

        Assert.Equal(["--in", evil, "--flag"], command.Arguments);
    }

    [Fact]
    public void UnknownVariables_AreLeftAsIs_AndReported()
    {
        var command = CustomCommand.Build(new CustomAction { Program = "x.exe", Arguments = "{fiel} {FILE}" }, @"C:\a\b.txt", null, null);
        Assert.Equal(["{fiel}", @"C:\a\b.txt"], command.Arguments);
        Assert.Equal(["fiel"], CustomCommand.UnknownVariables("{fiel} {file} {out}"));
    }

    [Fact]
    public void Output_NeverOverwrites_AndUsesSuffixAndExtension()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "Папка с пробелом");
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "клип.mov");
            File.WriteAllText(source, "x");
            var action = new CustomAction { OutputSuffix = "_720p", OutputExtension = ".mp4" };
            var exists = OutputNameGenerator.PathTaken;

            Assert.Equal(Path.Combine(dir, "клип_720p.mp4"), CustomCommand.OutputPath(action, source, null, exists));
            File.WriteAllText(Path.Combine(dir, "клип_720p.mp4"), "old");
            Assert.Equal(Path.Combine(dir, "клип_720p_2.mp4"), CustomCommand.OutputPath(action, source, null, exists));

            // Без добавки и расширения имя совпало бы с исходником — исходник занят всегда.
            Assert.Equal(Path.Combine(dir, "клип_2.mov"), CustomCommand.OutputPath(new CustomAction(), source, null, exists));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void Problem_ChecksNameProgramAndVariables()
    {
        var ok = new CustomAction { Name = "Хэш", Program = "certutil.exe", Arguments = "-hashfile {file} SHA256" };
        Assert.Null(CustomCommand.Problem(ok));
        Assert.Null(CustomCommand.Problem(ok with { Program = "certutil" })); // без .exe — тоже находится
        Assert.Null(CustomCommand.Problem(ok with { Program = "{ffmpeg}" }));
        Assert.NotNull(CustomCommand.Problem(ok with { Name = " " }));
        Assert.NotNull(CustomCommand.Problem(ok with { Program = "" }));
        Assert.NotNull(CustomCommand.Problem(ok with { Program = "no-such-program-filedial.exe" }));
        Assert.NotNull(CustomCommand.Problem(ok with { Program = @"C:\scripts\run.cmd" }));
        Assert.NotNull(CustomCommand.Problem(ok with { Program = "tools\\x.exe" })); // относительный путь
        Assert.NotNull(CustomCommand.Problem(ok with { Arguments = "{fiel}" }));
        Assert.NotNull(CustomCommand.Problem(ok with { Arguments = "\"{file}" }));
        Assert.NotNull(CustomCommand.Problem(ok with { OutputSuffix = "a/b" }));
        Assert.NotNull(CustomCommand.Problem(ok with { AppliesTo = CustomActions.ByExtension, Extensions = " , " }));
    }

    [Fact]
    public void ProgramLocator_FindsSystemPrograms()
    {
        var certutil = ProgramLocator.Find("certutil.exe");
        Assert.NotNull(certutil);
        Assert.True(Path.IsPathFullyQualified(certutil));
        Assert.Equal(certutil, ProgramLocator.Find("\"" + certutil + "\""), StringComparer.OrdinalIgnoreCase);
        Assert.Null(ProgramLocator.Find("cmd.bat"));
    }

    [Theory]
    [InlineData(CustomActions.Any, "", @"C:\a\x.psd", true)]
    [InlineData("video", "", @"C:\a\x.MKV", true)]
    [InlineData("video", "", @"C:\a\x.png", false)]
    [InlineData(CustomActions.ByExtension, "txt, *.MD; .log", @"C:\a\readme.md", true)]
    [InlineData(CustomActions.ByExtension, "tar.gz", @"C:\a\backup.tar.gz", true)]
    [InlineData(CustomActions.ByExtension, "txt", @"C:\a\notes.txt.bak", false)]
    public void Matches(string kind, string extensions, string path, bool expected) =>
        Assert.Equal(expected, new CustomAction { AppliesTo = kind, Extensions = extensions }.Matches(path));
}

public class CustomPlacementTests
{
    private sealed class StubProbe(Func<string, FileState> state) : IFileProbe
    {
        public FileState Probe(string path) => state(path);
    }

    private static readonly IFileProbe Files = new StubProbe(_ => new FileState(FileStateKind.File, 30_000_000));
    private static readonly IFileProbe Folders = new StubProbe(_ => new FileState(FileStateKind.Directory));

    private static CustomAction Custom(string name, string kind = CustomActions.Any, string extensions = "") => new()
    {
        Id = name,
        Name = name,
        AppliesTo = kind,
        Extensions = extensions,
        Program = "certutil.exe",
        Arguments = "-hashfile {file}",
    };

    private static DialMenu Menu(IReadOnlyList<string> paths, IReadOnlyList<CustomAction> customs, IFileProbe? probe = null)
    {
        var outcome = SelectionInterpreter.Interpret(SelectionResult.Success(paths), probe ?? Files, null,
            new JobSettings { CustomActions = customs });
        return Assert.IsType<HotkeyOutcome.ShowMenu>(outcome).Menu;
    }

    [Fact]
    public void FullVideoDial_LastPlainSectorBecomesMore_KeysStayEight()
    {
        var menu = Menu([@"C:\v\clip.mp4"], [Custom("В 720p", "video"), Custom("Хэш")]);

        Assert.Equal(ActionCatalog.SectorCount, menu.Actions.Count);
        // «Формат ›» — группа, поэтому «Ещё» встаёт на «Убрать звук» (7-й сектор), «Формат» остаётся на 8-м.
        var more = menu.Actions[6];
        Assert.Equal(CustomActionIds.More, more.Id);
        Assert.Equal("Ещё", more.Label);
        Assert.True(more.IsEnabled);
        Assert.Equal([ActionIds.Mute, "custom:В 720p", "custom:Хэш"], more.Children!.Select(c => c.Id));
        Assert.Equal(ActionIds.Convert, menu.Actions[7].Id);
        Assert.Equal(ActionIds.Compress25, menu.Actions[0].Id);
    }

    [Fact]
    public void ImageDial_FirstCustomTakesFreeEighthSector_SecondMakesMore()
    {
        // У картинок восьмой сектор свободен (повороты собраны в «Повернуть ›») — своё действие встаёт прямо в него.
        var one = Menu([@"C:\p\photo.png"], [Custom("Уменьшить", "image")]);
        Assert.Equal("custom:Уменьшить", one.Actions[7].Id);
        Assert.Equal(ActionIds.ImageToPdf, one.Actions[6].Id);

        var two = Menu([@"C:\p\photo.png"], [Custom("Уменьшить", "image"), Custom("Печать", "image")]);
        Assert.Equal(CustomActionIds.More, two.Actions[7].Id);
        Assert.Equal(["custom:Уменьшить", "custom:Печать"], two.Actions[7].Children!.Select(c => c.Id));
    }

    [Fact]
    public void NotMatchingKind_LeavesDialUntouched()
    {
        var plain = Menu([@"C:\p\photo.png"], []);
        var menu = Menu([@"C:\p\photo.png"], [Custom("Видео", "video"), Custom("Текст", CustomActions.ByExtension, "txt")]);
        Assert.Equal(plain.Actions.Select(a => a.Id), menu.Actions.Select(a => a.Id));
    }

    [Fact]
    public void Fan_IsCappedAtNine()
    {
        var customs = Enumerable.Range(1, 12).Select(i => Custom($"Д{i}")).ToList();
        var menu = Menu([@"C:\v\clip.mp4"], customs);

        var more = menu.Actions.Single(a => a.Id == CustomActionIds.More);
        Assert.Equal(CustomPlacement.MaxInFan, more.Children!.Count);
        Assert.Equal(ActionIds.Mute, more.Children[0].Id);
        // Угол веера тоже выдерживает девять секторов.
        _ = new Geometry.SubmenuGeometry(0, CustomPlacement.MaxInFan, 196, 264, 30);
    }

    [Fact]
    public void CompactDial_WithMatchingActions_BecomesDialWithZip()
    {
        var menu = Menu([@"C:\a\notes.dat"], [Custom("Блокнот"), Custom("Только dat", CustomActions.ByExtension, "dat"),
            Custom("Только bin", CustomActions.ByExtension, "bin")]);

        Assert.Equal(DialMenuKind.Actions, menu.Kind);
        Assert.Null(menu.QuickAction);
        Assert.Equal([ActionIds.CompressToZip, "custom:Блокнот", "custom:Только dat"], menu.Actions.Select(a => a.Id));
        Assert.Equal([@"C:\a\notes.dat"], menu.SourcePaths);
    }

    [Fact]
    public void CompactDial_ForFolders_StaysCompact()
    {
        var menu = Menu([@"C:\a\Папка"], [Custom("Блокнот")], Folders);
        Assert.Equal(DialMenuKind.Unsupported, menu.Kind);
        Assert.Equal(ActionIds.CompressToZip, menu.QuickAction!.Id);
    }

    [Fact]
    public void Batch_ShowsOnlyActionsMatchingEveryFile()
    {
        var menu = Menu([@"C:\a\1.dat", @"C:\a\2.bin"],
            [Custom("Любые"), Custom("Только dat", CustomActions.ByExtension, "dat"), Custom("Тексты", CustomActions.ByExtension, "dat bin")]);

        Assert.Equal([ActionIds.CompressToZip, "custom:Любые", "custom:Тексты"], menu.Actions.Select(a => a.Id));
        Assert.True(menu.IsBatch);
    }

    [Fact]
    public void FreeSectorsOverflow_LastFreeSectorBecomesMore()
    {
        var customs = Enumerable.Range(1, 9).Select(i => Custom($"Д{i}")).ToList();
        var menu = Menu([@"C:\a\notes.dat"], customs);

        Assert.Equal(ActionCatalog.SectorCount, menu.Actions.Count);
        Assert.Equal(ActionIds.CompressToZip, menu.Actions[0].Id);
        Assert.Equal("custom:Д6", menu.Actions[6].Id);
        Assert.Equal(["custom:Д7", "custom:Д8", "custom:Д9"], menu.Actions[7].Children!.Select(c => c.Id));
    }

    [Fact]
    public void MissingProgram_SectorDisabledWithReason()
    {
        var action = Custom("Нет программы") with { Program = "no-such-program-filedial.exe" };
        var sector = CustomPlacement.ToDialAction(action);
        Assert.False(sector.IsEnabled);
        Assert.Contains("no-such-program-filedial.exe", sector.DisabledReason);
        Assert.Equal("Нет программы", sector.Title);
    }

    [Fact]
    public void Find_ById()
    {
        var a = Custom("А");
        Assert.Same(a, CustomActions.Find([Custom("Б"), a], CustomActionIds.For(a)));
        Assert.Null(CustomActions.Find([a], ActionIds.Trim));
    }
}

public class CustomSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void RoundTrip_KeepsActionsAndCyrillic()
    {
        var path = Path.Combine(_dir, "settings.json");
        var action = new CustomAction
        {
            Id = "abc",
            Name = "В 720p",
            AppliesTo = "video",
            Program = "{ffmpeg}",
            Arguments = "-i {file} -vf \"scale=-2:720\" {out}",
            OutputSuffix = "_720p",
            OutputExtension = "mp4",
            ShowWindow = true,
        };
        SettingsStore.Save(path, new AppSettings { CustomActions = [action] });

        Assert.Contains("В 720p", File.ReadAllText(path));
        var loaded = SettingsStore.Load(path);
        Assert.Equal([action], loaded.CustomActions);
        Assert.Equal([action], loaded.Jobs.CustomActions);
    }

    [Fact]
    public void Normalize_DropsInvalid_TrimsAndFixesIds()
    {
        var good = new CustomAction { Id = "1", Name = " Хэш ", Program = " certutil.exe ", Arguments = " -hashfile {file} " };
        var list = CustomActions.Normalize(
        [
            good,
            null,
            good with { Id = "1", Name = "Дубликат id" },
            good with { Id = "", Name = "Без id" },
            good with { Name = "" },
            good with { Program = "" },
            good with { Program = "run.bat" },
            good with { AppliesTo = "music" },
            good with { AppliesTo = CustomActions.ByExtension, Extensions = "" },
            good with { OutputSuffix = "..\\x" },
            good with { Id = "ext", AppliesTo = "EXTENSIONS", Extensions = "*.TXT;md", OutputExtension = ".mp4" },
        ]);

        Assert.Equal(["Хэш", "Дубликат id", "Без id", "Хэш"], list.Select(a => a.Name));
        Assert.Equal("certutil.exe", list[0].Program);
        Assert.Equal("-hashfile {file}", list[0].Arguments);
        Assert.Equal(list.Count, list.Select(a => a.Id).Distinct().Count());
        Assert.All(list, a => Assert.NotEmpty(a.Id));
        Assert.Equal("txt, md", list[3].Extensions);
        Assert.Equal(CustomActions.ByExtension, list[3].AppliesTo);
        Assert.Equal("mp4", list[3].OutputExtension);
    }

    [Fact]
    public void Load_BrokenEntries_DoNotBreakSettings()
    {
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """
            { "Hotkey": "Ctrl+Alt+J", "CustomActions": [ null, { "Name": "x" }, { "Name": "ok", "Program": "notepad.exe", "Arguments": null } ] }
            """);

        var loaded = SettingsStore.Load(path);

        Assert.Equal("Ctrl+Alt+J", loaded.Hotkey);
        var single = Assert.Single(loaded.CustomActions);
        Assert.Equal("ok", single.Name);
        Assert.Equal("", single.Arguments);

        File.WriteAllText(path, """{ "CustomActions": null }""");
        Assert.Empty(SettingsStore.Load(path).CustomActions);
    }
}

/// <summary>Настоящий запуск безобидной программы Windows (certutil.exe) — без cmd.exe и без окна.</summary>
public class CustomActionJobTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "Отчёты & копии");
    private readonly string _source;
    private readonly byte[] _content = "Привет, FileDial!\r\n"u8.ToArray();

    public CustomActionJobTests()
    {
        Directory.CreateDirectory(_dir);
        _source = Path.Combine(_dir, "отчёт за 2024 (копия).txt");
        File.WriteAllBytes(_source, _content);
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static CustomAction Certutil(string arguments, string suffix = "", string extension = "") => new()
    {
        Id = "t",
        Name = "certutil",
        Program = "certutil.exe",
        Arguments = arguments,
        OutputSuffix = suffix,
        OutputExtension = extension,
    };

    private void AssertSourceUntouched(DateTime written)
    {
        Assert.Equal(_content, File.ReadAllBytes(_source));
        Assert.Equal(written, File.GetLastWriteTimeUtc(_source));
    }

    [Fact]
    public async Task WithoutOutput_RunsAndSucceeds()
    {
        var written = File.GetLastWriteTimeUtc(_source);
        var result = await new CustomActionJob(Certutil("-hashfile {file} SHA256"), null)
            .RunAsync(_source, null, CancellationToken.None);

        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Null(result.OutputPath);
        Assert.Contains("certutil.exe", result.Message);
        AssertSourceUntouched(written);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task WithOutput_CreatesNewFile_NeverOverwrites()
    {
        var written = File.GetLastWriteTimeUtc(_source);
        var taken = Path.Combine(_dir, "отчёт за 2024 (копия)_b64.txt");
        File.WriteAllText(taken, "чужой файл");

        var result = await new CustomActionJob(Certutil("-encode {file} {out}", "_b64", "txt"), null)
            .RunAsync(_source, null, CancellationToken.None);

        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Equal(Path.Combine(_dir, "отчёт за 2024 (копия)_b64_2.txt"), result.OutputPath);
        // certutil -encode пишет base64 между BEGIN/END — значит, получил оба пути целиком.
        var encoded = File.ReadAllText(result.OutputPath!);
        Assert.Contains(Convert.ToBase64String(_content), encoded.Replace("\r\n", ""));
        Assert.Equal("чужой файл", File.ReadAllText(taken));
        AssertSourceUntouched(written);
    }

    [Fact]
    public async Task Failure_GivesErrorWithOutputTail_AndLeavesNoResult()
    {
        var written = File.GetLastWriteTimeUtc(_source);
        var job = new CustomActionJob(Certutil("-hashfile {file} NOT_AN_ALGORITHM {out}", "_x", "txt"), null);

        var error = await Assert.ThrowsAsync<CompressionException>(() => job.RunAsync(_source, null, CancellationToken.None));

        Assert.Contains("код", error.Message);
        Assert.False(string.IsNullOrWhiteSpace(error.Details));
        Assert.Single(Directory.GetFiles(_dir));
        AssertSourceUntouched(written);
    }

    [Fact]
    public async Task MissingProgram_AndMissingFfmpeg_AreClearErrors()
    {
        var noProgram = new CustomActionJob(Certutil("{file}") with { Program = "no-such-program-filedial.exe" }, null);
        var e1 = await Assert.ThrowsAsync<CompressionException>(() => noProgram.RunAsync(_source, null, CancellationToken.None));
        Assert.Contains("no-such-program-filedial.exe", e1.Message);

        var noFfmpeg = new CustomActionJob(Certutil("-i {file} {out}") with { Program = "{ffmpeg}" }, null);
        var e2 = await Assert.ThrowsAsync<CompressionException>(() => noFfmpeg.RunAsync(_source, null, CancellationToken.None));
        Assert.Contains("ffmpeg", e2.Message);
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task Batch_RunsForEachFile()
    {
        var second = Path.Combine(_dir, "второй файл.txt");
        File.WriteAllText(second, "2");
        var action = Certutil("-encode {file} {out}", "_b64", "txt");

        var summary = await BatchRunner.RunAsync([_source, second], _ => new CustomActionJob(action, null), null, null, null,
            CancellationToken.None);

        Assert.Equal(2, summary.Done);
        Assert.All(summary.Outcomes, o => Assert.Equal(HistoryStatus.Done, o.Status));
        Assert.True(File.Exists(Path.Combine(_dir, "второй файл_b64.txt")));
        Assert.Equal(SHA256.HashData(_content), SHA256.HashData(File.ReadAllBytes(_source)));
    }
}
