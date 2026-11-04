using System.Globalization;
using FileDial.Core.Compression;
using FileDial.Core.Localization;

namespace FileDial.Core.Jobs;

/// <summary>Пропорции кадра; 0:0 — свободные.</summary>
public readonly record struct CropAspect(int Width, int Height)
{
    public static CropAspect Free => default;

    /// <summary>Готовые пропорции в окне: квадрат, портреты, затем альбомные.</summary>
    public static readonly IReadOnlyList<CropAspect> Presets =
        [new(1, 1), new(4, 5), new(3, 4), new(2, 3), new(9, 16), new(16, 9), new(3, 2), new(4, 3)];

    public bool IsFree => Width <= 0 || Height <= 0;

    /// <summary>Та же пропорция в другой ориентации (портрет ↔ альбом).</summary>
    public CropAspect Swapped => new(Height, Width);

    /// <summary>Пропорции картинки, сокращённые (4032×3024 → 4:3).</summary>
    public static CropAspect Of(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return Free;
        var gcd = Gcd(width, height);
        return new(width / gcd, height / gcd);
    }

    public override string ToString() => IsFree ? "free" : Invariant($"{Width}:{Height}");

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    internal static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}

/// <summary>К какому краю прижать кадр, когда его выбирают не руками (несколько картинок).</summary>
public enum CropAnchor
{
    Center,
    Top,
    Bottom,
    Left,
    Right,
}

/// <summary>Прямоугольник в пикселях картинки (после поворота).</summary>
public readonly record struct CropRect(int X, int Y, int Width, int Height);

/// <summary>Прямоугольник в долях картинки (0…1) — от размера превью не зависит.</summary>
public readonly record struct CropRegion(double X, double Y, double Width, double Height);

/// <summary>
/// Что вырезать. <see cref="Region"/> — выбранная руками область одной картинки; без неё — наибольший кадр нужных
/// пропорций, прижатый по <see cref="Anchor"/>. Пропорции соблюдаются точно, насколько позволяют целые пиксели.
/// </summary>
public sealed record CropRequest(CropAspect Aspect, CropAnchor Anchor = CropAnchor.Center, CropRegion? Region = null)
{
    /// <summary>Кадр в пикселях для картинки <paramref name="width"/>×<paramref name="height"/>.</summary>
    public CropRect Rectangle(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return new(0, 0, Math.Max(width, 0), Math.Max(height, 0));

        if (Region is not { } region)
        {
            if (Aspect.IsFree)
                return new(0, 0, width, height);
            var (w, h) = Largest(width, height, Aspect);
            var x = Anchor switch
            {
                CropAnchor.Left => 0,
                CropAnchor.Right => width - w,
                _ => (width - w) / 2,
            };
            var y = Anchor switch
            {
                CropAnchor.Top => 0,
                CropAnchor.Bottom => height - h,
                _ => (height - h) / 2,
            };
            return new(x, y, w, h);
        }

        // Область из окна: сначала в пиксели, затем точные пропорции внутри неё с тем же центром.
        var rx = Math.Clamp(region.X, 0, 1) * width;
        var ry = Math.Clamp(region.Y, 0, 1) * height;
        var rw = Math.Clamp(region.Width, 0, 1) * width;
        var rh = Math.Clamp(region.Height, 0, 1) * height;
        rw = Math.Min(rw, width - rx);
        rh = Math.Min(rh, height - ry);
        int cw, ch;
        if (Aspect.IsFree)
        {
            cw = Math.Clamp((int)Math.Round(rw), 1, width);
            ch = Math.Clamp((int)Math.Round(rh), 1, height);
        }
        else
        {
            (cw, ch) = Largest(Math.Max(1, (int)Math.Round(rw)), Math.Max(1, (int)Math.Round(rh)), Aspect);
        }
        var cx = (int)Math.Round(rx + rw / 2 - cw / 2.0);
        var cy = (int)Math.Round(ry + rh / 2 - ch / 2.0);
        return new(Math.Clamp(cx, 0, width - cw), Math.Clamp(cy, 0, height - ch), cw, ch);
    }

    /// <summary>Наибольший кадр пропорций <paramref name="aspect"/>, помещающийся в <paramref name="width"/>×<paramref name="height"/>.</summary>
    public static (int Width, int Height) Largest(int width, int height, CropAspect aspect)
    {
        if (aspect.IsFree)
            return (width, height);
        // Шире нужного — во всю высоту, иначе — во всю ширину. Вторая сторона округляется и не выходит за край.
        if ((long)width * aspect.Height >= (long)height * aspect.Width)
        {
            var w = (int)Math.Min(width, Math.Round((double)height * aspect.Width / aspect.Height));
            return (Math.Max(1, w), height);
        }
        var h = (int)Math.Min(height, Math.Round((double)width * aspect.Height / aspect.Width));
        return (width, Math.Max(1, h));
    }

