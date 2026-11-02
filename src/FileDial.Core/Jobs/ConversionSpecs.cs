using System.Globalization;
using FileDial.Core.Compression;
using FileDial.Core.Menu;

namespace FileDial.Core.Jobs;

/// <summary>Аргументы ffmpeg для однопроходных действий над видео, изображениями и звуком.</summary>
public static class ConversionSpecs
{
    private const long Slack = 10_000_000;

    public const int GifMaxSeconds = 30;
    public const int GifFps = 12;
    public const int GifLongSide = 480;

    public static ConversionSpec? For(string actionId) => actionId switch
    {
        // --- Видео ---
        ActionIds.ExtractAudio => new()
        {
            Suffix = "",
            Needs = MediaNeeds.Sound,
            OutputExtension = (_, info) => ExtractedAudioExtension(info),
            Arguments = (_, info) => ["-map", "0:a:0", "-vn", .. ExtractAudioCodec(info)],
            EstimateBytes = (size, _) => size + Slack,
            Timed = true,
        },
        ActionIds.Mute => new()
        {
            Suffix = "_без_звука",
            Needs = MediaNeeds.Picture,
            OutputExtension = (src, _) => Path.GetExtension(src).ToLowerInvariant(),
            // Видео копируется без перекодирования — быстро и без потерь.
            Arguments = (src, _) => ["-map", "0:v:0", "-c:v", "copy", "-an", .. VideoMuxer(Path.GetExtension(src))],
            EstimateBytes = (size, _) => size + Slack,
            Timed = true,
        },
        ActionIds.Frame => new()
        {
            Suffix = "_кадр",
            Needs = MediaNeeds.Picture,
            OutputExtension = (_, _) => ".png",
            // Первый кадр часто чёрный (затемнение), берём кадр на трети длительности.
            InputArguments = (_, info) => info.DurationSeconds > 0 ? ["-ss", Seconds(info.DurationSeconds / 3)] : [],
            Arguments = (_, _) => ["-map", "0:v:0", "-frames:v", "1", "-map_metadata", "-1", .. ImageEncoder(".png")],
            EstimateBytes = (_, info) => (long)Math.Max(info.Width, 1) * Math.Max(info.Height, 1) * 4 + Slack,
        },
        ActionIds.Gif => new()
        {
            Suffix = "",
            Needs = MediaNeeds.Picture,
            OutputExtension = (_, _) => ".gif",
            Arguments = (_, info) => ["-filter_complex", GifFilter(info), "-map", "[out]", "-loop", "0", "-f", "gif"],
            EstimateBytes = (_, info) => (long)(Math.Max(info.DurationSeconds, 1) * 2_000_000) + Slack,
            Timed = true,
        },
        ActionIds.Convert => new()
        {
            Suffix = "",
            Needs = MediaNeeds.Picture,
            OutputExtension = (_, _) => ".mp4",
            Arguments = (_, info) => ToMp4(info),
            EstimateBytes = (size, _) => size * 2 + Slack,
            Timed = true,
        },

        // --- Изображения ---
        ActionIds.ToJpg => Image("", (_, _) => ".jpg", (_, info) => [.. FlattenOnWhite(info, null), .. ImageEncoder(".jpg")]),
        ActionIds.ToPng => Image("", (_, _) => ".png", (_, _) => ImageEncoder(".png")),
        ActionIds.ToWebp => Image("", (_, _) => ".webp", (_, _) => ImageEncoder(".webp")),
        ActionIds.Resize1920 => Image($"_{ActionCatalog.ResizeLongSide}px", (src, _) => SameImageFormat(src), (src, _) =>
            [.. Filter(ResizeFilter(ActionCatalog.ResizeLongSide)), .. ImageEncoder(SameImageFormat(src))]),
        ActionIds.RotateRight => Image("_повернуто", (src, _) => SameImageFormat(src), (src, _) =>
            [.. Filter("transpose=clock"), .. ImageEncoder(SameImageFormat(src))]),

        // --- Звук ---
        ActionIds.ToMp3 => Audio("", _ => ".mp3", (_, info) => AudioEncoder(".mp3", info), (size, _) => size * 3 + Slack),
        ActionIds.ToM4a => Audio("", _ => ".m4a", (_, info) => AudioEncoder(".m4a", info), (size, _) => size * 3 + Slack),
        // Несжатый WAV: до 48 кГц × 2 канала × 2 байта = 192 000 байт/с.
        ActionIds.ToWav => Audio("", _ => ".wav", (_, info) => AudioEncoder(".wav", info),
            (_, info) => (long)(Math.Max(info.DurationSeconds, 1) * 192_000) + Slack),
        ActionIds.Normalize => SameFormatAudio("_выровнено",
            // loudnorm внутри работает на 192 кГц, поэтому частоту возвращаем к исходной (см. AudioEncoder).
            "loudnorm=I=-16:TP=-1.5:LRA=11"),
        ActionIds.SpeedUp => SameFormatAudio("_x1,5", "atempo=1.5"),
        ActionIds.Denoise => SameFormatAudio("_без_шума", "afftdn=nf=-25"),
        _ => null,
    };

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // --- Видео ---

