using System.Globalization;
using FileDial.Core.Compression;
using FileDial.Core.Menu;

namespace FileDial.Core.Jobs;

/// <summary>Ввод времени: «83», «83,5», «1:23», «1:23.5», «1:02:03».</summary>
public static class TimeInput
{
    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Trim().Replace(',', '.').Split(':');
        if (parts.Length > 3)
            return false;

        // Дробная часть допустима только у секунд (последней части).
        double total = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            var isSeconds = i == parts.Length - 1;
            if (part.Length == 0 || part.StartsWith('-') || part.StartsWith('+'))
                return false;
            if (!isSeconds && part.Contains('.'))
                return false;
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                return false;
            // Минуты и секунды после двоеточия — меньше 60.
            if (i > 0 && number >= 60)
                return false;
            total = total * 60 + number;
        }

        value = TimeSpan.FromSeconds(total);
        return true;
    }

    /// <summary>1:23, 1:23.5, 1:02:03 — так же, как принимает <see cref="TryParse"/>.</summary>
    public static string Format(TimeSpan value)
    {
        var tenths = (long)Math.Round(value.TotalSeconds * 10);
        var whole = tenths / 10;
        var fraction = tenths % 10;
        var hours = whole / 3600;
        var minutes = whole / 60 % 60;
        var seconds = whole % 60;
        var main = hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes}:{seconds:00}";
        return fraction == 0 ? main : $"{main}.{fraction}";
    }
}

public sealed record TrimRequest(TimeSpan Start, TimeSpan End, bool Precise)
{
    public TimeSpan Length => End - Start;

    public const double MinSeconds = 0.1;

    /// <summary>Причина, почему диапазон не подходит, или null.</summary>
    public static string? Validate(TimeSpan start, TimeSpan end, double durationSeconds)
    {
        if (start < TimeSpan.Zero)
            return "Начало не может быть отрицательным.";
        if (end <= start)
            return "Конец должен быть позже начала.";
        if ((end - start).TotalSeconds < MinSeconds)
            return "Слишком короткий отрезок.";
        // Небольшой допуск: длительность из ffprobe округлена.
        if (durationSeconds > 0 && end.TotalSeconds > durationSeconds + 0.05)
            return $"Конец позже конца файла ({TimeInput.Format(TimeSpan.FromSeconds(durationSeconds))}).";
        if (durationSeconds > 0 && start == TimeSpan.Zero && end.TotalSeconds >= durationSeconds - 0.05)
            return "Выбран весь файл — обрезать нечего.";
        return null;
    }
}

public static class TrimSpecs
{
    public const string Suffix = "_обрезка";

    /// <summary>Отрезок того же формата, что исходник. Звук всегда перекодируется (точно до сотых),
    /// видео — либо точно (перекодирование), либо быстро без перекодирования (границы по ключевым кадрам).</summary>
    public static ConversionSpec For(string sourcePath, TrimRequest request)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var kind = FileKinds.Of(sourcePath);
        var length = Seconds(request.Length.TotalSeconds);

        return new ConversionSpec
        {
            Suffix = Suffix,
            Needs = kind == FileKind.Audio ? MediaNeeds.Sound : MediaNeeds.Picture,
            OutputExtension = (_, _) => extension,
            // -ss перед -i: быстрый переход; при перекодировании ffmpeg всё равно режет точно по кадру.
            InputArguments = (_, _) => ["-ss", Seconds(request.Start.TotalSeconds)],
            Arguments = (_, info) => kind == FileKind.Audio
                ? ["-t", length, "-map", "0:a:0", "-vn", .. ConversionSpecs.AudioEncoder(extension, info)]
                : ["-t", length, .. VideoArguments(extension, request.Precise)],
            EstimateBytes = (size, _) => size + 10_000_000,
            Timed = true,
            OutputDuration = _ => request.Length.TotalSeconds,
        };
    }

    private static IReadOnlyList<string> VideoArguments(string extension, bool precise)
    {
        string[] maps = ["-map", "0:v:0", "-map", "0:a:0?"];
        if (!precise)
            return [.. maps, "-c", "copy", "-avoid_negative_ts", "make_zero", .. ConversionSpecs.VideoMuxer(extension)];

        string[] codecs = extension == ".webm"
            ? ["-c:v", "libvpx-vp9", "-crf", "30", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4",
                "-c:a", "libopus", "-b:a", "128k", "-ar", "48000"]
            : ["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k"];
        return [.. maps, .. codecs, .. ConversionSpecs.VideoMuxer(extension)];
    }

    private static string Seconds(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
