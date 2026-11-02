using FileDial.Core.Compression;

namespace FileDial.Core.Tests;

/// <summary>Тест пропускается, если ffmpeg/ffprobe не найдены в PATH.</summary>
public sealed class FfmpegFactAttribute : FactAttribute
{
    public FfmpegFactAttribute()
    {
        if (FfmpegLocator.LocateDefault() is null)
            Skip = "ffmpeg и ffprobe не найдены рядом с тестами или в PATH";
    }
}

/// <summary>Настоящий запуск ffmpeg на коротком сгенерированном ролике в папке с кириллицей и пробелами.</summary>
public class VideoCompressorIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));
    private readonly string _videoDir;

    public VideoCompressorIntegrationTests()
    {
        _videoDir = Path.Combine(_root, "Мои видео с пробелами");
        Directory.CreateDirectory(_videoDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static FfmpegTools Tools => FfmpegLocator.LocateDefault()!;

    private async Task<string> MakeSourceAsync(string name, string audio = "sine=frequency=440:sample_rate=48000")
    {
        var path = Path.Combine(_videoDir, name);
        var result = await ProcessRunner.RunAsync(Tools.Ffmpeg,
        [
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error",
            "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30",
            "-f", "lavfi", "-i", audio,
            "-t", "8", "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "4M",
            "-c:a", "aac", "-b:a", "128k", "-shortest", path,
        ], null, captureOutput: false, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        return path;
    }

    private CompressionOptions Options(long target) => new()
    {
        TargetBytes = target,
        TempRoot = Path.Combine(_root, "временная папка"),
    };

    private static string Hash(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    [FfmpegFact]
    public async Task CompressesToTarget_KeepsSource_CleansTemp()
    {
        var source = await MakeSourceAsync("тестовый ролик.mp4");
        var sourceHash = Hash(source);
        const long target = 1_000_000;
        var reports = new List<CompressionProgress>();

        var result = await new VideoCompressor(Tools, Options(target))
            .CompressAsync(source, new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(CompressionStatus.Done, result.Status);
        Assert.Equal(Path.Combine(_videoDir, "тестовый ролик_25мб.mp4"), result.OutputPath);
        Assert.True(File.Exists(result.OutputPath));
        Assert.True(result.OutputSize <= target, $"Размер {result.OutputSize} больше цели {target}");
        Assert.True(result.OutputSize > target / 2, $"Размер {result.OutputSize} подозрительно мал");

        Assert.Equal(sourceHash, Hash(source)); // исходник не изменён
        Assert.Equal(2, Directory.GetFiles(_videoDir).Length); // только исходник и результат
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "временная папка", "FileDial")));
        Assert.Contains(reports, r => r.Stage == "Проход 2 из 2" && r.Fraction > 0.5);
    }

    [FfmpegFact]
    public async Task SecondRun_DoesNotOverwrite_AndLowBitrateDownscales()
    {
        var source = await MakeSourceAsync("clip.mp4");
        File.WriteAllText(Path.Combine(_videoDir, "clip_25мб.mp4"), "занято");
        // 8 с при цели 500 КБ → видео ~357 кбит/с; цель 380 КБ → ниже 300 кбит/с, кадр уменьшается.
        var result = await new VideoCompressor(Tools, Options(380_000))
            .CompressAsync(source, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_videoDir, "clip_25мб_2.mp4"), result.OutputPath);
        Assert.Equal("занято", File.ReadAllText(Path.Combine(_videoDir, "clip_25мб.mp4")));

        var probe = await ProcessRunner.RunAsync(Tools.Ffprobe, ProbeInfo.Arguments(result.OutputPath!), null,
            captureOutput: true, CancellationToken.None);
        var info = ProbeInfo.Parse(probe.StandardOutput);
        Assert.Equal(854, info.Width);
        Assert.Equal(480, info.Height);
    }

    [FfmpegFact]
    public async Task SurroundAudio_IsDownmixedToStereo()
    {
        var source = await MakeSourceAsync("5.1.mp4", "anullsrc=channel_layout=5.1:sample_rate=48000");
        var result = await new VideoCompressor(Tools, Options(1_000_000))
            .CompressAsync(source, null, CancellationToken.None);

        var probe = await ProcessRunner.RunAsync(Tools.Ffprobe, ProbeInfo.Arguments(result.OutputPath!), null,
            captureOutput: true, CancellationToken.None);
        Assert.Equal(2, ProbeInfo.Parse(probe.StandardOutput).AudioChannels);
    }

    [FfmpegFact]
    public async Task AlreadySmall_IsNotCompressed()
    {
        var source = await MakeSourceAsync("small.mp4");
        var result = await new VideoCompressor(Tools, Options(100_000_000))
            .CompressAsync(source, null, CancellationToken.None);

        Assert.Equal(CompressionStatus.AlreadySmall, result.Status);
        Assert.Single(Directory.GetFiles(_videoDir));
    }

    [FfmpegFact]
    public async Task Cancel_RemovesTemporaryFiles()
    {
        var source = await MakeSourceAsync("отмена.mp4");
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.Stage.StartsWith("Проход", StringComparison.Ordinal)) cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VideoCompressor(Tools, Options(1_000_000)).CompressAsync(source, progress, cts.Token));

        Assert.Equal([source], Directory.GetFiles(_videoDir));
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(_root, "временная папка", "FileDial")));
    }

    /// <summary>IProgress без перехода в другой поток — чтобы отчёты приходили сразу.</summary>
    private sealed class SyncProgress(Action<CompressionProgress> onReport) : IProgress<CompressionProgress>
    {
        public void Report(CompressionProgress value)
        {
            lock (this) onReport(value);
        }
    }
}
