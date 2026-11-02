using System.Globalization;
using System.Text.Json;

namespace FileDial.Core.Compression;

/// <summary>Нужные для расчёта сведения из ffprobe.</summary>
public sealed record ProbeInfo(double DurationSeconds, bool HasVideo, bool HasAudio, int Width, int Height,
    int AudioChannels = 0, int AudioSampleRate = 0)
{
    public static IReadOnlyList<string> Arguments(string input) =>
    [
        "-v", "error",
        "-show_entries", "stream=codec_type,width,height,channels,sample_rate:format=duration",
        "-of", "json",
        input,
    ];

    /// <summary>Запускает ffprobe; при ошибке — исключение с понятным текстом.</summary>
    public static async Task<ProbeInfo> RunAsync(FfmpegTools tools, string input, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(tools.Ffprobe, Arguments(input), null,
            captureOutput: true, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new CompressionException("ffprobe не смог прочитать файл. Возможно, он повреждён.", result.ErrorTail);
        try
        {
            return Parse(result.StandardOutput);
        }
        catch (JsonException ex)
        {
            throw new CompressionException("Не удалось разобрать ответ ffprobe.", ex.Message);
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

        bool hasVideo = false, hasAudio = false;
        int width = 0, height = 0, channels = 0, sampleRate = 0;
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
                        channels = s.TryGetProperty("channels", out var ch) && ch.TryGetInt32(out var cv) ? cv : 0;
                        // ffprobe отдаёт sample_rate строкой.
                        if (s.TryGetProperty("sample_rate", out var sr) && sr.ValueKind == JsonValueKind.String)
                            int.TryParse(sr.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sampleRate);
                    }
                    hasAudio = true;
                }
                else if (type == "video" && !hasVideo)
                {
                    hasVideo = true;
                    width = s.TryGetProperty("width", out var w) && w.TryGetInt32(out var wv) ? wv : 0;
                    height = s.TryGetProperty("height", out var h) && h.TryGetInt32(out var hv) ? hv : 0;
                }
            }
        }

        return new ProbeInfo(duration, hasVideo, hasAudio, width, height, channels, sampleRate);
    }
}
