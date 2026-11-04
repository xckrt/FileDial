using System.Globalization;
using System.Text.Json;
using FileDial.Core.Menu;

namespace FileDial.Core.Compression;

/// <summary>Нужные для расчёта сведения из ffprobe.</summary>
/// <remarks>
/// Ширина и высота — как картинка выглядит, то есть уже после поворота: ffmpeg сам поворачивает кадр по матрице
/// поворота дорожки, по EXIF у JPEG и по irot у HEIC, поэтому при повороте на 90° стороны меняются местами.
/// </remarks>
public sealed record ProbeInfo(double DurationSeconds, bool HasVideo, bool HasAudio, int Width, int Height,
    int AudioChannels = 0, int AudioSampleRate = 0, string? VideoCodec = null, string? AudioCodec = null)
{
    /// <summary>Главная картинка для ffmpeg: «0:v:0», «0:v:N» (HEIC с миниатюрой впереди) или «0:g:N» (сетка плиток HEIC/AVIF).</summary>
    public string Picture { get; init; } = "0:v:0";

    /// <summary>Картинка собрана из плиток: ffmpeg склеивает её сам, но только в -filter_complex.</summary>
    public bool IsTileGrid => Picture.StartsWith("0:g:", StringComparison.Ordinal);

    /// <summary>
    /// Выбор главной картинки и фильтр к ней (null — без фильтра). Сетку плиток нельзя взять через -map и -vf
    /// (ffmpeg собирает её своим графом), поэтому для неё — -filter_complex с выходом [out].
    /// </summary>
    public IReadOnlyList<string> PictureArguments(string? filter) =>
        IsTileGrid ? ["-filter_complex", $"[{Picture}]{filter ?? "null"}[out]", "-map", "[out]"]
        : filter is null ? ["-map", Picture]
        : ["-map", Picture, "-vf", filter];

    public static IReadOnlyList<string> Arguments(string input)
    {
        var extension = Path.GetExtension(input);
        var entries = "stream=codec_type,codec_name,width,height,channels,sample_rate:stream_side_data=rotation:format=duration";
        // Группы дорожек знает только новый ffprobe (7.1+): просим их лишь у HEIC/AVIF, чтобы старый не падал на остальном.
        if (FileKinds.Heif.Contains(extension))
            entries += ":stream_disposition=default:stream_group=type:stream_group_disposition=default" +
                       ":stream_group_component=width,height:stream_group_side_data=rotation";
        // Поворот по EXIF (JPEG с телефона) виден только в декодированном кадре — читаем один кадр.
        else if (FileKinds.Image.Contains(extension))
            return ["-v", "error", "-read_intervals", "%+#1", "-show_entries", entries + ":frame_side_data=rotation",
                "-of", "json", input];
        return ["-v", "error", "-show_entries", entries, "-of", "json", input];
    }

    /// <summary>Запускает ffprobe; при ошибке — исключение с понятным текстом.</summary>
    public static async Task<ProbeInfo> RunAsync(FfmpegTools tools, string input, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(tools.Ffprobe, Arguments(input), null,
            captureOutput: true, ct).ConfigureAwait(false);
        // Старый ffprobe не знает групп дорожек и падает — для HEIC честнее сказать, что нужен новый ffmpeg.
        if (result.ExitCode != 0)
            throw new CompressionException(FileKinds.Heif.Contains(Path.GetExtension(input))
                ? Localization.Lang.T.ImgHeifUnreadable : Localization.Lang.T.JobFfprobeCannotRead, result.ErrorTail);
        try
        {
            return Parse(result.StandardOutput);
        }
        catch (JsonException ex)
        {
            throw new CompressionException(Localization.Lang.T.JobFfprobeBadOutput, ex.Message);
        }
    }

    public static ProbeInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        double duration = 0;
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var d) &&
            d.ValueKind == JsonValueKind.String)
        {
            double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
        }

        bool hasAudio = false;
        int channels = 0, sampleRate = 0;
        string? audioCodec = null;
        var videos = new List<JsonElement>();
        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                if (type == "audio")
                {
                    // Сжимаем первую звуковую дорожку (-map 0:a:0) — её каналы и важны.
                    if (!hasAudio)
                    {
                        audioCodec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                        channels = Int(s, "channels");
                        // ffprobe отдаёт sample_rate строкой.
                        if (s.TryGetProperty("sample_rate", out var sr) && sr.ValueKind == JsonValueKind.String)
                            int.TryParse(sr.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate);
                    }
                    hasAudio = true;
                }
                else if (type == "video")
                {
                    videos.Add(s);
                }
            }
        }

        // У HEIC главная картинка помечена «по умолчанию»: впереди может стоять миниатюра.
        // (Пометки ffprobe показывает, только если их попросили, — у остальных файлов берём первую дорожку.)
        var main = Math.Max(0, videos.FindIndex(IsDefault));
        var picture = "0:v:" + main.ToString(CultureInfo.InvariantCulture);
        int width = 0, height = 0;
        double? rotation = null;
        string? videoCodec = null;
        if (videos.Count > 0)
        {
            var v = videos[main];
            videoCodec = v.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
            width = Int(v, "width");
            height = Int(v, "height");
            rotation = Rotation(v);
        }

        // Сетка плиток (HEIC с iPhone, большие AVIF): размер и поворот — у группы, а не у плиток-дорожек.
        if (root.TryGetProperty("stream_groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            var grids = groups.EnumerateArray().Select((g, i) => (Group: g, Index: i))
                .Where(x => x.Group.TryGetProperty("type", out var t) && t.GetString()?.Replace(" ", "") == "TileGrid")
                .ToList();
            if (grids.Count > 0)
            {
                var (grid, index) = grids.FirstOrDefault(x => IsDefault(x.Group), grids[0]);
                if (grid.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array
                    && components.GetArrayLength() > 0)
                {
                    var component = components[0];
                    width = Int(component, "width");
                    height = Int(component, "height");
                    rotation = Rotation(component);
                    picture = "0:g:" + index.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        // Поворот по EXIF приходит в первом кадре — он главнее (у дорожки JPEG поворота нет).
        if (root.TryGetProperty("frames", out var frames) && frames.ValueKind == JsonValueKind.Array)
        {
            foreach (var frame in frames.EnumerateArray())
            {
                if (Rotation(frame) is { } r)
                {
                    rotation = r;
                    break;
                }
            }
        }

        if (rotation is { } angle && Math.Abs((long)Math.Round(angle / 90)) % 2 == 1)
            (width, height) = (height, width);

        return new ProbeInfo(duration, videos.Count > 0, hasAudio, width, height, channels, sampleRate, videoCodec, audioCodec)
        {
            Picture = picture,
        };
    }

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static bool IsDefault(JsonElement e) =>
        e.TryGetProperty("disposition", out var d) && d.TryGetProperty("default", out var v) && v.TryGetInt32(out var i) && i == 1;

    /// <summary>Угол из матрицы поворота в side_data_list, если есть.</summary>
    private static double? Rotation(JsonElement e)
    {
        if (!e.TryGetProperty("side_data_list", out var list) || list.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in list.EnumerateArray())
            if (item.TryGetProperty("rotation", out var r) && r.TryGetDouble(out var angle))
                return angle;
        return null;
    }
}
