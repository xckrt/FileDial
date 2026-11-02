namespace FileDial.Core.Menu;

/// <summary>Один сектор круга. Для неактивного сектора <see cref="DisabledReason"/> объясняет почему.</summary>
public sealed record DialAction(string Id, string Label, bool IsEnabled, string? DisabledReason = null);

public static class ActionIds
{
    public const string Compress25 = "compress-25mb";
    public const string Trim = "trim";
    public const string Gif = "gif";
    public const string ExtractAudio = "extract-audio";
    public const string Frame = "frame";
    public const string Subtitles = "subtitles";
    public const string Mute = "mute";
    public const string Convert = "convert";
}
