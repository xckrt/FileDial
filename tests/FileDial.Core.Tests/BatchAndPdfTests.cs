using System.Text;
using System.Text.RegularExpressions;
using FileDial.Core.Compression;
using FileDial.Core.History;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Selection;
using FileDial.Core.Text;

namespace FileDial.Core.Tests;

public class PluralTests
{
    [Theory]
    [InlineData(1, "1 картинка")]
    [InlineData(2, "2 картинки")]
    [InlineData(4, "4 картинки")]
    [InlineData(5, "5 картинок")]
    [InlineData(11, "11 картинок")]
    [InlineData(14, "14 картинок")]
    [InlineData(21, "21 картинка")]
    [InlineData(22, "22 картинки")]
    [InlineData(111, "111 картинок")]
    public void Russian(int n, string expected)
    {
        Assert.Equal(expected, Plural.Count(n, "картинка", "картинки", "картинок"));
    }

    [Theory]
    [InlineData(FileKind.Video, 3, "3 видео")]
    [InlineData(FileKind.Audio, 2, "2 аудиофайла")]
    [InlineData(FileKind.Image, 5, "5 картинок")]
    public void KindCount(FileKind kind, int n, string expected)
    {
        Assert.Equal(expected, SelectionInterpreter.KindCount(kind, n));
    }
}

public class BatchMenuTests
{
    private sealed class PathProbe(Func<string, FileState> f) : IFileProbe
    {
        public FileState Probe(string path) => f(path);
    }

    private static readonly IFileProbe Files = new PathProbe(_ => new FileState(FileStateKind.File, 2_000_000));

    private static HotkeyOutcome Interpret(params string[] paths) =>
        SelectionInterpreter.Interpret(SelectionResult.Success(paths), Files);

    private static DialMenu Menu(params string[] paths) => Assert.IsType<HotkeyOutcome.ShowMenu>(Interpret(paths)).Menu;

    [Fact]
    public void SameKind_GivesBatchMenu()
    {
        var menu = Menu(@"C:\p\a.png", @"C:\p\b.jpg", @"C:\p\c.webp");
        Assert.Equal("3 картинки", menu.Title);
        Assert.Equal("6 МБ", menu.Subtitle);
        Assert.Equal(3, menu.SourcePaths.Count);
    }

    [Fact]
    public void MixedKinds_AreUnsupported()
    {
        var menu = Menu(@"C:\p\a.png", @"C:\p\b.mp3");
        Assert.Equal(DialMenuKind.Unsupported, menu.Kind);
        Assert.Contains("Файлы разных типов", menu.Subtitle);
    }

    [Fact]
    public void Folders_AreUnsupported()
    {
        var probe = new PathProbe(p => p.EndsWith("папка") ? new FileState(FileStateKind.Directory) : new FileState(FileStateKind.File, 1));
        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(
            SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\p\a.png", @"C:\p\папка"]), probe)).Menu;
        Assert.Contains("есть папки", menu.Subtitle);
    }

    [Fact]
    public void UnavailableFile_ShowsToastWithItsName()
    {
        var probe = new PathProbe(p => p.Contains("занят") ? new FileState(FileStateKind.Unavailable) : new FileState(FileStateKind.File, 1));
        var toast = Assert.IsType<HotkeyOutcome.ShowToast>(
            SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\p\a.png", @"C:\p\занят.png"]), probe));
        Assert.Equal("Файл недоступен: занят.png", toast.Message);
    }

    [Fact]
    public void TooMany_AreUnsupported()
    {
        var paths = Enumerable.Range(0, SelectionInterpreter.MaxBatch + 1).Select(i => $@"C:\p\{i}.png").ToArray();
        Assert.Equal(DialMenuKind.Unsupported, Menu(paths).Kind);
    }

    [Fact]
    public void FormatChild_DisabledOnlyIfAllFilesAlreadyThere()
    {
        var allPng = Menu(@"C:\p\a.png", @"C:\p\b.PNG").Actions[0].Children!.Single(c => c.Label == "PNG");
        Assert.False(allPng.IsEnabled);
        Assert.Equal("Все файлы уже в PNG", allPng.DisabledReason);

        var mixed = Menu(@"C:\p\a.png", @"C:\p\b.jpg").Actions[0].Children!;
        Assert.True(mixed.Single(c => c.Label == "PNG").IsEnabled);
        Assert.True(mixed.Single(c => c.Label == "JPG").IsEnabled);
    }

    [Fact]
    public void Trim_IsSingleFileOnly_Pdf_CollectsAll()
    {
        var video = Menu(@"C:\v\a.mp4", @"C:\v\b.mp4").Actions;
        Assert.Equal("Обрезка — по одному файлу", video.Single(a => a.Id == ActionIds.Trim).DisabledReason);

        var pdf = Menu(@"C:\p\a.png", @"C:\p\b.png").Actions.Single(a => a.Id == ActionIds.ImageToPdf);
        Assert.True(pdf.IsEnabled);
        Assert.Equal("Собрать в PDF", pdf.Label);
    }

    [Fact]
    public void Compress_DisabledWhenEveryFileIsSmall()
    {
        var compress = Menu(@"C:\v\a.mp4", @"C:\v\b.mp4").Actions.Single(a => a.Id == ActionIds.Compress25);
        Assert.Equal("Все файлы уже меньше 25 МБ", compress.DisabledReason);
    }

    [Fact]
    public void SinglePicture_PdfIsEnabled()
    {
        var pdf = ActionCatalog.ForFile(@"C:\p\a.png", 1).Single(a => a.Id == ActionIds.ImageToPdf);
        Assert.True(pdf.IsEnabled);
        Assert.Equal("В PDF", pdf.Label);
    }
}

public class BatchRunnerTests
{
    private sealed class FakeJob(Func<string, JobResult> run) : IFileJob
    {
        public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress("Работа", 0.5));
            return Task.FromResult(run(sourcePath));
        }
    }

