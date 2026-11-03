using System.IO.Compression;
using System.Text;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Selection;

namespace FileDial.Core.Tests;

public class ArchiveMenuTests
{
    private sealed class Files(Func<string, FileState> f) : IFileProbe
    {
        public FileState Probe(string path) => f(path);
    }

    private static readonly IFileProbe AnyFile = new Files(_ => new FileState(FileStateKind.File, 1_000));

    [Theory]
    [InlineData(@"C:\a.zip")]
    [InlineData(@"C:\a.7Z")]
    [InlineData(@"C:\a.rar")]
    [InlineData(@"C:\a.tar.gz")]
    [InlineData(@"C:\a.tgz")]
    public void Archives_AreRecognized(string path)
    {
        Assert.Equal(FileKind.Archive, FileKinds.Of(path));
    }

    [Fact]
    public void ArchiveCircle_HasExtractTestAndFormats()
    {
        var actions = ActionCatalog.ForFile(@"C:\a\фото.zip", 1_000);

        Assert.Equal(["Распаковать сюда", "Распаковать в папку", "Распаковать в…", "Проверить архив", "Формат",
            "Защитить паролем", "Разбить на части", "Содержимое"], actions.Select(a => a.Label));
        var formats = actions.Single(a => a.Id == ActionIds.ArchiveConvert).Children!;
        Assert.Equal(["ZIP", "7z"], formats.Select(f => f.Label));
        Assert.Equal("Файл уже в ZIP", formats[0].DisabledReason);
        Assert.True(formats[1].IsEnabled || !ArchiveSupport.TarAvailable);
    }

    [Theory]
    [InlineData(@"C:\a\фото.tar.gz", "фото")]
    [InlineData(@"C:\a\Data.TAR.XZ", "Data")]
    [InlineData(@"C:\a\отчёт.zip", "отчёт")]
    [InlineData(@"C:\a\v1.2.7z", "v1.2")]
    public void Stem_StripsCompoundExtensions(string path, string expected)
    {
        Assert.Equal(expected, ArchiveSupport.Stem(path));
    }

    [Fact]
    public void OtherFile_GetsQuickZip()
    {
        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(
            SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\d\отчёт.txt"]), AnyFile)).Menu;
        Assert.Equal(DialMenuKind.Unsupported, menu.Kind);
        Assert.Equal(ActionIds.CompressToZip, menu.QuickAction!.Id);
        Assert.Equal([@"C:\d\отчёт.txt"], menu.SourcePaths);
    }

    [Fact]
    public void FolderAndMixedSelection_GetQuickZip_TooManyDoNot()
    {
        var folder = new Files(_ => new FileState(FileStateKind.Directory));
        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(
            SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\d\Проект"]), folder)).Menu;
        Assert.NotNull(menu.QuickAction);

        var mixed = Assert.IsType<HotkeyOutcome.ShowMenu>(SelectionInterpreter.Interpret(
            SelectionResult.Success([@"C:\d\a.pdf", @"C:\d\b.mp4"]), AnyFile)).Menu;
        Assert.Equal(ActionIds.CompressToZip, mixed.QuickAction!.Id);
        Assert.Equal(2, mixed.SourcePaths.Count);

        var many = Enumerable.Range(0, SelectionInterpreter.MaxBatch + 1).Select(i => $@"C:\d\{i}.pdf").ToArray();
        Assert.Null(Assert.IsType<HotkeyOutcome.ShowMenu>(
            SelectionInterpreter.Interpret(SelectionResult.Success(many), AnyFile)).Menu.QuickAction);
    }

    [Fact]
    public void ArchiveJobs_DoNotNeedFfmpeg()
    {
        foreach (var id in new[] { ActionIds.ExtractHere, ActionIds.ExtractToFolder, ActionIds.ExtractTo,
                     ActionIds.TestArchive, ActionIds.CompressToZip, ActionIds.ArchiveTo(".7z") })
        {
            Assert.False(FileJobs.NeedsFfmpeg(id), id);
            Assert.True(FileJobs.IsImplemented(id), id);
        }
        Assert.NotNull(FileJobs.Create(ActionIds.ExtractHere, tools: null));
        Assert.True(FileJobs.NeedsFfmpeg(ActionIds.Gif));
        Assert.False(FileJobs.IsImplemented(ActionIds.RemoveBackground)); // последняя заглушка
    }
}

/// <summary>Настоящие архивы: zip из «старого Проводника», 7z и tar.gz через tar.exe из Windows.</summary>
public class ArchiveIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "Архивы");

