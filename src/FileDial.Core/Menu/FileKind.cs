namespace FileDial.Core.Menu;

public enum FileKind
{
    Other,
    Video,
    Image,
    Audio,
}

public static class FileKinds
{
    public static readonly IReadOnlySet<string> Video = Set(".mp4", ".mov", ".mkv", ".webm");
    public static readonly IReadOnlySet<string> Image = Set(".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".tif", ".tiff");
    public static readonly IReadOnlySet<string> Audio = Set(".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma");

    public static FileKind Of(string path)
    {
        var ext = Path.GetExtension(path);
        if (Video.Contains(ext)) return FileKind.Video;
        if (Image.Contains(ext)) return FileKind.Image;
        if (Audio.Contains(ext)) return FileKind.Audio;
        return FileKind.Other;
    }

    private static HashSet<string> Set(params string[] extensions) =>
        new(extensions, StringComparer.OrdinalIgnoreCase);
}
