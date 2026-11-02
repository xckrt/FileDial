using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

/// <summary>Новые действия над видео, звуком и картинками на настоящем ffmpeg.</summary>
public class MoreActionsIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));
    private readonly string _dir;

    public MoreActionsIntegrationTests()
    {
        _dir = Path.Combine(_root, "Папка с роликами");
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

    /// <summary>Ролик 4 с: H.264 + AAC в mp4 (или другой кодек, если задан).</summary>
    private Task<string> ClipAsync(string name, string videoCodec = "libx264", string audioCodec = "aac") =>
        GenerateAsync(name,
            "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
            "-t", "4", "-c:v", videoCodec, "-c:a", audioCodec, "-shortest");

    private static Task<ProbeInfo> ProbeAsync(string path) => ProbeInfo.RunAsync(Tools, path, CancellationToken.None);

    private async Task<string> RunAsync(string actionId, string source)
    {
        var before = File.ReadAllBytes(source);
        var result = await FileJobs.Create(actionId, Tools)!.RunAsync(source, null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Empty(Directory.GetFiles(_dir, ".filedial-*"));
        return result.OutputPath!;
    }

    [FfmpegFact]
    public async Task ExtractAudio_CopiesAacIntoM4a()
    {
        var clip = await ClipAsync("отпуск.mp4");
        var output = await RunAsync(ActionIds.ExtractAudio, clip);

        Assert.Equal(Path.Combine(_dir, "отпуск.m4a"), output);
        var info = await ProbeAsync(output);
        Assert.False(info.HasVideo);
        Assert.Equal("aac", info.AudioCodec);
    }

    [FfmpegFact]
    public async Task Mute_RemovesAudio_KeepsVideoAndContainer()
    {
        var clip = await ClipAsync("клип.mkv");
        var output = await RunAsync(ActionIds.Mute, clip);

        Assert.Equal(Path.Combine(_dir, "клип_без_звука.mkv"), output);
        var info = await ProbeAsync(output);
        Assert.True(info.HasVideo);
        Assert.False(info.HasAudio);
        Assert.Equal("h264", info.VideoCodec);
    }

    [FfmpegFact]
    public async Task Frame_SavesPng()
    {
        var clip = await ClipAsync("ролик.mp4");
        var output = await RunAsync(ActionIds.Frame, clip);

        Assert.Equal(Path.Combine(_dir, "ролик_кадр.png"), output);
        var info = await ProbeAsync(output);
        Assert.Equal((640, 360), (info.Width, info.Height));
    }

    [FfmpegFact]
    public async Task Gif_IsSmallAndAnimated()
    {
        var clip = await ClipAsync("мем.mp4");
        var output = await RunAsync(ActionIds.Gif, clip);

        Assert.Equal(Path.Combine(_dir, "мем.gif"), output);
        var info = await ProbeAsync(output);
        Assert.Equal("gif", info.VideoCodec);
        Assert.Equal(480, info.Width);
        Assert.InRange(info.DurationSeconds, 3.5, 4.5);
    }

    [FfmpegFact]
    public async Task Convert_MkvH264_IsRemuxedToMp4()
    {
        var clip = await ClipAsync("запись.mkv");
        var output = await RunAsync(ActionIds.Convert, clip);

        Assert.Equal(Path.Combine(_dir, "запись.mp4"), output);
        var info = await ProbeAsync(output);
        Assert.Equal(("h264", "aac"), (info.VideoCodec, info.AudioCodec));
    }

    [FfmpegFact]
    public async Task Convert_OtherCodecs_AreReencoded()
    {
        var clip = await ClipAsync("старое.mkv", videoCodec: "mpeg4", audioCodec: "libmp3lame");
        var output = await RunAsync(ActionIds.Convert, clip);

        var info = await ProbeAsync(output);
        Assert.Equal(("h264", "aac"), (info.VideoCodec, info.AudioCodec));
    }

    [FfmpegFact]
    public async Task SpeedUp_ShortensDuration_AndDenoiseKeepsIt()
    {
        var wav = await GenerateAsync("лекция.wav", "-f", "lavfi", "-i", "sine=frequency=300:sample_rate=44100", "-t", "3");

        var fast = await RunAsync(ActionIds.SpeedUp, wav);
        Assert.Equal(Path.Combine(_dir, "лекция_x1,5.wav"), fast);
        Assert.InRange((await ProbeAsync(fast)).DurationSeconds, 1.9, 2.1);

        var clean = await RunAsync(ActionIds.Denoise, wav);
        Assert.Equal(Path.Combine(_dir, "лекция_без_шума.wav"), clean);
        Assert.InRange((await ProbeAsync(clean)).DurationSeconds, 2.9, 3.1);
    }

    [FfmpegFact]
    public async Task Image_IsCompressedUnderOneMegabyte()
    {
        // Шум почти не сжимается: PNG выйдет на несколько мегабайт.
        var png = await GenerateAsync("шум.png",
            "-f", "lavfi", "-i", "nullsrc=s=1600x1200,geq=lum='random(1)*255':cb=128:cr=128", "-frames:v", "1");
        Assert.True(new FileInfo(png).Length > 1_000_000);

        var output = await RunAsync(ActionIds.ImageCompress1Mb, png);
        Assert.Equal(Path.Combine(_dir, "шум_1мб.jpg"), output);
        Assert.True(new FileInfo(output).Length <= 1_000_000, $"{new FileInfo(output).Length} байт");
    }

    [FfmpegFact]
    public async Task SmallImage_IsNotCompressed()
    {
        var png = await GenerateAsync("мелкая.png", "-f", "lavfi", "-i", "color=c=blue:s=64x64", "-frames:v", "1");
        var result = await FileJobs.Create(ActionIds.ImageCompress1Mb, Tools)!.RunAsync(png, null, CancellationToken.None);

        Assert.Equal(JobStatus.NothingToDo, result.Status);
        Assert.Single(Directory.GetFiles(_dir));
    }
}
