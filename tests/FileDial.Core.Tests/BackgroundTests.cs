using System.Net;
using System.Text;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;
using FileDial.Core.Menu;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

/// <summary>Есть и ffmpeg, и модель IS-Net (скачанная в %LOCALAPPDATA%\FileDial\models или рядом с тестами).</summary>
public sealed class BackgroundModelFactAttribute : FactAttribute
{
    public BackgroundModelFactAttribute()
    {
        if (FfmpegLocator.LocateDefault() is null)
            Skip = "ffmpeg и ffprobe не найдены рядом с тестами или в PATH";
        else if (BackgroundModels.FindDefault() is null)
            Skip = "Модель isnet-general-use.onnx не скачана";
    }
}

[Collection(LanguageCollection.Name)]
public class BackgroundTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public BackgroundTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string FakeModelFile()
    {
        var dir = Path.Combine(_root, "модели");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, BackgroundModels.FileName);
        File.WriteAllText(path, "x");
        return path;
    }

    private static DialAction Sector(JobSettings settings) =>
        ActionCatalog.ForFile(@"C:\p\фото.jpg", 5_000_000, null, settings).Single(a => a.Id == ActionIds.RemoveBackground);

    // --- Круг ---

    [Fact]
    public void Catalog_NoModel_DisabledWithDownloadFix()
    {
        var sector = Sector(JobSettings.Default);
        Assert.False(sector.IsEnabled);
        Assert.Equal("Нужна модель для фона (179 МБ)", sector.DisabledReason);
        Assert.Equal(ActionIds.SetupBackground, sector.FixActionId);

        // Путь есть, а файла нет (удалили) — так же предлагаем скачать.
        var gone = Sector(new JobSettings { BackgroundModel = Path.Combine(_root, "нет", BackgroundModels.FileName) });
        Assert.False(gone.IsEnabled);
        Assert.Equal(ActionIds.SetupBackground, gone.FixActionId);
    }

    [Fact]
    public void Catalog_WithModel_Enabled_AlsoForBatch()
    {
        var settings = new JobSettings { BackgroundModel = FakeModelFile() };
        var sector = Sector(settings);
        Assert.True(sector.IsEnabled);
        Assert.Null(sector.DisabledReason);
        Assert.Null(sector.FixActionId);

        var batch = ActionCatalog.ForBatch(FileKind.Image, [@"C:\p\a.png", @"C:\p\b.jpg"], [1, 1], settings);
        Assert.True(batch.Single(a => a.Id == ActionIds.RemoveBackground).IsEnabled);
    }

    [Fact]
    public void Catalog_InEnglish()
    {
        using var _ = new EnglishScope();
        Assert.Equal("Needs the background model (179 MB)", Sector(JobSettings.Default).DisabledReason);
        Assert.Equal("_no_bg", Lang.T.BgSuffix);
    }

    [Fact]
    public void Job_IsImplemented_AndUsesGivenModel()
    {
        Assert.True(FileJobs.IsImplemented(ActionIds.RemoveBackground));
        Assert.True(FileJobs.NeedsFfmpeg(ActionIds.RemoveBackground));
        Assert.IsType<RemoveBackgroundJob>(FileJobs.Create(ActionIds.RemoveBackground, new FfmpegTools("ffmpeg", "ffprobe"),
            new JobSettings { BackgroundModel = FakeModelFile() }));
    }

    [Fact]
    public async Task Job_WithoutModel_SaysWhereToGetIt()
    {
        var job = new RemoveBackgroundJob(new FfmpegTools("ffmpeg", "ffprobe"), Path.Combine(_root, "нет.onnx"));
        var ex = await Assert.ThrowsAsync<CompressionException>(() => job.RunAsync(@"C:\p\a.png", null, CancellationToken.None));
        Assert.Equal(Lang.T.BgModelMissing, ex.Message);
    }

    [Fact]
    public void Find_TakesFirstFolderWithModel()
    {
        var empty = Path.Combine(_root, "пусто");
        Directory.CreateDirectory(empty);
        var model = FakeModelFile();
        Assert.Equal(model, BackgroundModels.Find(empty, Path.Combine(_root, "нет такой папки"), Path.GetDirectoryName(model)!));
        Assert.Null(BackgroundModels.Find(empty));
    }

    // --- Загрузка ---

    private sealed class FakeHttp(byte[] body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Assert.Equal(BackgroundModels.Url, request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    [Fact]
    public async Task Download_RejectsWrongFile_AndLeavesNothing()
    {
        var folder = Path.Combine(_root, "загрузка");
        await Assert.ThrowsAsync<InvalidDataException>(() => BackgroundModels.DownloadAsync(
            new HttpClient(new FakeHttp(new byte[1000])), folder, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task Download_SkipsWhenAlreadyPresent()
    {
        var folder = Path.Combine(_root, "уже есть");
        Directory.CreateDirectory(folder);
        using (var file = File.Create(Path.Combine(folder, BackgroundModels.FileName)))
            file.SetLength(BackgroundModels.SizeBytes);
        var http = new FakeHttp([]);

        var path = await BackgroundModels.DownloadAsync(new HttpClient(http), folder, null, CancellationToken.None);
        Assert.Equal(Path.Combine(folder, BackgroundModels.FileName), path);
        Assert.Equal(0, http.Requests);
    }

    // --- Математика ---

    [Fact]
    public void ToTensor_SameSize_IsPlanarRgb_DividedBy255_MinusMean()
    {
        // 2×2: красный, зелёный / синий, белый; прозрачность не влияет.
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 0, 0, 0, 255, 255, 255, 255, 255, 10];
        var t = BackgroundMask.ToTensor(rgba, 2, 2, size: 2);

        Assert.Equal(12, t.Length);
        float[] mean = [0.485f, 0.456f, 0.406f];
        float[][] expected =
        [
            [1, 0, 0, 1], // R по пикселям
            [0, 1, 0, 1], // G
            [0, 0, 1, 1], // B
        ];
        for (var c = 0; c < 3; c++)
            for (var i = 0; i < 4; i++)
                Assert.Equal(expected[c][i] - mean[c], t[c * 4 + i], 4);
    }

    [Fact]
    public void ToTensor_Downscale_KeepsFlatColor_AndAverages()
    {
        // Ровная заливка остаётся ровной при любом масштабе (веса нормированы).
        var flat = Enumerable.Range(0, 37 * 23).SelectMany(_ => new byte[] { 51, 102, 204, 255 }).ToArray();
        var t = BackgroundMask.ToTensor(flat, 37, 23, size: 8);
        Assert.All(t.Take(64), v => Assert.Equal(0.2f - 0.485f, v, 4));
        Assert.All(t.Skip(128), v => Assert.Equal(0.8f - 0.406f, v, 4));

        // Чередование чёрных и белых столбцов при уменьшении вдвое усредняется в серый, а не выбрасывается.
        var stripes = new byte[16 * 2 * 4];
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 16; x += 2)
                Array.Fill(stripes, (byte)255, (y * 16 + x) * 4, 4);
        var gray = BackgroundMask.ToTensor(stripes, 16, 2, size: 8);
        Assert.All(gray.Take(8 * 8), v => Assert.InRange(v + 0.485f, 0.35f, 0.65f));
    }

    [Fact]
    public void Normalize_StretchesToZeroOne_FlatMeansKeepEverything()
    {
        Assert.Equal([0f, 0.5f, 1f], BackgroundMask.Normalize([2f, 4f, 6f]));
        Assert.Equal([1f, 1f], BackgroundMask.Normalize([0.3f, 0.3f]));
    }

    [Fact]
    public void Apply_MultipliesAlpha_AndUpscalesMask()
    {
        // Маска того же размера: прозрачность исходника умножается на маску.
        byte[] rgba = [9, 9, 9, 255, 9, 9, 9, 128, 9, 9, 9, 255, 9, 9, 9, 255];
        BackgroundMask.Apply(rgba, 2, 2, [0.5f, 0.5f, 1f, 0f], 2);
        Assert.Equal(new byte[] { 128, 64, 255, 0 }, new[] { rgba[3], rgba[7], rgba[11], rgba[15] });
        Assert.Equal(9, rgba[0]); // цвет не трогаем

        // Маска 4×4 (слева фон, справа объект) растягивается до 16×8: края чёткие, в середине — плавный переход.
        float[] mask = [0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1];
        var image = Enumerable.Repeat((byte)255, 16 * 8 * 4).ToArray();
        BackgroundMask.Apply(image, 16, 8, mask, 4);
        byte Alpha(int x, int y) => image[(y * 16 + x) * 4 + 3];
        for (var y = 0; y < 8; y++)
        {
            Assert.Equal(0, Alpha(0, y));
            Assert.Equal(0, Alpha(5, y));
            Assert.Equal(255, Alpha(10, y));
            Assert.Equal(255, Alpha(15, y));
            Assert.InRange(Alpha(7, y), 1, 254);
        }
    }

    [Fact]
    public async Task Pam_ReadsHeaderAndPixels()
    {
        var header = "P7\nWIDTH 2\nHEIGHT 1\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n"u8.ToArray();
        byte[] pixels = [1, 2, 3, 4, 5, 6, 7, 8];
        var (w, h, rgba) = await Pam.ReadAsync(new MemoryStream([.. header, .. pixels]), CancellationToken.None);
        Assert.Equal((2, 1), (w, h));
        Assert.Equal(pixels, rgba);

        var rgb = Encoding.ASCII.GetBytes("P7\nWIDTH 1\nHEIGHT 1\nDEPTH 3\nMAXVAL 255\nENDHDR\n");
        await Assert.ThrowsAsync<InvalidDataException>(() => Pam.ReadAsync(new MemoryStream([.. rgb, 1, 2, 3]), CancellationToken.None));
    }

    // --- Настоящая модель ---

    [BackgroundModelFact]
    public async Task RealModel_RedCircleOnWhite_BackgroundBecomesTransparent()
    {
        var tools = FfmpegLocator.LocateDefault()!;
        var dir = Path.Combine(_root, "Мои картинки");
        Directory.CreateDirectory(dir);
        var source = Path.Combine(dir, "красный круг.jpg");
        const string inside = "lt(hypot(X-320,Y-240),150)";
        var make = await ProcessRunner.RunAsync(tools.Ffmpeg,
        [
            "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-f", "lavfi", "-i",
            $"color=white:s=640x480,format=rgb24,geq=r=255:g='if({inside},30,255)':b='if({inside},30,255)'",
            "-frames:v", "1", "-q:v", "2", source,
        ], null, captureOutput: false, CancellationToken.None);
        Assert.Equal(0, make.ExitCode);
        var before = File.ReadAllBytes(source);

        var stages = new List<string>();
        var job = FileJobs.Create(ActionIds.RemoveBackground, tools, new JobSettings { BackgroundModel = BackgroundModels.FindDefault() })!;
        var result = await job.RunAsync(source, new SyncProgress(p => stages.Add(p.Stage)), CancellationToken.None);

        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Equal(Path.Combine(dir, "красный круг_без_фона.png"), result.OutputPath);
        Assert.Equal(before, File.ReadAllBytes(source)); // исходник не изменён
        Assert.Empty(Directory.GetFiles(dir, ".filedial-*"));
        Assert.Contains(Lang.T.BgStageRemoving, stages);

        // Читаем результат обратно: углы прозрачные, центр круга — непрозрачный и красный.
        var decoded = await ProcessRunner.RunAsync(tools.Ffprobe,
            ["-v", "error", "-show_entries", "stream=width,height,pix_fmt", "-of", "csv=p=0", result.OutputPath!],
            null, captureOutput: true, CancellationToken.None);
        Assert.Equal("640,480,rgba", decoded.StandardOutput.Trim());
        var raw = Path.Combine(_root, "result.rgba");
        var dump = await ProcessRunner.RunAsync(tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", result.OutputPath!, "-f", "rawvideo", "-pix_fmt", "rgba", raw],
            null, captureOutput: false, CancellationToken.None);
        Assert.Equal(0, dump.ExitCode);
        var pixels = File.ReadAllBytes(raw);
        byte At(int x, int y, int c) => pixels[(y * 640 + x) * 4 + c];

        foreach (var (x, y) in new[] { (5, 5), (634, 5), (5, 474), (634, 474) })
            Assert.True(At(x, y, 3) < 20, $"угол ({x},{y}): прозрачность {At(x, y, 3)}");
        Assert.True(At(320, 240, 3) > 235, $"центр: {At(320, 240, 3)}");
        Assert.True(At(320, 240, 0) > 200 && At(320, 240, 1) < 80, "центр остаётся красным");
    }

    private sealed class SyncProgress(Action<JobProgress> a) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => a(value);
    }
}
