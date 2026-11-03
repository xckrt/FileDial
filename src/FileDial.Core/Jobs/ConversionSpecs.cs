using System.Globalization;
using FileDial.Core.Compression;
using FileDial.Core.Localization;
using FileDial.Core.Menu;

namespace FileDial.Core.Jobs;

/// <summary>Аргументы ffmpeg для однопроходных действий над видео, изображениями и звуком.</summary>
public static class ConversionSpecs
{
    private const long Slack = 10_000_000;

    public const int GifMaxSeconds = 30;
    public const int GifFps = 12;
    public const int GifLongSide = 480;

    /// <summary>Несжатый звук: до 48 кГц × 2 канала × 2 байта.</summary>
    private const int PcmBytesPerSecond = 192_000;

    public static ConversionSpec? For(string actionId)
    {
        if (ActionIds.TargetExtension(actionId, ActionIds.ImageToPrefix) is { } image)
            return ImageTo(image);
        if (ActionIds.TargetExtension(actionId, ActionIds.AudioToPrefix) is { } audio)
            return AudioTo(audio);
        if (ActionIds.TargetExtension(actionId, ActionIds.VideoToPrefix) is { } video)
            return VideoTo(video);

        return actionId switch
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
                Suffix = Lang.T.JobSuffixMuted,
                Needs = MediaNeeds.Picture,
                OutputExtension = (src, _) => Path.GetExtension(src).ToLowerInvariant(),
                // Видео копируется без перекодирования — быстро и без потерь.
                Arguments = (src, _) => ["-map", "0:v:0", "-c:v", "copy", "-an", .. VideoMuxer(Path.GetExtension(src))],
                EstimateBytes = (size, _) => size + Slack,
                Timed = true,
            },
            ActionIds.Frame => new()
            {
                Suffix = Lang.T.JobSuffixFrame,
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

            // --- Изображения ---
            ActionIds.Resize1920 => ImageFilter($"_{ActionCatalog.ResizeLongSide}px", ResizeFilter(ActionCatalog.ResizeLongSide)),
            ActionIds.RotateRight => ImageFilter(Lang.T.JobSuffixRotated, "transpose=clock"),
            ActionIds.RotateLeft => ImageFilter(Lang.T.JobSuffixRotatedLeft, "transpose=cclock"),
            ActionIds.Mirror => ImageFilter(Lang.T.JobSuffixMirrored, "hflip"),

            // --- Звук ---
            // loudnorm внутри работает на 192 кГц, поэтому частоту возвращаем к исходной (см. AudioEncoder).
            ActionIds.Normalize => SameFormatAudio(Lang.T.JobSuffixNormalized, "loudnorm=I=-16:TP=-1.5:LRA=11"),
            ActionIds.SpeedUp => SameFormatAudio(Lang.T.JobSuffixSpedUp, "atempo=1.5"),
            ActionIds.SlowDown => SameFormatAudio(Lang.T.JobSuffixSlowedDown, "atempo=0.75"),
            ActionIds.Denoise => SameFormatAudio(Lang.T.JobSuffixDenoised, "afftdn=nf=-25"),
            ActionIds.ToMono => SameFormatAudio(Lang.T.JobSuffixMono, "aformat=channel_layouts=mono"),
            _ => null,
        };
    }

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // --- Видео ---

    private static ConversionSpec? VideoTo(string extension)
    {
        Func<ProbeInfo, IReadOnlyList<string>>? args = extension switch
        {
            ".mp4" => ToMp4,
            ".mov" => ToMov,
            ".webm" => ToWebm,
            ".mkv" => ToMkv,
            _ => null,
        };
        if (args is null)
            return null;

        return new()
        {
            Suffix = "",
            Needs = MediaNeeds.Picture,
            OutputExtension = (_, _) => extension,
            Arguments = (_, info) => args(info),
            EstimateBytes = (size, _) => size * 2 + Slack,
            Timed = true,
        };
    }

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

    private static readonly string[] X264 = ["-c:v", "libx264", "-preset", "medium", "-crf", "20", "-pix_fmt", "yuv420p"];
    private static readonly string[] Aac192 = ["-c:a", "aac", "-b:a", "192k"];
    private static readonly string[] Vp9 = ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1",
        "-deadline", "good", "-cpu-used", "4", "-pix_fmt", "yuv420p"];
    private static readonly string[] Opus128 = ["-c:a", "libopus", "-b:a", "128k", "-ar", "48000"];

    /// <summary>В MP4: H.264 + AAC уже подходят — копируем без перекодирования; иначе H.264 CRF 20 + AAC 192 кбит/с.</summary>
    public static IReadOnlyList<string> ToMp4(ProbeInfo info) =>
        Remux(info, ["h264"], ["aac"], X264, Aac192, VideoMuxer(".mp4"));

    /// <summary>В MOV: H.264/HEVC + AAC копируются, остальное — в H.264 + AAC.</summary>
    public static IReadOnlyList<string> ToMov(ProbeInfo info) =>
        Remux(info, ["h264", "hevc"], ["aac"], X264, Aac192, VideoMuxer(".mov"));

    /// <summary>В WebM: VP8/VP9/AV1 + Opus/Vorbis копируются, остальное — в VP9 + Opus.</summary>
    public static IReadOnlyList<string> ToWebm(ProbeInfo info) =>
        Remux(info, ["vp8", "vp9", "av1"], ["opus", "vorbis"], Vp9, Opus128, VideoMuxer(".webm"));

    /// <summary>MKV принимает почти любые кодеки — всегда перепаковка без потерь.</summary>
    public static IReadOnlyList<string> ToMkv(ProbeInfo info) =>
        ["-map", "0:v:0", "-map", "0:a:0?", "-c", "copy", .. VideoMuxer(".mkv")];

    private static IReadOnlyList<string> Remux(ProbeInfo info, string[] okVideo, string[] okAudio,
        string[] videoEncoder, string[] audioEncoder, IReadOnlyList<string> muxer)
    {
        string[] maps = ["-map", "0:v:0", "-map", "0:a:0?"];
        var videoOk = info.VideoCodec is { } v && okVideo.Contains(v);
        var audioOk = !info.HasAudio || info.AudioCodec is { } a && okAudio.Contains(a);
        if (videoOk && audioOk)
            return [.. maps, "-c", "copy", .. muxer];

        return [.. maps,
            .. videoOk ? ["-c:v", "copy"] : videoEncoder,
            .. audioOk ? ["-c:a", "copy"] : audioEncoder,
            .. muxer];
    }

    // --- Изображения ---

    private static ConversionSpec? ImageTo(string extension) => extension switch
    {
        // В JPG и BMP без прозрачности подкладываем белый фон, иначе прозрачное станет чёрным.
        ".jpg" or ".bmp" => Image("", (_, _) => extension, (_, info) => [.. FlattenOnWhite(info, null), .. ImageEncoder(extension)]),
        ".png" or ".webp" or ".tiff" => Image("", (_, _) => extension, (_, _) => ImageEncoder(extension)),
        // GIF — 256 цветов: своя палитра под картинку, а не стандартная.
        ".gif" => Image("", (_, _) => ".gif", (_, _) =>
            ["-filter_complex", "[0:v:0]split[a][b];[a]palettegen=reserve_transparent=1[p];[b][p]paletteuse[out]",
                "-map", "[out]", "-f", "gif"]),
        _ => null,
    };

    private static ConversionSpec Image(string suffix, Func<string, ProbeInfo, string> extension,
        Func<string, ProbeInfo, IReadOnlyList<string>> args) => new()
    {
        Suffix = suffix,
        Needs = MediaNeeds.Picture,
        MissingPictureMessage = Lang.T.JobCannotReadImage,
        OutputExtension = extension,
        // Первый кадр: у GIF и WebP может быть анимация, берём одно изображение.
        Arguments = (src, info) =>
        {
            var specific = args(src, info);
            // С -filter_complex поток выбирает сам граф (метка [out]); иначе — первая видеодорожка.
            IReadOnlyList<string> map = specific.Contains("-filter_complex") ? [] : ["-map", "0:v:0"];
            return [.. map, "-frames:v", "1", "-map_metadata", "-1", .. specific];
        },
        // PNG и BMP из JPG бывают в разы больше исходника.
        EstimateBytes = (size, info) => Math.Max(size * 6, (long)info.Width * info.Height * 4) + Slack,
    };

    /// <summary>Фильтр с сохранением формата (поворот, отражение, уменьшение).</summary>
    private static ConversionSpec ImageFilter(string suffix, string filter) =>
        Image(suffix, (src, _) => SameImageFormat(src), (src, _) => [.. Filter(filter), .. ImageEncoder(SameImageFormat(src))]);

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
        ".bmp" => ["-c:v", "bmp", "-pix_fmt", "bgr24", "-f", "image2", "-update", "1"],
        ".tiff" => ["-c:v", "tiff", "-compression_algo", "lzw", "-f", "image2", "-update", "1"],
        _ => throw new ArgumentOutOfRangeException(nameof(extension), extension, "Неподдерживаемый формат изображения"),
    };

    /// <summary>
    /// Подкладываем белый фон размером с картинку для форматов без прозрачности
    /// (размер знаем из ffprobe; без него оставляем как есть).
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

    private static ConversionSpec? AudioTo(string extension)
    {
        if (extension is not (".mp3" or ".m4a" or ".wav" or ".flac" or ".ogg" or ".opus"))
            return null;
        // Несжатый и сжатый без потерь звук считаем по длительности; остальное — от размера исходника.
        Func<long, ProbeInfo, long> estimate = extension is ".wav" or ".flac"
            ? (_, info) => (long)(Math.Max(info.DurationSeconds, 1) * PcmBytesPerSecond) + Slack
            : (size, _) => size * 3 + Slack;
        return Audio("", _ => extension, (_, info) => AudioEncoder(extension, info), estimate);
    }

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
