using System.Security.Cryptography;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Selection;

namespace FileDial.Core.Tests;

public class SplitJoinTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "Части");

    public SplitJoinTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    private string MakeFile(string name, int size)
    {
        var path = Path.Combine(_root, name);
        var data = new byte[size];
        new Random(7).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public async Task Split_Then_Join_GivesIdenticalFile()
    {
        var source = MakeFile("большой архив.zip", 2_500_000);
        var hash = Hash(source);

        var split = await new FileSplitJob(1_000_000).RunAsync(source, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "большой архив.zip.001"), split.OutputPath);
        Assert.Equal([1_000_000L, 1_000_000L, 500_000L],
            FileParts.Find(Path.Combine(_root, "большой архив.zip")).Select(p => new FileInfo(p).Length));
        Assert.StartsWith("3 части по 1 МБ", split.Message);
        Assert.Empty(Directory.GetFiles(_root, ".filedial-*"));

        // Исходник ещё рядом — собранный получает имя «_2», ничего не перезаписывается.
        var joined = await new FileJoinJob().RunAsync(Path.Combine(_root, "большой архив.zip.002"), null, CancellationToken.None);
        Assert.Equal(Path.Combine(_root, "большой архив_2.zip"), joined.OutputPath);
        Assert.Equal(hash, Hash(joined.OutputPath!));
        Assert.Equal(hash, Hash(source));
        Assert.StartsWith("Собрано из 3 частей", joined.Message);
    }

    [Fact]
    public async Task Split_AvoidsExistingParts()
    {
        var source = MakeFile("a.7z", 2_100_000);
        File.WriteAllText(Path.Combine(_root, "a.7z.002"), "чужая часть");

        var split = await new FileSplitJob(1_000_000).RunAsync(source, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "a_2.7z.001"), split.OutputPath);
        Assert.Equal("чужая часть", File.ReadAllText(Path.Combine(_root, "a.7z.002")));
    }

    [Fact]
    public async Task Split_SmallFile_IsNothingToDo()
    {
        var source = MakeFile("мелкий.zip", 500);
        var result = await new FileSplitJob(1_000_000).RunAsync(source, null, CancellationToken.None);
        Assert.Equal(JobStatus.NothingToDo, result.Status);
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task Join_MissingOrBrokenParts_AreExplained()
    {
        File.WriteAllBytes(Path.Combine(_root, "x.zip.001"), new byte[100]);
        var only = await Assert.ThrowsAsync<CompressionException>(() =>
            new FileJoinJob().RunAsync(Path.Combine(_root, "x.zip.001"), null, CancellationToken.None));
        Assert.StartsWith("Найдена только одна часть", only.Message);

        File.WriteAllBytes(Path.Combine(_root, "x.zip.002"), new byte[50]);
        File.WriteAllBytes(Path.Combine(_root, "x.zip.003"), new byte[10]);
        var broken = await Assert.ThrowsAsync<CompressionException>(() =>
            new FileJoinJob().RunAsync(Path.Combine(_root, "x.zip.001"), null, CancellationToken.None));
        Assert.Contains("x.zip.002", broken.Message);
        Assert.Empty(Directory.GetFiles(_root, ".filedial-*"));
    }

    [Theory]
    [InlineData(@"C:\a\архив.zip.001", @"C:\a\архив.zip")]
    [InlineData(@"C:\a\film.mkv.017", @"C:\a\film.mkv")]
    [InlineData(@"C:\a\x.000", null)]
    [InlineData(@"C:\a\x.zip", null)]
    [InlineData(@"C:\a\x.1", null)]
    public void BaseOf(string path, string? expected)
    {
        Assert.Equal(expected, FileParts.BaseOf(path));
    }

    [Theory]
    [InlineData(2_500_000, 1_000_000, 3)]
    [InlineData(3_000_000, 1_000_000, 3)]
    [InlineData(1, 1_000_000, 1)]
    [InlineData(0, 1_000_000, 0)]
    public void PartCount(long size, long part, long expected)
    {
        Assert.Equal(expected, FileParts.PartCount(size, part));
    }

    [CyrillicWindowsFact]
    public async Task SevenZip_SurvivesSplitAndJoin()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var dir = Path.Combine(_root, "Фото");
        Directory.CreateDirectory(dir);
        var data = new byte[1_500_000];
        new Random(1).NextBytes(data);
        File.WriteAllBytes(Path.Combine(dir, "снимок.raw"), data);
        var archive = (await new ArchiveCreateJob(ArchiveFormat.SevenZip).RunAsync(dir, null, CancellationToken.None)).OutputPath!;
        Directory.Delete(dir, recursive: true);

        await new FileSplitJob(600_000).RunAsync(archive, null, CancellationToken.None);
        File.Delete(archive); // у получателя есть только части
        var joined = await new FileJoinJob().RunAsync(archive + ".001", null, CancellationToken.None);

        Assert.Equal(archive, joined.OutputPath);
        var test = await new ArchiveTestJob().RunAsync(joined.OutputPath!, null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, test.Status);
    }

    [Fact]
    public void PartFile_GetsQuickJoin_InCircle()
    {
        File.WriteAllBytes(Path.Combine(_root, "фильм.mkv.001"), new byte[100]);
        File.WriteAllBytes(Path.Combine(_root, "фильм.mkv.002"), new byte[40]);
        var probe = new FileSystemProbe();

        var single = Assert.IsType<HotkeyOutcome.ShowMenu>(SelectionInterpreter.Interpret(
            SelectionResult.Success([Path.Combine(_root, "фильм.mkv.002")]), probe)).Menu;
        Assert.Equal(ActionIds.JoinParts, single.QuickAction!.Id);
        Assert.Equal("фильм.mkv", single.Title);
        Assert.Equal("2 части · 140 Б", single.Subtitle);
        Assert.Equal([Path.Combine(_root, "фильм.mkv.001")], single.SourcePaths);

        var both = Assert.IsType<HotkeyOutcome.ShowMenu>(SelectionInterpreter.Interpret(SelectionResult.Success(
            [Path.Combine(_root, "фильм.mkv.001"), Path.Combine(_root, "фильм.mkv.002")]), probe)).Menu;
        Assert.Equal(ActionIds.JoinParts, both.QuickAction!.Id);
    }

    [Fact]
    public void Catalog_SplitEnabled_AndNeedsSize()
    {
        Assert.True(ActionCatalog.ForFile(@"C:\a.zip", 1).Single(a => a.Id == ActionIds.ArchiveSplit).IsEnabled);
        Assert.True(FileJobs.NeedsPartSize(ActionIds.ArchiveSplit));
        Assert.False(FileJobs.NeedsFfmpeg(ActionIds.JoinParts));
        Assert.IsType<FileJoinJob>(FileJobs.Create(ActionIds.JoinParts, null));
    }
}
