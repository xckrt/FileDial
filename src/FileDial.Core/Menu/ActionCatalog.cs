using FileDial.Core.Compression;

namespace FileDial.Core.Menu;

/// <summary>Какие действия доступны для какого файла.</summary>
public static class ActionCatalog
{
    public const int SectorCount = 8;
    public const string NotInPrototype = "Недоступно в прототипе";
    public const string AlreadySmall = CompressionCheck.AlreadySmall;

    public static readonly IReadOnlySet<string> VideoExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".webm" };

    public static bool IsVideo(string path) =>
        VideoExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Действия для файла; пустой список — тип не поддерживается.
    /// Порядок = порядок секторов по часовой стрелке, начиная сверху (клавиши 1–8).
    /// </summary>
    /// <param name="media">Сведения ffprobe, если удалось получить; без них проверяем только размер.</param>
    public static IReadOnlyList<DialAction> ForFile(string path, long sizeBytes, ProbeInfo? media = null)
    {
        if (!IsVideo(path))
            return [];

        // Не предлагаем то, что заведомо не получится или не нужно.
        var whyNot = CompressionCheck.WhyNot(sizeBytes, media);
        return
        [
            new(ActionIds.Compress25, "Сжать до 25 МБ", whyNot is null, whyNot),
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
