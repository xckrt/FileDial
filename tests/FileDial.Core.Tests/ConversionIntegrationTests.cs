using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

/// <summary>Настоящие преобразования изображений и звука через ffmpeg в папке с кириллицей и пробелами.</summary>
public class ConversionIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));
    private readonly string _dir;

    public ConversionIntegrationTests()
    {
        _dir = Path.Combine(_root, "Мои файлы с пробелами");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static FfmpegTools Tools => FfmpegLocator.LocateDefault()!;

    private async Task<string> GenerateAsync(string name, params string[] inputArgs)
    {
        var path = Path.Combine(_dir, name);
        var result = await ProcessRunner.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", .. inputArgs, path],
            null, captureOutput: false, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        return path;
    }

    private static Task<ProbeInfo> ProbeAsync(string path) => ProbeInfo.RunAsync(Tools, path, CancellationToken.None);

    private async Task<JobResult> RunAsync(string actionId, string source)
    {
        var before = File.ReadAllBytes(source);
        var result = await FileJobs.Create(actionId, Tools)!.RunAsync(source, null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, result.Status);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(before, File.ReadAllBytes(source)); // исходник не изменён
        Assert.Empty(Directory.GetFiles(_dir, ".filedial-*")); // временных файлов нет
        return result;
    }

    [FfmpegFact]
    public async Task TransparentPng_ToJpg_GetsWhiteBackground()
    {
        var png = await GenerateAsync("прозрачная картинка.png",
            "-f", "lavfi", "-i", "color=c=red@0.0:s=64x48,format=rgba", "-frames:v", "1");

        var result = await RunAsync(ActionIds.ToJpg, png);
        Assert.Equal(Path.Combine(_dir, "прозрачная картинка.jpg"), result.OutputPath);

        // Раскладываем JPG в PPM (несжатый RGB) и смотрим на пиксели: должны быть белыми, а не чёрными.
        var ppm = Path.Combine(_root, "check.ppm");
        var decode = await ProcessRunner.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", result.OutputPath!, "-f", "image2", "-c:v", "ppm", ppm],
            null, false, CancellationToken.None);
        Assert.Equal(0, decode.ExitCode);
        var bytes = File.ReadAllBytes(ppm);
        Assert.All(bytes[^30..], b => Assert.True(b > 240, $"Пиксель {b} не белый"));
    }

    [FfmpegFact]
    public async Task LargeImage_IsResizedTo1920_KeepingFormat()
    {
        var png = await GenerateAsync("большая.png", "-f", "lavfi", "-i", "testsrc2=size=2400x1200", "-frames:v", "1");

        var result = await RunAsync(ActionIds.Resize1920, png);
        Assert.Equal(Path.Combine(_dir, "большая_1920px.png"), result.OutputPath);
        var info = await ProbeAsync(result.OutputPath!);
        Assert.Equal((1920, 960), (info.Width, info.Height));
    }

    [FfmpegFact]
    public async Task Rotate_SwapsSides()
    {
        var jpg = await GenerateAsync("снимок.jpg", "-f", "lavfi", "-i", "testsrc2=size=64x48", "-frames:v", "1");

        var result = await RunAsync(ActionIds.RotateRight, jpg);
        Assert.Equal(Path.Combine(_dir, "снимок_повернуто.jpg"), result.OutputPath);
        var info = await ProbeAsync(result.OutputPath!);
        Assert.Equal((48, 64), (info.Width, info.Height));
    }

    [FfmpegFact]
    public async Task Png_ToWebp()
    {
        var png = await GenerateAsync("a.png", "-f", "lavfi", "-i", "testsrc2=size=64x48", "-frames:v", "1");
        var result = await RunAsync(ActionIds.ToWebp, png);
        Assert.Equal(64, (await ProbeAsync(result.OutputPath!)).Width);
    }

    [FfmpegFact]
    public async Task Wav_ToMp3_And_M4a_AndExistingNameIsNotOverwritten()
    {
        var wav = await GenerateAsync("запись голоса.wav",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "3");
        File.WriteAllText(Path.Combine(_dir, "запись голоса.mp3"), "занято");

        var mp3 = await RunAsync(ActionIds.ToMp3, wav);
        Assert.Equal(Path.Combine(_dir, "запись голоса_2.mp3"), mp3.OutputPath);
        Assert.Equal("занято", File.ReadAllText(Path.Combine(_dir, "запись голоса.mp3")));
        var info = await ProbeAsync(mp3.OutputPath!);
        Assert.True(info.HasAudio);
        Assert.InRange(info.DurationSeconds, 2.9, 3.2);

        var m4a = await RunAsync(ActionIds.ToM4a, wav);
        Assert.Equal(".m4a", Path.GetExtension(m4a.OutputPath));
        Assert.True((await ProbeAsync(m4a.OutputPath!)).HasAudio);
    }

    [FfmpegFact]
    public async Task Normalize_KeepsFormatAndSampleRate()
    {
        var wav = await GenerateAsync("тихо.wav",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100,volume=0.05", "-t", "3");

        var result = await RunAsync(ActionIds.Normalize, wav);
        Assert.Equal(Path.Combine(_dir, "тихо_выровнено.wav"), result.OutputPath);
        Assert.Equal(44100, (await ProbeAsync(result.OutputPath!)).AudioSampleRate);
    }

    [FfmpegFact]
    public async Task Image_WithoutPicture_FailsClearly()
    {
        var wav = await GenerateAsync("звук.wav", "-f", "lavfi", "-i", "sine", "-t", "1");
        var renamed = Path.Combine(_dir, "не картинка.png");
        File.Move(wav, renamed);

        var ex = await Assert.ThrowsAsync<CompressionException>(() =>
            FileJobs.Create(ActionIds.ToJpg, Tools)!.RunAsync(renamed, null, CancellationToken.None));
        Assert.Equal("Не удалось прочитать изображение.", ex.Message);
        Assert.Single(Directory.GetFiles(_dir));
    }
}
