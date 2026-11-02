namespace FileDial.Core.Menu;

/// <summary>Какие действия доступны для какого типа файла.</summary>
public static class ActionCatalog
{
    public const int SectorCount = 8;

    public static readonly IReadOnlySet<string> VideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".webm" };

    // Порядок = порядок секторов по часовой стрелке, начиная сверху (клавиши 1–8).
    private static readonly IReadOnlyList<DialAction> VideoActions =
    [
        new(ActionIds.Compress25, "Сжать до 25 МБ", true),
        new(ActionIds.Trim, "Обрезать", false),
        new(ActionIds.Gif, "Сделать GIF", false),
        new(ActionIds.ExtractAudio, "Извлечь звук", false),
        new(ActionIds.Frame, "Кадр в картинку", false),
        new(ActionIds.Subtitles, "Субтитры", false),
        new(ActionIds.Mute, "Убрать звук", false),
        new(ActionIds.Convert, "Формат", false),
    ];

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>Действия для файла; пустой список — тип не поддерживается.</summary>
    public static IReadOnlyList<DialAction> ForFile(string path) =>
        IsVideo(path) ? VideoActions : [];
}
