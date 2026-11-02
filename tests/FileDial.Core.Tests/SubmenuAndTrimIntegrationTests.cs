using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

/// <summary>Все форматы из внешних колец и обрезка — на настоящем ffmpeg.</summary>
public class SubmenuAndTrimIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));
    private readonly string _dir;

    public SubmenuAndTrimIntegrationTests()
    {
        _dir = Path.Combine(_root, "Файлы для форматов");
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

    private static Task<ProbeInfo> ProbeAsync(string path) => ProbeInfo.RunAsync(Tools, path, CancellationToken.None);

    private async Task<string> RunAsync(IFileJob job, string source)
    {
        var result = await job.RunAsync(source, null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Empty(Directory.GetFiles(_dir, ".filedial-*"));
        return result.OutputPath!;
    }

    private Task<string> RunAsync(string actionId, string source) => RunAsync(FileJobs.Create(actionId, Tools)!, source);

    [FfmpegFact]
    public async Task Image_ToEveryFormat()
    {
        var png = await GenerateAsync("картинка.png", "-f", "lavfi", "-i", "testsrc2=size=96x64", "-frames:v", "1");

        foreach (var (label, ext) in ActionCatalog.ImageFormats.Where(f => f.Extension != ".png"))
        {
            var output = await RunAsync(ActionIds.ImageTo(ext), png);
            Assert.Equal(Path.Combine(_dir, "картинка" + ext), output);
            var info = await ProbeAsync(output);
            Assert.True((info.Width, info.Height) == (96, 64), $"{label}: {info.Width}×{info.Height}");
        }
    }

    [FfmpegFact]
    public async Task Image_RotateLeft_AndMirror()
    {
        var jpg = await GenerateAsync("фото.jpg", "-f", "lavfi", "-i", "testsrc2=size=96x64", "-frames:v", "1");

        var left = await RunAsync(ActionIds.RotateLeft, jpg);
        Assert.Equal(Path.Combine(_dir, "фото_повернуто_влево.jpg"), left);
        Assert.Equal(64, (await ProbeAsync(left)).Width);

        var mirror = await RunAsync(ActionIds.Mirror, jpg);
        Assert.Equal(Path.Combine(_dir, "фото_отражено.jpg"), mirror);
        Assert.Equal(96, (await ProbeAsync(mirror)).Width);
    }

    [FfmpegFact]
    public async Task Audio_ToEveryFormat_AndProcessing()
    {
        var wav = await GenerateAsync("голос.wav",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-ac", "2", "-t", "2");

        foreach (var (label, ext) in ActionCatalog.AudioFormats.Where(f => f.Extension != ".wav"))
        {
            var output = await RunAsync(ActionIds.AudioTo(ext), wav);
            Assert.Equal(Path.Combine(_dir, "голос" + ext), output);
            var info = await ProbeAsync(output);
            Assert.True(info.HasAudio, label);
            Assert.InRange(info.DurationSeconds, 1.9, 2.2);
        }

        var slow = await RunAsync(ActionIds.SlowDown, wav);
        Assert.InRange((await ProbeAsync(slow)).DurationSeconds, 2.6, 2.75);

        var mono = await RunAsync(ActionIds.ToMono, wav);
        Assert.Equal(Path.Combine(_dir, "голос_моно.wav"), mono);
        Assert.Equal(1, (await ProbeAsync(mono)).AudioChannels);
    }

    [FfmpegFact]
    public async Task Video_ToMkvWebmMov()
    {
        var mp4 = await GenerateAsync("ролик.mp4",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-f", "lavfi", "-i", "sine=sample_rate=48000",
            "-t", "2", "-c:v", "libx264", "-c:a", "aac", "-shortest");

        var mkv = await ProbeAsync(await RunAsync(ActionIds.VideoTo(".mkv"), mp4));
        Assert.Equal(("h264", "aac"), (mkv.VideoCodec, mkv.AudioCodec));

        var webm = await ProbeAsync(await RunAsync(ActionIds.VideoTo(".webm"), mp4));
        Assert.Equal(("vp9", "opus"), (webm.VideoCodec, webm.AudioCodec));

        var mov = await ProbeAsync(await RunAsync(ActionIds.VideoTo(".mov"), mp4));
        Assert.Equal(("h264", "aac"), (mov.VideoCodec, mov.AudioCodec));
    }

    [FfmpegFact]
    public async Task Trim_Video_PreciseAndFast_AndAudio()
    {
        var mp4 = await GenerateAsync("длинный ролик.mp4",
            "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25", "-f", "lavfi", "-i", "sine=sample_rate=48000",
            "-t", "6", "-c:v", "libx264", "-g", "25", "-c:a", "aac", "-shortest");

        var precise = new TrimRequest(TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(4), Precise: true);
        var cut = await RunAsync(FileJobs.CreateTrim(Tools, mp4, precise), mp4);
        Assert.Equal(Path.Combine(_dir, "длинный ролик_обрезка.mp4"), cut);
        Assert.InRange((await ProbeAsync(cut)).DurationSeconds, 2.4, 2.6);

        var fast = new TrimRequest(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), Precise: false);
        var quick = await RunAsync(FileJobs.CreateTrim(Tools, mp4, fast), mp4);
        Assert.Equal(Path.Combine(_dir, "длинный ролик_обрезка_2.mp4"), quick);
        // Без перекодирования границы по ключевым кадрам (каждую секунду) — длина около 2 с.
        Assert.InRange((await ProbeAsync(quick)).DurationSeconds, 1.8, 3.1);

        var wav = await GenerateAsync("звук.wav", "-f", "lavfi", "-i", "sine=sample_rate=44100", "-t", "5");
        var audioCut = await RunAsync(FileJobs.CreateTrim(Tools, wav,
            new TrimRequest(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), false)), wav);
        Assert.InRange((await ProbeAsync(audioCut)).DurationSeconds, 1.49, 1.51);
    }
}