    public ArchiveIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    private string Folder(string name, params (string Path, string Text)[] files)
    {
        var dir = Path.Combine(_root, name);
        foreach (var (path, text) in files)
        {
            var full = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>zip как у Проводника: имена в cp866, без флага UTF-8.</summary>
    private string LegacyZip(string name, params (string Entry, string Text)[] entries)
    {
        var path = Path.Combine(_root, name);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.GetEncoding(866));
        foreach (var (entry, text) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write(text);
        }
        return path;
    }

    private static Task<JobResult> Run(IFileJob job, string path) => job.RunAsync(path, null, CancellationToken.None);

    [Fact]
    public async Task LegacyZip_CyrillicNames_AreReadCorrectly()
    {
        var zip = LegacyZip("Документы.zip", ("Отчёт за май.txt", "раз"), ("Счета/январь.txt", "два"));

        var result = await Run(new ArchiveExtractJob(ExtractMode.Here), zip);

        // Два элемента в корне — значит, в папку с именем архива.
        Assert.Equal(Path.Combine(_root, "Документы"), result.OutputPath);
        Assert.Equal("раз", File.ReadAllText(Path.Combine(_root, "Документы", "Отчёт за май.txt")));
        Assert.Equal("два", File.ReadAllText(Path.Combine(_root, "Документы", "Счета", "январь.txt")));
        Assert.Empty(Directory.GetDirectories(_root, ".filedial-*"));
    }

    [Fact]
    public async Task ExtractHere_SingleRootFolder_HasNoDoubleNesting()
    {
        var zip = LegacyZip("проект-v2.zip", ("проект/readme.txt", "r"), ("проект/src/main.txt", "m"));

        var result = await Run(new ArchiveExtractJob(ExtractMode.Here), zip);
        Assert.Equal(Path.Combine(_root, "проект"), result.OutputPath);
        Assert.True(File.Exists(Path.Combine(_root, "проект", "src", "main.txt")));

        // Второй раз — папка занята: рядом «проект_2», первая не тронута.
        var again = await Run(new ArchiveExtractJob(ExtractMode.Here), zip);
        Assert.Equal(Path.Combine(_root, "проект_2"), again.OutputPath);

        // «В папку» — всегда в папку с именем архива.
        var folder = await Run(new ArchiveExtractJob(ExtractMode.ToFolder), zip);
        Assert.Equal(Path.Combine(_root, "проект-v2"), folder.OutputPath);
        Assert.True(Directory.Exists(Path.Combine(_root, "проект-v2", "проект")));
    }

    [Fact]
    public async Task ZipSlip_IsRefused_AndNothingLeaks()
    {
        var zip = LegacyZip("злой.zip", ("ok.txt", "ok"), ("../../вне.txt", "плохо"));

        await Assert.ThrowsAsync<CompressionException>(() => Run(new ArchiveExtractJob(ExtractMode.Here), zip));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "вне.txt")));
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public async Task CompressFiles_ToZip_NamedAfterFolder_Utf8Names()
    {
        var dir = Folder("Отпуск", ("пляж.txt", "1"), ("горы.txt", "2"), ("Видео/день 1.txt", "3"));
        var job = new ArchiveCreateJob(ArchiveFormat.Zip);

        var result = await job.RunAsync([Path.Combine(dir, "пляж.txt"), Path.Combine(dir, "Видео")], null, CancellationToken.None);

        Assert.Equal(Path.Combine(dir, "Отпуск.zip"), result.OutputPath);
        using var zip = ZipFile.OpenRead(result.OutputPath!);
        Assert.Equal(["Видео/день 1.txt", "пляж.txt"], zip.Entries.Select(e => e.FullName).Order());
        Assert.Contains("2 файла", result.Message);
    }

    [Fact]
    public async Task CompressFolder_KeepsEmptySubfolders()
    {
        var dir = Folder("Проект", ("a.txt", "a"));
        Directory.CreateDirectory(Path.Combine(dir, "пустая"));

        var result = await new ArchiveCreateJob(ArchiveFormat.Zip).RunAsync(dir, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "Проект.zip"), result.OutputPath);
        using var zip = ZipFile.OpenRead(result.OutputPath!);
        Assert.Contains("Проект/пустая/", zip.Entries.Select(e => e.FullName));
        Assert.Contains("Проект/a.txt", zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public async Task SevenZip_RoundTrip_AndCorruptionIsDetected()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var dir = Folder("Фотоальбом", ("снимок 1.txt", new string('я', 5000)), ("Подпапка/снимок 2.txt", "два"));

        var created = await new ArchiveCreateJob(ArchiveFormat.SevenZip).RunAsync(dir, null, CancellationToken.None);
        Assert.Equal(Path.Combine(_root, "Фотоальбом.7z"), created.OutputPath);

        var test = await Run(new ArchiveTestJob(), created.OutputPath!);
        Assert.Equal("Архив в порядке: 2 файла, ошибок нет.", test.Message);

        // Распаковываем в другую папку и сверяем содержимое.
        var target = Path.Combine(_root, "Распаковка");
        Directory.CreateDirectory(target);
        var extracted = await Run(new ArchiveExtractJob(ExtractMode.ToChosen, target), created.OutputPath!);
        Assert.Equal(Path.Combine(target, "Фотоальбом"), extracted.OutputPath);
        Assert.Equal("два", File.ReadAllText(Path.Combine(target, "Фотоальбом", "Подпапка", "снимок 2.txt")));

        // Портим байт в середине — проверка должна найти ошибку.
        var bytes = File.ReadAllBytes(created.OutputPath!);
        bytes[bytes.Length / 2] ^= 0xFF;
        var broken = Path.Combine(_root, "битый.7z");
        File.WriteAllBytes(broken, bytes);
        await Assert.ThrowsAsync<CompressionException>(() => Run(new ArchiveTestJob(), broken));
    }

    [Fact]
    public async Task TarGz_IsRepackedToZip()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var dir = Folder("исходники", ("код/main.txt", "main"), ("readme.txt", "r"));
        var tgz = Path.Combine(_root, "исходники.tar.gz");
        await ArchiveSupport.RunTarAsync(["-c", "-z", "-f", tgz, "-C", _root, "исходники"], "tar", CancellationToken.None);
        Directory.Delete(dir, recursive: true);

        var result = await Run(new ArchiveRepackJob(ArchiveFormat.Zip), tgz);

        Assert.Equal(Path.Combine(_root, "исходники.zip"), result.OutputPath);
        using var zip = ZipFile.OpenRead(result.OutputPath!);
        Assert.Contains("исходники/код/main.txt", zip.Entries.Select(e => e.FullName));
        Assert.Equal(2, ArchiveSupport.CountFiles(tgz, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task EmptyArchive_IsNothingToDo()
    {
        var zip = LegacyZip("пустой.zip");
        var result = await Run(new ArchiveExtractJob(ExtractMode.Here), zip);
        Assert.Equal(JobStatus.NothingToDo, result.Status);
        Assert.Empty(Directory.GetDirectories(_root));
    }
}
