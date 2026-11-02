using System.Globalization;
using FileDial.Core.Compression;
using FileDial.Core.Menu;

namespace FileDial.Core.Jobs;

/// <summary>Аргументы ffmpeg для действий над изображениями и звуком.</summary>
public static class ConversionSpecs
{
    private const long Slack = 10_000_000;

    public static ConversionSpec? For(string actionId) => actionId switch
    {
        ActionIds.ToJpg => Image("", _ => ".jpg", (_, info) => [.. FlattenOnWhite(info, null), .. ImageEncoder(".jpg")]),
        ActionIds.ToPng => Image("", _ => ".png", (_, _) => ImageEncoder(".png")),
        ActionIds.ToWebp => Image("", _ => ".webp", (_, _) => ImageEncoder(".webp")),
        ActionIds.Resize1920 => Image($"_{ActionCatalog.ResizeLongSide}px", SameImageFormat, (src, _) =>
            [.. Filter(ResizeFilter(ActionCatalog.ResizeLongSide)), .. ImageEncoder(SameImageFormat(src))]),
        ActionIds.RotateRight => Image("_повернуто", SameImageFormat, (src, _) =>
            [.. Filter("transpose=clock"), .. ImageEncoder(SameImageFormat(src))]),

        ActionIds.ToMp3 => Audio("", _ => ".mp3", (_, info) => AudioEncoder(".mp3", info),
            (size, _) => size * 3 + Slack),
        ActionIds.ToM4a => Audio("", _ => ".m4a", (_, info) => AudioEncoder(".m4a", info),
            (size, _) => size * 3 + Slack),
        // Несжатый WAV: до 48 кГц × 2 канала × 2 байта = 192 000 байт/с.
        ActionIds.ToWav => Audio("", _ => ".wav", (_, info) => AudioEncoder(".wav", info),
            (_, info) => (long)(Math.Max(info.DurationSeconds, 1) * 192_000) + Slack),
        ActionIds.Normalize => Audio("_выровнено", SameAudioFormat, (src, info) =>
            // loudnorm внутри работает на 192 кГц, поэтому частоту возвращаем к исходной.
            ["-af", "loudnorm=I=-16:TP=-1.5:LRA=11", .. AudioEncoder(SameAudioFormat(src), info)],
            (size, _) => size * 2 + Slack),
        _ => null,
    };

    // --- Изображения ---

    private static ConversionSpec Image(string suffix, Func<string, string> extension,
        Func<string, ProbeInfo, IReadOnlyList<string>> args) =>
        new(suffix, MediaRequirement.Image, extension,
            // Первый кадр: у GIF и WebP может быть анимация, берём одно изображение.
            (src, info) =>
            {
                var specific = args(src, info);
                // С -filter_complex поток выбирает сам граф (метка [out]); иначе — первая видеодорожка.
                IReadOnlyList<string> map = specific.Contains("-filter_complex") ? [] : ["-map", "0:v:0"];
                return [.. map, "-frames:v", "1", "-map_metadata", "-1", .. specific];
            },
            // PNG из JPG бывает в разы больше исходника.
            (size, _) => size * 6 + Slack);

    /// <summary>Формат результата для поворота и уменьшения: тот же, если умеем в него писать, иначе PNG.</summary>
    public static string SameImageFormat(string sourcePath) =>
        Path.GetExtension(sourcePath).ToLowerInvariant() switch
        {
            ".jpg" => ".jpg",
            ".jpeg" => ".jpeg",
            ".webp" => ".webp",
            _ => ".png",
        };

    public static string ResizeFilter(int longSide) =>
        FfmpegArguments.ScaleFilter(longSide) + ":flags=lanczos";

    public static IReadOnlyList<string> ImageEncoder(string extension) => extension switch
    {
        // yuvj444p — без потери цвета на мелких деталях; JPEG не умеет прозрачность.
        ".jpg" or ".jpeg" => ["-c:v", "mjpeg", "-q:v", "2", "-pix_fmt", "yuvj444p", "-f", "image2", "-update", "1"],
        ".webp" => ["-c:v", "libwebp", "-quality", "90", "-f", "webp"],
        ".png" => ["-c:v", "png", "-f", "image2", "-update", "1"],
        _ => throw new ArgumentOutOfRangeException(nameof(extension), extension, "Неподдерживаемый формат изображения"),
    };

    /// <summary>
    /// В JPG нет прозрачности: прозрачные места иначе становятся чёрными. Подкладываем белый фон
    /// размером с картинку (размер знаем из ffprobe; без него оставляем как есть).
    /// </summary>
    public static IReadOnlyList<string> FlattenOnWhite(ProbeInfo info, string? extraFilter)
    {
        if (info.Width <= 0 || info.Height <= 0)
            return extraFilter is null ? [] : Filter(extraFilter);

        var size = string.Create(CultureInfo.InvariantCulture, $"{info.Width}x{info.Height}");
        var tail = extraFilter is null ? "" : "," + extraFilter;
        return ["-filter_complex",
            $"color=c=white:s={size}[bg];[0:v:0]format=rgba[fg];[bg][fg]overlay=shortest=1:format=auto{tail}[out]",
            "-map", "[out]"];
    }

    private static IReadOnlyList<string> Filter(string filter) => ["-vf", filter];

    // --- Звук ---

    private static ConversionSpec Audio(string suffix, Func<string, string> extension,
        Func<string, ProbeInfo, IReadOnlyList<string>> args, Func<long, ProbeInfo, long> estimate) =>
        new(suffix, MediaRequirement.Audio, extension,
            // Только звук: обложку и видеодорожку не переносим.
            (src, info) => ["-map", "0:a:0", "-vn", .. args(src, info)],
            estimate);

    /// <summary>Формат результата для «Выровнять громкость» — тот же, что у исходника.</summary>
    public static string SameAudioFormat(string sourcePath) => Path.GetExtension(sourcePath).ToLowerInvariant();

    public static IReadOnlyList<string> AudioEncoder(string extension, ProbeInfo info)
    {
        var rate = info.AudioSampleRate > 0 ? info.AudioSampleRate : 48_000;
        string[] codec = extension switch
        {
            ".mp3" => ["-c:a", "libmp3lame", "-q:a", "2", "-f", "mp3"],
            ".m4a" => ["-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-f", "ipod"],
            ".aac" => ["-c:a", "aac", "-b:a", "192k", "-f", "adts"],
            ".wav" => ["-c:a", "pcm_s16le", "-f", "wav"],
            ".flac" => ["-c:a", "flac", "-f", "flac"],
            ".ogg" => ["-c:a", "libvorbis", "-q:a", "6", "-f", "ogg"],
            // Opus работает только с 48 кГц.
            ".opus" => ["-c:a", "libopus", "-b:a", "160k", "-f", "opus"],
            ".wma" => ["-c:a", "wmav2", "-b:a", "192k", "-f", "asf"],
            _ => throw new ArgumentOutOfRangeException(nameof(extension), extension, "Неподдерживаемый формат звука"),
        };
        if (extension == ".opus")
            rate = 48_000;
        // MP3 не бывает выше 48 кГц.
        if (extension == ".mp3" && rate > 48_000)
            rate = 48_000;
        return [.. codec, "-ar", rate.ToString(CultureInfo.InvariantCulture)];
    }
}
