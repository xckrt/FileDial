using System.ComponentModel;
using System.Text.Json;

namespace FileDial.Core.Compression;

/// <summary>Быстрый опрос медиафайла до показа круга. null — сведений нет (не страшно, меню всё равно покажем).</summary>
public interface IMediaProbe
{
    ProbeInfo? TryProbe(string path);
}

public sealed class FfprobeMediaProbe(FfmpegTools tools, TimeSpan timeout) : IMediaProbe
{
    public ProbeInfo? TryProbe(string path)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var result = ProcessRunner.RunAsync(tools.Ffprobe, ProbeInfo.Arguments(path), null,
                captureOutput: true, cts.Token).GetAwaiter().GetResult();
            return result.ExitCode == 0 ? ProbeInfo.Parse(result.StandardOutput) : null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or JsonException or Win32Exception
                                       or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

/// <summary>Можно ли осмысленно сжать файл до цели; если нет — причина для пользователя.</summary>
public static class CompressionCheck
{
    public const string AlreadySmall = "Файл уже меньше 25 МБ";
    public const string NoVideo = "В файле нет видеодорожки";

    public static string? WhyNot(long sizeBytes, ProbeInfo? info, long targetBytes = BitrateCalculator.DefaultTargetBytes)
    {
        if (sizeBytes <= targetBytes)
            return AlreadySmall;
        if (info is null)
            return null; // не знаем — пусть решит окно сжатия, оно объяснит подробно
        if (!info.HasVideo)
            return NoVideo;
        if (info.DurationSeconds <= 0)
            return null;

        var plan = BitrateCalculator.Plan(info.DurationSeconds, info.HasAudio, info.Width, info.Height, targetBytes);
        if (plan.IsFeasible)
            return null;

        var minutes = (int)Math.Floor(BitrateCalculator.MaxDurationSeconds(info.HasAudio, targetBytes) / 60);
        return $"Видео длиннее {minutes} мин в 25 МБ не уместить";
    }
}