    [Fact]
    public async Task OneFailure_DoesNotStopTheRest()
    {
        var job = new FakeJob(p => p.Contains("плохой")
            ? throw new CompressionException("ffmpeg не смог обработать файл.")
            : new JobResult(JobStatus.Done, "ok", p + ".out"));
        var seen = new List<FileOutcome>();

        var summary = await BatchRunner.RunAsync([@"C:\a.wav", @"C:\плохой.wav", @"C:\c.wav"], _ => job, null, null,
            seen.Add, CancellationToken.None);

        Assert.Equal(2, summary.Done);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(3, seen.Count);
        Assert.Equal(@"C:\c.wav.out", summary.LastOutput);
        Assert.Equal("Готово: 2 из 3. Ошибок: 1. плохой.wav: ffmpeg не смог обработать файл.", summary.Message);
    }

    [Fact]
    public async Task AlreadyInTargetFormat_IsSkipped_NotCopied()
    {
        var runs = 0;
        var job = new FakeJob(p => { runs++; return new JobResult(JobStatus.Done, "ok", p); });

        var summary = await BatchRunner.RunAsync([@"C:\a.mp3", @"C:\b.wav"], _ => job,
            BatchRunner.SkipWhenAlreadyInFormat(ActionIds.ToMp3), null, null, CancellationToken.None);

        Assert.Equal(1, runs);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal("Уже в формате MP3.", summary.Outcomes[0].Message);
        Assert.Equal("Готово: 1 из 2. Пропущено: 1 (делать нечего).", summary.Message);
    }

    [Fact]
    public async Task Progress_IsOverallAcrossFiles()
    {
        var reports = new List<JobProgress>();
        var progress = new Sync(reports.Add);
        var job = new FakeJob(p => new JobResult(JobStatus.Done, "ok", p));

        await BatchRunner.RunAsync([@"C:\a.png", @"C:\b.png"], _ => job, null, progress, null, CancellationToken.None);

        Assert.Contains(reports, r => r.Stage.StartsWith("2 из 2 · b.png") && r.Fraction is 0.75);
        Assert.Equal(1, reports[^1].Fraction);
    }

    [Fact]
    public async Task Cancellation_StopsBatch()
    {
        using var cts = new CancellationTokenSource();
        var job = new FakeJob(p => { cts.Cancel(); return new JobResult(JobStatus.Done, "ok", p); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BatchRunner.RunAsync([@"C:\a.png", @"C:\b.png"], _ => job, null, null, null, cts.Token));
    }

    [Fact]
    public void SkipRule_OnlyForConversions()
    {
        Assert.Null(BatchRunner.SkipWhenAlreadyInFormat(ActionIds.Mirror));
        Assert.NotNull(BatchRunner.SkipWhenAlreadyInFormat(ActionIds.ImageTo(".jpg"))!(@"C:\a.JPEG"));
    }

    private sealed class Sync(Action<JobProgress> a) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => a(value);
    }
}

