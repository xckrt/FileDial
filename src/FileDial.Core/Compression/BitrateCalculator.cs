namespace FileDial.Core.Compression;

/// <summary>План кодирования под целевой размер.</summary>
public sealed record CompressionPlan(
    int TotalKbps,
    int VideoKbps,
    int AudioKbps,
    /// <summary>Длинная сторона кадра после уменьшения или null, если разрешение не меняется.</summary>
    int? ScaleLongSide)
{
    public bool IsFeasible => VideoKbps >= BitrateCalculator.MinVideoKbps;
}

public static class BitrateCalculator
{
    public const long DefaultTargetBytes = 25_000_000;
    public const double SafetyMargin = 0.03;
    public const int AudioKbps = 128;
    /// <summary>Ниже этого видеобитрейта уменьшаем разрешение.</summary>
    public const int LowVideoKbps = 300;
    /// <summary>Ниже этого кодировать бессмысленно — сообщаем пользователю.</summary>
    public const int MinVideoKbps = 50;

    /// <summary>Общий битрейт (кбит/с = 1000 бит/с) с запасом 3% на контейнер и погрешность кодера.</summary>
    public static int TotalKbps(double durationSeconds, long targetBytes = DefaultTargetBytes)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Длительность должна быть больше нуля.");
        if (targetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetBytes));

        var bits = targetBytes * 8.0 * (1 - SafetyMargin);
        return (int)Math.Floor(bits / durationSeconds / 1000.0);
    }

    public static CompressionPlan Plan(double durationSeconds, bool hasAudio, int width, int height,
        long targetBytes = DefaultTargetBytes)
    {
        var total = TotalKbps(durationSeconds, targetBytes);
        var audio = hasAudio ? AudioKbps : 0;
        var video = total - audio;

        int? scale = null;
        var longSide = Math.Max(width, height);
        if (video < LowVideoKbps)
        {
            var limit = LongSideLimit(video);
            if (longSide > limit)
                scale = limit;
        }

        return new CompressionPlan(total, video, audio, scale);
    }

    /// <summary>Максимальная длинная сторона для низкого битрейта (480p / 360p / 240p).</summary>
    public static int LongSideLimit(int videoKbps) => videoKbps switch
    {
        >= 200 => 854,
        >= 120 => 640,
        _ => 426,
    };
}
