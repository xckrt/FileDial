using FileDial.Core.Compression;

namespace FileDial.Core.Menu;

/// <summary>
/// Какие действия доступны для какого файла. Действие, которое заведомо не нужно или не получится,
/// показывается неактивным с причиной — лучше честно сказать сразу, чем упасть после клика.
/// </summary>
public static class ActionCatalog
{
    public const int SectorCount = 8;
    public const string NotInPrototype = "Недоступно в прототипе";
    public const string AlreadySmall = CompressionCheck.AlreadySmall;
    public const int ResizeLongSide = 1920;

    public static bool IsVideo(string path) => FileKinds.Of(path) == FileKind.Video;

    /// <summary>
    /// Действия для файла; пустой список — тип не поддерживается.
    /// Порядок = порядок секторов по часовой стрелке, начиная сверху (клавиши 1–8).
    /// </summary>
    /// <param name="media">Сведения ffprobe, если удалось получить; без них проверяем только то, что знаем.</param>
    public static IReadOnlyList<DialAction> ForFile(string path, long sizeBytes, ProbeInfo? media = null) =>
        FileKinds.Of(path) switch
        {
            FileKind.Video => ForVideo(sizeBytes, media),
            FileKind.Image => ForImage(path, media),
            FileKind.Audio => ForAudio(path),
            _ => [],
        };

    private static IReadOnlyList<DialAction> ForVideo(long sizeBytes, ProbeInfo? media)
    {
        var whyNot = CompressionCheck.WhyNot(sizeBytes, media);
        return
        [
            Action(ActionIds.Compress25, "Сжать до 25 МБ", whyNot),
            Stub(ActionIds.Trim, "Обрезать"),
            Stub(ActionIds.Gif, "Сделать GIF"),
            Stub(ActionIds.ExtractAudio, "Извлечь звук"),
            Stub(ActionIds.Frame, "Кадр в картинку"),
            Stub(ActionIds.Subtitles, "Субтитры"),
            Stub(ActionIds.Mute, "Убрать звук"),
            Stub(ActionIds.Convert, "Формат"),
        ];
    }

    private static IReadOnlyList<DialAction> ForImage(string path, ProbeInfo? media)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        string? resizeWhyNot = null;
        if (media is { Width: > 0, Height: > 0 } && Math.Max(media.Width, media.Height) <= ResizeLongSide)
            resizeWhyNot = $"Уже не больше {ResizeLongSide} px ({media.Width}×{media.Height})";

        return
        [
            Action(ActionIds.ToJpg, "В JPG", ext is ".jpg" or ".jpeg" ? "Файл уже в JPG" : null),
            Action(ActionIds.ToPng, "В PNG", ext == ".png" ? "Файл уже в PNG" : null),
            Action(ActionIds.ToWebp, "В WebP", ext == ".webp" ? "Файл уже в WebP" : null),
            Action(ActionIds.Resize1920, $"Уменьшить до {ResizeLongSide} px", resizeWhyNot),
            Action(ActionIds.RotateRight, "Повернуть вправо", null),
            Stub(ActionIds.ImageCompress1Mb, "Сжать до 1 МБ"),
            Stub(ActionIds.RemoveBackground, "Убрать фон"),
            Stub(ActionIds.ImageToPdf, "В PDF"),
        ];
    }

    private static IReadOnlyList<DialAction> ForAudio(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return
        [
            Action(ActionIds.ToMp3, "В MP3", ext == ".mp3" ? "Файл уже в MP3" : null),
            Action(ActionIds.ToM4a, "В M4A", ext == ".m4a" ? "Файл уже в M4A" : null),
            Action(ActionIds.ToWav, "В WAV", ext == ".wav" ? "Файл уже в WAV" : null),
            Action(ActionIds.Normalize, "Выровнять громкость", null),
            Stub(ActionIds.AudioTrim, "Обрезать"),
            Stub(ActionIds.Denoise, "Убрать шум"),
            Stub(ActionIds.SpeedUp, "Ускорить"),
            Stub(ActionIds.SpeechToText, "В текст"),
        ];
    }

    private static DialAction Action(string id, string label, string? whyNot) => new(id, label, whyNot is null, whyNot);

    private static DialAction Stub(string id, string label) => new(id, label, false, NotInPrototype);
}