public class PdfWriterTests
{
    /// <summary>Проверка структуры: каждая запись xref указывает на начало «N 0 obj».</summary>
    internal static void AssertValidPdf(byte[] pdf, int expectedPages)
    {
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Equal(expectedPages, Regex.Matches(text, @"/Type /Page\b(?!s)").Count);
        Assert.Contains($"/Count {expectedPages}", text);

        var startxref = int.Parse(Regex.Match(text, @"startxref\n(\d+)").Groups[1].Value);
        Assert.StartsWith("xref", text[startxref..]);
        var entries = Regex.Matches(text[startxref..], @"(\d{10}) 00000 n ");
        Assert.Equal(2 + expectedPages * 3, entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var offset = int.Parse(entries[i].Groups[1].Value);
            Assert.StartsWith($"{i + 1} 0 obj", text[offset..]);
        }
    }

    [Fact]
    public void TwoPages_HaveCorrectStructureAndSizes()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        using var ms = new MemoryStream();
        PdfWriter.Write(ms, [new PdfImagePage(jpeg, 960, 540), new PdfImagePage(jpeg, 100, 200)]);

        var pdf = ms.ToArray();
        AssertValidPdf(pdf, 2);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.Contains("/MediaBox [0 0 720 405]", text); // 960×540 px при 96 dpi
        Assert.Contains("/Filter /DCTDecode", text);
    }

    [Fact]
    public void NoPages_Throws()
    {
        Assert.Throws<ArgumentException>(() => PdfWriter.Write(new MemoryStream(), []));
    }

    [Theory]
    [InlineData(new[] { @"D:\Отпуск\IMG_1.jpg" }, "IMG_1")]
    [InlineData(new[] { @"D:\Отпуск\IMG_1.jpg", @"D:\Отпуск\IMG_2.jpg" }, "Отпуск")]
    [InlineData(new[] { @"D:\a.jpg", @"D:\b.jpg" }, "Картинки")]
    public void PdfName(string[] paths, string expected)
    {
        Assert.Equal(expected, ImagesToPdfJob.PdfName(paths));
    }
}

/// <summary>PDF и пакет на настоящем ffmpeg.</summary>
public class BatchAndPdfIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));
    private readonly string _dir;

    public BatchAndPdfIntegrationTests()
    {
        _dir = Path.Combine(_root, "Отпуск 2026");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static FfmpegTools Tools => FfmpegLocator.LocateDefault()!;

    private async Task<string> GenerateAsync(string name, params string[] args)
    {
        var path = Path.Combine(_dir, name);
        var result = await ProcessRunner.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", .. args, path], null, false, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        return path;
    }

    [FfmpegFact]
    public async Task Images_AreCollectedIntoOnePdf()
    {
        var a = await GenerateAsync("1 пляж.jpg", "-f", "lavfi", "-i", "testsrc2=size=320x240", "-frames:v", "1");
        var b = await GenerateAsync("2 прозрачная.png", "-f", "lavfi", "-i", "color=c=red@0.3:s=200x100,format=rgba", "-frames:v", "1");
        var c = await GenerateAsync("3 горы.webp", "-f", "lavfi", "-i", "testsrc2=size=100x300", "-frames:v", "1");

        var result = await new ImagesToPdfJob(Tools).RunAsync([a, b, c], null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "Отпуск 2026.pdf"), result.OutputPath);
        PdfWriterTests.AssertValidPdf(File.ReadAllBytes(result.OutputPath!), 3);
        Assert.Contains("3 картинки", result.Message);
        Assert.Equal(4, Directory.GetFiles(_dir).Length); // три исходника и PDF, временных нет
    }

    [FfmpegFact]
    public async Task Batch_ConvertsEach_SkipsAlreadyConverted()
    {
        var w1 = await GenerateAsync("запись 1.wav", "-f", "lavfi", "-i", "sine", "-t", "1");
        var w2 = await GenerateAsync("запись 2.wav", "-f", "lavfi", "-i", "sine", "-t", "1");
        var m3 = await GenerateAsync("готовая.mp3", "-f", "lavfi", "-i", "sine", "-t", "1");
        var recorded = new List<FileOutcome>();

        var summary = await BatchRunner.RunAsync([w1, w2, m3],
            _ => FileJobs.Create(ActionIds.ToMp3, Tools)!,
            BatchRunner.SkipWhenAlreadyInFormat(ActionIds.ToMp3), null, recorded.Add, CancellationToken.None);

        Assert.Equal(2, summary.Done);
        Assert.Equal(1, summary.Skipped);
        Assert.True(File.Exists(Path.Combine(_dir, "запись 1.mp3")));
        Assert.True(File.Exists(Path.Combine(_dir, "запись 2.mp3")));
        Assert.False(File.Exists(Path.Combine(_dir, "готовая_2.mp3")));
        Assert.Equal([HistoryStatus.Done, HistoryStatus.Done, HistoryStatus.NothingToDo], recorded.Select(r => r.Status));
    }
}
