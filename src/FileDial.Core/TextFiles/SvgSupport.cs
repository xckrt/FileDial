using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using FileDial.Core.Compression;

namespace FileDial.Core.TextFiles;

/// <summary>
/// SVG рисует ffmpeg декодером librsvg (он есть в «full»-сборках gyan.dev). Есть ли он — по списку
/// «ffmpeg -decoders», один раз на каждый ffmpeg.
/// </summary>
public static class SvgSupport
{
    private static readonly ConcurrentDictionary<string, bool> Decoders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Для тестов: подменить ответ «есть ли декодер SVG» (null — спросить ffmpeg).</summary>
    internal static bool? Override { get; set; }

    /// <summary>Есть ли в ffmpeg декодер SVG. Без ffmpeg — true: окно действия само предложит его установить.</summary>
    public static bool DecoderAvailable(FfmpegTools? tools)
    {
        if (Override is { } forced) return forced;
        if (tools is null) return true;
        return Decoders.GetOrAdd(tools.Ffmpeg, ffmpeg =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-decoders"], null, captureOutput: true, cts.Token)
                    .GetAwaiter().GetResult();
                return result.ExitCode == 0 && HasSvgDecoder(result.StandardOutput);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                           or IOException or OperationCanceledException)
            {
                return false;
            }
        });
    }

    /// <summary>Строка списка декодеров вида « V....D librsvg   Librsvg rasterizer (codec svg)».</summary>
    public static bool HasSvgDecoder(string decodersOutput) =>
        decodersOutput.Split('\n').Any(line =>
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && parts[0].StartsWith('V') && (parts[1] is "librsvg" or "svg" || line.Contains("(codec svg)"));
        });

    /// <summary>Пропорции рисунка из viewBox или width/height; null — не указаны.</summary>
    public static (double Width, double Height)? Dimensions(string path)
    {
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                IgnoreComments = true,
            });
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.LocalName != "svg") return null;
                var width = Length(reader.GetAttribute("width"));
                var height = Length(reader.GetAttribute("height"));
                if (width is > 0 && height is > 0)
                    return (width.Value, height.Value);
                if (reader.GetAttribute("viewBox") is { } viewBox)
                {
                    var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 4
                        && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                        && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
                        && w > 0 && h > 0)
                        return (w, h);
                }
                return null;
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
        return null;
    }

    /// <summary>Длина SVG в пикселях: «120», «120px», «2in», «10mm»; проценты и em — null.</summary>
    private static double? Length(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        var units = new (string Suffix, double Px)[] { ("px", 1), ("pt", 96 / 72.0), ("pc", 16), ("mm", 96 / 25.4), ("cm", 96 / 2.54), ("in", 96) };
        var factor = 1.0;
        foreach (var (suffix, px) in units)
        {
            if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^suffix.Length];
                factor = px;
                break;
            }
        }
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number * factor
            : null;
    }

    /// <summary>Размер в пикселях с длинной стороной <paramref name="longSide"/> (чётные не нужны — это PNG).</summary>
    public static (int Width, int Height) Fit((double Width, double Height)? dimensions, int longSide)
    {
        if (dimensions is not { } d) return (longSide, longSide);
        return d.Width >= d.Height
            ? (longSide, Math.Max(1, (int)Math.Round(longSide * d.Height / d.Width)))
            : (Math.Max(1, (int)Math.Round(longSide * d.Width / d.Height)), longSide);
    }
}
