using System.Globalization;
using FileDial.Core.Compression;
using FileDial.Core.Localization;

namespace FileDial.Core.Jobs;

public enum ResizeBy
{
    /// <summary>Длинная сторона не больше N px.</summary>
    LongSide,
    /// <summary>N процентов от исходного размера.</summary>
    Percent,
    /// <summary>Свой размер: вписать в рамку Ширина×Высота (с пропорциями) или ровно в неё (без).</summary>
    Exact,
}

/// <summary>
/// Во что уменьшить картинку. Только уменьшение: картинку меньше цели не растягиваем — для неё делать нечего.
/// </summary>
public sealed record ResizeRequest(ResizeBy Mode, int Value, int Width = 0, int Height = 0, bool KeepAspect = true)
{
    public static readonly IReadOnlyList<int> LongSides = [3840, 2560, 1920, 1280, 1024, 800, 640];
    public static readonly IReadOnlyList<int> Percents = [75, 50, 33, 25];
    public const int MaxSide = 65535;

    public static ResizeRequest Default { get; } = new(ResizeBy.LongSide, 1920);

    /// <summary>Размер результата для картинки <paramref name="width"/>×<paramref name="height"/>.</summary>
    public (int Width, int Height) Target(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return (width, height);
        switch (Mode)
        {
            case ResizeBy.Exact when !KeepAspect:
                // Ровно в рамку, но не больше исходника ни по одной стороне.
                return (Math.Clamp(Width, 1, width), Math.Clamp(Height, 1, height));
            case ResizeBy.Exact:
                return Scale(width, height, Math.Min((double)Width / width, (double)Height / height));
            case ResizeBy.Percent:
                return Scale(width, height, Value / 100.0);
            default:
                return Scale(width, height, (double)Value / Math.Max(width, height));
        }
    }

    private static (int, int) Scale(int width, int height, double factor)
    {
        if (factor >= 1)
            return (width, height);
        return (Math.Max(1, (int)Math.Round(width * factor)), Math.Max(1, (int)Math.Round(height * factor)));
    }

    /// <summary>Есть что уменьшать.</summary>
    public bool Shrinks(int width, int height)
    {
        var (w, h) = Target(width, height);
        return w < width || h < height;
    }

    /// <summary>Добавка к имени: «_1920px», «_50%», «_800x600».</summary>
    public string Suffix => Mode switch
    {
        ResizeBy.Percent => $"_{Value}%",
        ResizeBy.Exact => $"_{Width}x{Height}",
        _ => $"_{Value}px",
    };

    public bool IsValid => Mode switch
    {
        ResizeBy.Exact => Width is >= 1 and <= MaxSide && Height is >= 1 and <= MaxSide,
        ResizeBy.Percent => Value is >= 1 and <= 99,
        _ => Value is >= 1 and <= MaxSide,
    };

    /// <summary>Для настроек: «long:1920», «pct:50», «box:800x600», «exact:800x600».</summary>
    public string Format() => Mode switch
    {
        ResizeBy.Percent => Invariant($"pct:{Value}"),
        ResizeBy.Exact => Invariant($"{(KeepAspect ? "box" : "exact")}:{Width}x{Height}"),
        _ => Invariant($"long:{Value}"),
    };

    public static ResizeRequest Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Split(':') is not [var kind, var value])
            return Default;
        ResizeRequest? request = kind switch
        {
            "long" when TryInt(value, out var n) => new(ResizeBy.LongSide, n),
            "pct" when TryInt(value, out var n) => new(ResizeBy.Percent, n),
            "box" or "exact" when value.Split('x') is [var w, var h] && TryInt(w, out var wi) && TryInt(h, out var hi) =>
                new(ResizeBy.Exact, 0, wi, hi, KeepAspect: kind == "box"),
            _ => null,
        };
        return request is { IsValid: true } ? request : Default;
    }

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// «Уменьшить…»: картинка уменьшается до выбранного размера (lanczos), формат тот же. Если картинка уже не больше
/// цели — ничего не делаем (в пакете такие файлы пропускаются).
/// </summary>
public sealed class ImageResizeJob(FfmpegTools tools, ResizeRequest request, string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.JobStageAnalyzing, null));
        var info = await ProbeInfo.RunAsync(tools, sourcePath, ct).ConfigureAwait(false);
        if (!info.HasVideo || info.Width <= 0 || info.Height <= 0)
            throw new CompressionException(Lang.T.JobCannotReadImage);
        if (!request.Shrinks(info.Width, info.Height))
            return new JobResult(JobStatus.NothingToDo, Lang.T.RszAlreadySmall(info.Width, info.Height));

        var (w, h) = request.Target(info.Width, info.Height);
        var result = await new ConversionJob(tools, ConversionSpecs.Resize(request), outputDirectory)
            .RunAsync(sourcePath, progress, ct).ConfigureAwait(false);
        return result with { Message = $"{info.Width}×{info.Height} → {w}×{h}. {result.Message}" };
    }
}
