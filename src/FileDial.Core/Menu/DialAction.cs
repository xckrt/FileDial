namespace FileDial.Core.Menu;

/// <summary>Один сектор круга. Для неактивного сектора <see cref="DisabledReason"/> объясняет почему.</summary>
public sealed record DialAction(string Id, string Label, bool IsEnabled, string? DisabledReason = null);

public static class ActionIds
{
    // Видео
    public const string Compress25 = "compress-25mb";
    public const string Trim = "trim";
    public const string Gif = "gif";
    public const string ExtractAudio = "extract-audio";
    public const string Frame = "frame";
    public const string Subtitles = "subtitles";
    public const string Mute = "mute";
    public const string Convert = "convert";

    // Изображения
    public const string ToJpg = "image-to-jpg";
    public const string ToPng = "image-to-png";
    public const string ToWebp = "image-to-webp";
    public const string Resize1920 = "image-resize-1920";
    public const string RotateRight = "image-rotate-right";
    public const string ImageCompress1Mb = "image-compress-1mb";
    public const string RemoveBackground = "image-remove-background";
    public const string ImageToPdf = "image-to-pdf";

    // Звук
    public const string ToMp3 = "audio-to-mp3";
    public const string ToM4a = "audio-to-m4a";
    public const string ToWav = "audio-to-wav";
    public const string Normalize = "audio-normalize";
    public const string AudioTrim = "audio-trim";
    public const string Denoise = "audio-denoise";
    public const string SpeedUp = "audio-speed-up";
    public const string SpeechToText = "audio-to-text";
}
