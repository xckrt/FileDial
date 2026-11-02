using FileDial.Core.Compression;

namespace FileDial.Core.Menu;

/// <summary>Какие действия доступны для какого файла.</summary>
public static class ActionCatalog
{
    public const int SectorCount = 8;
    public const string NotInPrototype = "Недоступно в прототипе";
    public const string AlreadySmall = "Файл уже меньше 25 МБ";

    public static readonly IReadOnlySet<string> VideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".webm" };

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Действия для файла; пустой список — тип не поддерживается.
    /// Порядок = порядок секторов по часовой стрелке, начиная сверху (клавиши 1–8).
    /// </summary>
    public static IReadOnlyList<DialAction> ForFile(string path, long sizeBytes)
    {
        if (!IsVideo(path))
            return [];

        // Сжимать до 25 МБ то, что уже не больше 25 МБ, бессмысленно.
        var canCompress = sizeBytes > BitrateCalculator.DefaultTargetBytes;
        return
        [
            new(ActionIds.Compress25, "Сжать до 25 МБ", canCompress, canCompress ? null : AlreadySmall),
            Stub(ActionIds.Trim, "Обрезать"),
            Stub(ActionIds.Gif, "Сделать GIF"),
            Stub(ActionIds.ExtractAudio, "Извлечь звук"),
            Stub(ActionIds.Frame, "Кадр в картинку"),
            Stub(ActionIds.Subtitles, "Субтитры"),
            Stub(ActionIds.Mute, "Убрать звук"),
            Stub(ActionIds.Convert, "Формат"),
        ];
    }

    private static DialAction Stub(string id, string label) => new(id, label, false, NotInPrototype);
}
