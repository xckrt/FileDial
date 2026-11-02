using System.Globalization;

namespace FileDial.Core.Compression;

/// <summary>Разбор строк вида key=value из ffmpeg -progress.</summary>
public static class FfmpegProgressParser
{
    /// <summary>Достаёт позицию кодирования из out_time_us / out_time_ms (оба в микросекундах) или out_time.</summary>
    public static bool TryParseOutTime(string? line, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrEmpty(line))
            return false;

        var eq = line.IndexOf('=');
        if (eq <= 0)
            return false;
        var key = line.AsSpan(0, eq).Trim();
        var value = line.AsSpan(eq + 1).Trim();

        if (key is "out_time_us" or "out_time_ms")
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) || us < 0)
                return false;
            time = TimeSpan.FromMicroseconds(us);
            return true;
        }

        if (key is "out_time" &&
            TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed >= TimeSpan.Zero)
        {
            time = parsed;
            return true;
        }

        return false;
    }

    /// <summary>Общая доля (0..1) для двух проходов: первый — 0..0.5, второй — 0.5..1.</summary>
    public static double OverallFraction(int pass, TimeSpan position, double durationSeconds)
    {
        if (durationSeconds <= 0)
            return pass >= 2 ? 0.5 : 0;
        var withinPass = Math.Clamp(position.TotalSeconds / durationSeconds, 0, 1);
        return Math.Clamp((pass - 1 + withinPass) / 2.0, 0, 1);
    }
}