    /// <summary>Если звук уже в «обычном» кодеке — копируем без потерь, иначе кодируем в AAC.</summary>
    public static string ExtractedAudioExtension(ProbeInfo info) => info.AudioCodec switch
    {
        "aac" => ".m4a",
        "mp3" => ".mp3",
        "opus" => ".opus",
        "vorbis" => ".ogg",
        "flac" => ".flac",
        _ => ".m4a",
    };

    private static IReadOnlyList<string> ExtractAudioCodec(ProbeInfo info) => info.AudioCodec switch
    {
        "aac" => ["-c:a", "copy", "-movflags", "+faststart", "-f", "ipod"],
        "mp3" => ["-c:a", "copy", "-f", "mp3"],
        "opus" => ["-c:a", "copy", "-f", "opus"],
        "vorbis" => ["-c:a", "copy", "-f", "ogg"],
        "flac" => ["-c:a", "copy", "-f", "flac"],
        _ => AudioEncoder(".m4a", info),
    };

    public static IReadOnlyList<string> VideoMuxer(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp4" => ["-movflags", "+faststart", "-f", "mp4"],
        ".mov" => ["-movflags", "+faststart", "-f", "mov"],
        ".mkv" => ["-f", "matroska"],
        ".webm" => ["-f", "webm"],
        _ => throw new ArgumentOutOfRangeException(nameof(extension), extension, "Неподдерживаемый формат видео"),
    };

    /// <summary>GIF с собственной палитрой: 12 кадров/с, длинная сторона до 480 px.</summary>
    public static string GifFilter(ProbeInfo info)
    {
        var scale = Math.Max(info.Width, info.Height) > GifLongSide ? "," + ResizeFilter(GifLongSide) : "";
        return $"[0:v:0]fps={GifFps}{scale},split[a][b];[a]palettegen=stats_mode=diff[p];" +
               "[b][p]paletteuse=dither=bayer:bayer_scale=4[out]";
    }

    /// <summary>В MP4: H.264 + AAC уже подходят — копируем без перекодирования; иначе H.264 CRF 20 + AAC 192 кбит/с.</summary>
    public static IReadOnlyList<string> ToMp4(ProbeInfo info)
    {
        string[] maps = ["-map", "0:v:0", "-map", "0:a:0?"];
        var videoOk = info.VideoCodec == "h264";
        var audioOk = !info.HasAudio || info.AudioCodec == "aac";
        if (videoOk && audioOk)
            return [.. maps, "-c", "copy", "-movflags", "+faststart", "-f", "mp4"];

        return [.. maps,
            .. videoOk ? ["-c:v", "copy"] : new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p" },
            .. audioOk ? ["-c:a", "copy"] : new[] { "-c:a", "aac", "-b:a", "192k" },
            "-movflags", "+faststart", "-f", "mp4"];
    }

    // --- Изображения ---

    private static ConversionSpec Image(string suffix, Func<string, ProbeInfo, string> extension,
        Func<string, ProbeInfo, IReadOnlyList<string>> args) => new()
    {
        Suffix = suffix,
        Needs = MediaNeeds.Picture,
        MissingPictureMessage = "Не удалось прочитать изображение.",
        OutputExtension = extension,
        // Первый кадр: у GIF и WebP может быть анимация, берём одно изображение.
        Arguments = (src, info) =>
        {
            var specific = args(src, info);
            // С -filter_complex поток выбирает сам граф (метка [out]); иначе — первая видеодорожка.
            IReadOnlyList<string> map = specific.Contains("-filter_complex") ? [] : ["-map", "0:v:0"];
            return [.. map, "-frames:v", "1", "-map_metadata", "-1", .. specific];
        },
        // PNG из JPG бывает в разы больше исходника.
        EstimateBytes = (size, _) => size * 6 + Slack,
    };

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

    public static IReadOnlyList<string> ImageEncoder(string extension, int jpegQuality = 2) => extension switch
    {
        // yuvj444p — без потери цвета на мелких деталях; JPEG не умеет прозрачность.
        ".jpg" or ".jpeg" => ["-c:v", "mjpeg", "-q:v", jpegQuality.ToString(CultureInfo.InvariantCulture),
            "-pix_fmt", "yuvj444p", "-f", "image2", "-update", "1"],
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
        Func<string, ProbeInfo, IReadOnlyList<string>> args, Func<long, ProbeInfo, long> estimate) => new()
    {
        Suffix = suffix,
        Needs = MediaNeeds.Sound,
        OutputExtension = (src, _) => extension(src),
        // Только звук: обложку и видеодорожку не переносим.
        Arguments = (src, info) => ["-map", "0:a:0", "-vn", .. args(src, info)],
        EstimateBytes = estimate,
        Timed = true,
    };

    private static ConversionSpec SameFormatAudio(string suffix, string filter) =>
        Audio(suffix, SameAudioFormat, (src, info) => ["-af", filter, .. AudioEncoder(SameAudioFormat(src), info)],
            (size, _) => size * 2 + Slack);

    /// <summary>Формат результата для обработки звука — тот же, что у исходника.</summary>
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