    /// <summary>Есть что отрезать.</summary>
    public bool Crops(int width, int height)
    {
        var r = Rectangle(width, height);
        return r.Width < width || r.Height < height;
    }

    /// <summary>Добавка к имени: «_кадрировано_4x5» / «_crop_4x5», для свободного кадра — размер в пикселях.</summary>
    public string SuffixFor(int width, int height)
    {
        var r = Rectangle(width, height);
        var tail = Aspect.IsFree ? CropAspect.Invariant($"{r.Width}x{r.Height}") : CropAspect.Invariant($"{Aspect.Width}x{Aspect.Height}");
        return Lang.T.ImgCropSuffix + "_" + tail;
    }
}

/// <summary>
/// Запомненный выбор в окне «Кадрировать…»: пропорции (свободные, как у картинки или готовые) и край для нескольких картинок.
/// В настройках: «free», «original», «4:5», с краем — «4:5@top».
/// </summary>
public sealed record CropChoice(CropAspect Aspect, bool Original = false, CropAnchor Anchor = CropAnchor.Center)
{
    public static CropChoice Default { get; } = new(CropAspect.Free);

    public string Format()
    {
        var aspect = Original ? "original" : Aspect.ToString();
        return Anchor == CropAnchor.Center ? aspect : aspect + "@" + Anchor.ToString().ToLowerInvariant();
    }

    public static CropChoice Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Default;
        var parts = text.Trim().Split('@');
        if (parts.Length > 2)
            return Default;
        var anchor = CropAnchor.Center;
        if (parts.Length == 2)
        {
            // Только имена: Enum.TryParse принял бы и числа.
            var named = Enum.GetValues<CropAnchor>()
                .Where(a => a.ToString().Equals(parts[1], StringComparison.OrdinalIgnoreCase)).ToList();
            if (named.Count == 0)
                return Default;
            anchor = named[0];
        }
        switch (parts[0])
        {
            case "free":
                return new(CropAspect.Free, false, anchor);
            case "original":
                return new(CropAspect.Free, true, anchor);
        }
        if (parts[0].Split(':') is [var w, var h]
            && int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var wi) && wi is >= 1 and <= 100
            && int.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out var hi) && hi is >= 1 and <= 100)
            return new(new CropAspect(wi, hi), false, anchor);
        return Default;
    }
}

/// <summary>
/// «Кадрировать…»: вырезает кадр нужных пропорций, формат тот же (HEIC/AVIF — в JPG). Исходник не меняется.
/// Если кадр совпадает со всей картинкой — делать нечего (в пакете такие файлы пропускаются).
/// </summary>
public sealed class ImageCropJob(FfmpegTools tools, CropRequest request, string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.JobStageAnalyzing, null));
        var info = await ProbeInfo.RunAsync(tools, sourcePath, ct).ConfigureAwait(false);
        if (!info.HasVideo || info.Width <= 0 || info.Height <= 0)
            throw new CompressionException(Lang.T.JobCannotReadImage);
        if (!request.Crops(info.Width, info.Height))
            return new JobResult(JobStatus.NothingToDo, Lang.T.ImgCropNothing(info.Width, info.Height));

        var r = request.Rectangle(info.Width, info.Height);
        var spec = ConversionSpecs.Crop(request, request.SuffixFor(info.Width, info.Height));
        var result = await new ConversionJob(tools, spec, outputDirectory).RunAsync(sourcePath, progress, ct).ConfigureAwait(false);
        return result with { Message = $"{info.Width}×{info.Height} → {r.Width}×{r.Height}. {result.Message}" };
    }
}

/// <summary>Уменьшенная копия картинки для окна кадрирования: PNG через ffmpeg (WPF не читает HEIC, AVIF и часть WebP).</summary>
public static class ImagePreview
{
    /// <summary>Рисует картинку (уже повёрнутой, как её видит пользователь) с длинной стороной не больше <paramref name="longSide"/>.</summary>
    public static async Task RenderAsync(FfmpegTools tools, string source, ProbeInfo info, int longSide, string output,
        CancellationToken ct)
    {
        var scale = Math.Max(info.Width, info.Height) > longSide ? ConversionSpecs.ResizeFilter(longSide) : "null";
        List<string> args = ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", source,
            .. info.PictureArguments(scale), "-frames:v", "1", "-map_metadata", "-1", .. ConversionSpecs.ImageEncoder(".png"), output];
        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, null, captureOutput: false, ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output))
            throw new CompressionException(Lang.T.JobCannotReadImage, result.ErrorTail);
    }
}
