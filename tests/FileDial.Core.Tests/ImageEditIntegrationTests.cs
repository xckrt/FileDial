using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

/// <summary>
/// HEIC/AVIF (собранные <see cref="HeifSamples"/> как у iPhone: сетка плиток, irot, миниатюра, Exif), повороты,
/// отражения и кадрирование — настоящим ffmpeg, с проверкой размеров и цветов по четвертям картинки.
/// </summary>
public class ImageEditIntegrationTests : IDisposable
{
    /// <summary>700×500: красный | зелёный сверху, синий | жёлтый снизу.</summary>
    private const string Quadrants = "color=red:s=350x250[a];color=lime:s=350x250[b];color=blue:s=350x250[c];" +
                                     "color=yellow:s=350x250[d];[a][b]hstack[t];[c][d]hstack[bt];[t][bt]vstack";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));
    private readonly string _dir;

    public ImageEditIntegrationTests()
    {
        _dir = Path.Combine(_root, "Фото с iPhone");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static FfmpegTools Tools => FfmpegLocator.LocateDefault()!;

    private async Task<string> HeifAsync(string name, int tile = 256, int rotation = 0, bool thumbnail = false,
        int? exif = null, HeifSamples.Codec codec = HeifSamples.Codec.Hevc)
    {
        var path = Path.Combine(_dir, name);
        await HeifSamples.WriteAsync(Tools, path, Quadrants, 700, 500, codec, tile, rotation, thumbnail: thumbnail, exifOrientation: exif);
        return path;
    }

    private async Task<string> GenerateAsync(string name, string source)
    {
        var path = Path.Combine(_dir, name);
        var result = await ProcessRunner.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-f", "lavfi", "-i", source, "-frames:v", "1", "-update", "1", path],
            null, captureOutput: false, CancellationToken.None);
        Assert.Equal(0, result.ExitCode);
        return path;
    }

    private async Task<JobResult> RunAsync(IFileJob job, string source)
    {
        var before = File.ReadAllBytes(source);
        var result = await job.RunAsync(source, null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, result.Status);
        Assert.True(File.Exists(result.OutputPath));
        Assert.Equal(before, File.ReadAllBytes(source)); // исходник не изменён
        Assert.Empty(Directory.GetFiles(_dir, ".filedial-*"));
        return result;
    }

    private Task<JobResult> RunAsync(string actionId, string source) => RunAsync(FileJobs.Create(actionId, Tools)!, source);

    /// <summary>Размер результата и цвета в четвертях: левый верх, правый верх, левый низ, правый низ.</summary>
    private async Task<(int Width, int Height, string[] Corners)> LookAsync(string path)
    {
        var info = await ProbeInfo.RunAsync(Tools, path, CancellationToken.None);
        var raw = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".rgb");
        var decode = await ProcessRunner.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", path, .. info.PictureArguments(null),
                "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", raw],
            null, captureOutput: false, CancellationToken.None);
        Assert.Equal(0, decode.ExitCode);
        var rgb = File.ReadAllBytes(raw);
        Assert.Equal(info.Width * info.Height * 3, rgb.Length); // ffprobe и декодер согласны о размере после поворота

        string At(double fx, double fy)
        {
            var x = (int)(info.Width * fx);
            var y = (int)(info.Height * fy);
            var i = (y * info.Width + x) * 3;
            return (rgb[i] > 150, rgb[i + 1] > 150, rgb[i + 2] > 150) switch
            {
                (true, false, false) => "red",
                (false, true, false) => "lime",
                (false, false, true) => "blue",
                (true, true, false) => "yellow",
                (true, true, true) => "white",
                _ => $"rgb({rgb[i]},{rgb[i + 1]},{rgb[i + 2]})",
            };
        }
        return (info.Width, info.Height, [At(0.1, 0.1), At(0.9, 0.1), At(0.1, 0.9), At(0.9, 0.9)]);
    }

    [FfmpegFact]
    public async Task IphoneLikeHeic_Grid_IsReadWhole_AndUpright()
    {
        // Сетка 3×2 плиток 256 px, обрезанная до 700×500; irot = 270° против часовой (= 90° по часовой);
        // впереди миниатюра; в Exif — Orientation 6, который читатель HEIF не должен применять второй раз.
        var heic = await HeifAsync("IMG_0001.HEIC", rotation: 3, thumbnail: true, exif: 6);

        var info = await ProbeInfo.RunAsync(Tools, heic, CancellationToken.None);
        Assert.True(info.IsTileGrid);
        Assert.Equal((500, 700), (info.Width, info.Height));

        var jpg = await RunAsync(ActionIds.ToJpg, heic);
        Assert.Equal(Path.Combine(_dir, "IMG_0001.jpg"), jpg.OutputPath);
        var look = await LookAsync(jpg.OutputPath!);
        Assert.Equal((500, 700), (look.Width, look.Height));
        Assert.Equal(["blue", "red", "yellow", "lime"], look.Corners);

        // Подпись в центре круга — настоящее разрешение, а не плитка 256×256.
        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(SelectionInterpreter.Interpret(
            Selection.SelectionResult.Success([heic]), new Selection.FileSystemProbe(),
            new FfprobeMediaProbe(Tools, TimeSpan.FromSeconds(20)))).Menu;
        Assert.EndsWith("· 500×700", menu.Subtitle);
    }

    [FfmpegFact]
    public async Task Heic_SingleImageWithThumbnailFirst_UsesPrimary()
    {
        var heic = await HeifAsync("одна картинка.heic", tile: 0, rotation: 1, thumbnail: true);

        var info = await ProbeInfo.RunAsync(Tools, heic, CancellationToken.None);
        Assert.Equal("0:v:1", info.Picture);
        var png = await RunAsync(ActionIds.ToPng, heic);
        var look = await LookAsync(png.OutputPath!);
        Assert.Equal((500, 700), (look.Width, look.Height));
        Assert.Equal(["lime", "yellow", "red", "blue"], look.Corners); // 90° против часовой
    }

    [FfmpegFact]
    public async Task Heic_Gif_Ico_Pdf_Work()
    {
        var heic = await HeifAsync("сетка.heic");

        var gif = await LookAsync((await RunAsync(ActionIds.ImageTo(".gif"), heic)).OutputPath!);
        Assert.Equal((700, 500), (gif.Width, gif.Height));
        Assert.Equal(["red", "lime", "blue", "yellow"], gif.Corners);

        var ico = await RunAsync(ActionIds.ImageToIco, heic);
        Assert.EndsWith(".ico", ico.OutputPath);
        var pdf = await RunAsync(ActionIds.ImageToPdf, heic);
        Assert.EndsWith(".pdf", pdf.OutputPath);
    }

    [FfmpegFact]
    public async Task Heic_Rotate180_FlipVertical_ToJpg()
    {
        var heic = await HeifAsync("повороты.heic");

        var turned = await RunAsync(ActionIds.Rotate180, heic);
        Assert.Equal(Path.Combine(_dir, "повороты_повернуто_180.jpg"), turned.OutputPath);
        Assert.Equal(["yellow", "blue", "lime", "red"], (await LookAsync(turned.OutputPath!)).Corners);

        var flipped = await RunAsync(ActionIds.MirrorVertical, heic);
        var look = await LookAsync(flipped.OutputPath!);
        Assert.Equal((700, 500), (look.Width, look.Height));
        Assert.Equal(["blue", "yellow", "red", "lime"], look.Corners);
    }

    [FfmpegFact]
    public async Task Avif_Grid_Rotated_ResizeKeepsUprightProportions()
    {
        var avif = await HeifAsync("снимок.avif", rotation: 1, codec: HeifSamples.Codec.Av1);

        var result = await RunAsync(new ImageResizeJob(Tools, new ResizeRequest(ResizeBy.Percent, 50)), avif);
        Assert.Equal(Path.Combine(_dir, "снимок_50%.jpg"), result.OutputPath);
        Assert.StartsWith("500×700 → 250×350.", result.Message);
        var look = await LookAsync(result.OutputPath!);
        Assert.Equal((250, 350), (look.Width, look.Height));
        Assert.Equal(["lime", "yellow", "red", "blue"], look.Corners);
    }

    [FfmpegFact]
    public async Task Crop_Png_RightSquare_AndFreeRegion_ExactPixels()
    {
        var png = await GenerateAsync("четверти.png", Quadrants);

        var square = await RunAsync(new ImageCropJob(Tools, new CropRequest(new CropAspect(1, 1), CropAnchor.Right)), png);
        Assert.Equal(Path.Combine(_dir, "четверти_кадр_1x1.png"), square.OutputPath);
        Assert.StartsWith("700×500 → 500×500.", square.Message);
        var look = await LookAsync(square.OutputPath!);
        Assert.Equal((500, 500), (look.Width, look.Height));
        Assert.Equal(["red", "lime", "blue", "yellow"], look.Corners); // кадр x = 200…700: слева ещё красный

        var region = new CropRequest(CropAspect.Free, Region: new CropRegion(0.5, 0.5, 0.5, 0.5));
        var quarter = await RunAsync(new ImageCropJob(Tools, region), png);
        Assert.Equal(Path.Combine(_dir, "четверти_кадр_350x250.png"), quarter.OutputPath);
        var q = await LookAsync(quarter.OutputPath!);
        Assert.Equal((350, 250), (q.Width, q.Height));
        Assert.All(q.Corners, c => Assert.Equal("yellow", c));
    }

    [FfmpegFact]
    public async Task Crop_RotatedHeicGrid_CentersSquareOnUprightImage()
    {
        var heic = await HeifAsync("портрет.heic", rotation: 3);

        var result = await RunAsync(new ImageCropJob(Tools, new CropRequest(new CropAspect(1, 1))), heic);
        Assert.Equal(Path.Combine(_dir, "портрет_кадр_1x1.jpg"), result.OutputPath);
        Assert.StartsWith("500×700 → 500×500.", result.Message);
        var look = await LookAsync(result.OutputPath!);
        Assert.Equal((500, 500), (look.Width, look.Height));
        Assert.Equal(["blue", "red", "yellow", "lime"], look.Corners);

        // Уже квадрат — отрезать нечего, файлов не появляется.
        var squarePath = result.OutputPath!;
        var nothing = await new ImageCropJob(Tools, new CropRequest(new CropAspect(1, 1))).RunAsync(squarePath, null, CancellationToken.None);
        Assert.Equal(JobStatus.NothingToDo, nothing.Status);
    }

    [FfmpegFact]
    public async Task Crop_Batch_TopAnchor_OnPortraitAndLandscape()
    {
        var landscape = await GenerateAsync("альбом.png", Quadrants);
        var portrait = await HeifAsync("портрет.heic", rotation: 3);
        var job = new ImageCropJob(Tools, new CropRequest(new CropAspect(1, 1), CropAnchor.Top));

        var a = await LookAsync((await RunAsync(job, landscape)).OutputPath!);
        Assert.Equal(["red", "lime", "blue", "yellow"], a.Corners); // по ширине — по центру
        var b = await LookAsync((await RunAsync(job, portrait)).OutputPath!);
        Assert.Equal((500, 500), (b.Width, b.Height));
        // Верхние 500 из 700: верхняя половина (350) целиком и 150 строк нижней — внизу уже жёлтый и зелёный.
        Assert.Equal(["blue", "red", "yellow", "lime"], b.Corners);
    }

    [FfmpegFact]
    public async Task Jpeg_WithExifOrientation_SizesAreUpright()
    {
        // 80×20: слева красный, справа зелёный; Orientation 6 — показывать повёрнутым на 90° по часовой.
        var plain = await GenerateAsync("плоский.jpg", "color=red:s=40x20[a];color=lime:s=40x20[b];[a][b]hstack");
        var bytes = File.ReadAllBytes(plain);
        byte[] tiff = [0x4D, 0x4D, 0, 0x2A, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, 6, 0, 0, 0, 0, 0, 0];
        byte[] payload = [.. "Exif"u8, 0, 0, .. tiff];
        var length = payload.Length + 2;
        var jpg = Path.Combine(_dir, "с телефона.jpg");
        File.WriteAllBytes(jpg, [.. bytes[..2], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload, .. bytes[2..]]);

        var info = await ProbeInfo.RunAsync(Tools, jpg, CancellationToken.None);
        Assert.Equal((20, 80), (info.Width, info.Height));

        var resized = await RunAsync(new ImageResizeJob(Tools, new ResizeRequest(ResizeBy.Percent, 50)), jpg);
        Assert.StartsWith("20×80 → 10×40.", resized.Message);
        var bmp = await LookAsync((await RunAsync(ActionIds.ImageTo(".bmp"), jpg)).OutputPath!);
        Assert.Equal((20, 80), (bmp.Width, bmp.Height)); // белый фон под картинкой — того же, повёрнутого размера
        Assert.Equal(["red", "red", "lime", "lime"], bmp.Corners);
    }
}
