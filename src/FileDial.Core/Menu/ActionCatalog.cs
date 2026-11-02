using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Settings;

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

    /// <summary>Форматы для конвертации: подпись и расширение. Порядок = порядок во внешнем кольце.</summary>
    public static readonly IReadOnlyList<(string Label, string Extension)> VideoFormats =
        [("MP4", ".mp4"), ("MKV", ".mkv"), ("WebM", ".webm"), ("MOV", ".mov")];

    public static readonly IReadOnlyList<(string Label, string Extension)> ImageFormats =
        [("JPG", ".jpg"), ("PNG", ".png"), ("WebP", ".webp"), ("BMP", ".bmp"), ("TIFF", ".tiff"), ("GIF", ".gif")];

    public static readonly IReadOnlyList<(string Label, string Extension)> AudioFormats =
        [("MP3", ".mp3"), ("M4A", ".m4a"), ("WAV", ".wav"), ("FLAC", ".flac"), ("OGG", ".ogg"), ("Opus", ".opus")];

    public static bool IsVideo(string path) => FileKinds.Of(path) == FileKind.Video;

    /// <summary>
    /// Действия для файла; пустой список — тип не поддерживается.
    /// Порядок = порядок секторов по часовой стрелке, начиная сверху (клавиши 1–8).
    /// </summary>
    /// <param name="media">Сведения ffprobe, если удалось получить; без них проверяем только то, что знаем.</param>
    /// <param name="settings">Цели сжатия из настроек; по умолчанию 25 МБ и 1 МБ.</param>
    public static IReadOnlyList<DialAction> ForFile(string path, long sizeBytes, ProbeInfo? media = null,
        JobSettings? settings = null) =>
        FileKinds.Of(path) switch
        {
            FileKind.Video => ForVideo(path, sizeBytes, media, settings ?? JobSettings.Default),
            FileKind.Image => ForImage(path, sizeBytes, media, settings ?? JobSettings.Default),
            FileKind.Audio => ForAudio(path, media),
            _ => [],
        };

    /// <summary>Все запускаемые действия, включая вложенные в группы.</summary>
    public static IEnumerable<DialAction> Flatten(IEnumerable<DialAction> actions) =>
        actions.SelectMany(a => a.IsGroup ? a.Children! : [a]);

    private static IReadOnlyList<DialAction> ForVideo(string path, long sizeBytes, ProbeInfo? media, JobSettings settings)
    {
        // Без сведений ffprobe звук и длительность неизвестны — тогда не запрещаем, окно действия объяснит ошибку.
        var noSound = media is { HasAudio: false };
        var tooLongForGif = media is { DurationSeconds: > ConversionSpecs.GifMaxSeconds };

        return
        [
            Action(ActionIds.Compress25, $"Сжать до {JobSettings.TargetLabel(settings.VideoTargetBytes)}",
                CompressionCheck.WhyNot(sizeBytes, media, settings.VideoTargetBytes)),
            Action(ActionIds.Trim, "Обрезать", null),
            Action(ActionIds.Gif, "Сделать GIF",
                tooLongForGif ? $"GIF — только для роликов до {ConversionSpecs.GifMaxSeconds} с" : null),
            Action(ActionIds.ExtractAudio, "Извлечь звук", noSound ? "В видео нет звука" : null),
            Action(ActionIds.Frame, "Кадр в картинку", null),
            Stub(ActionIds.Subtitles, "Субтитры"),
            Action(ActionIds.Mute, "Убрать звук", noSound ? "В видео и так нет звука" : null),
            DialAction.Group(ActionIds.Convert, "Формат", Formats(path, VideoFormats, ActionIds.VideoTo)),
        ];
    }

    private static IReadOnlyList<DialAction> ForImage(string path, long sizeBytes, ProbeInfo? media, JobSettings settings)
    {
        string? resizeWhyNot = null;
        if (media is { Width: > 0, Height: > 0 } && Math.Max(media.Width, media.Height) <= ResizeLongSide)
            resizeWhyNot = $"Уже не больше {ResizeLongSide} px ({media.Width}×{media.Height})";

        return
        [
            DialAction.Group(ActionIds.ImageConvert, "Формат", Formats(path, ImageFormats, ActionIds.ImageTo)),
            Action(ActionIds.Resize1920, $"Уменьшить до {ResizeLongSide} px", resizeWhyNot),
            Action(ActionIds.RotateRight, "Повернуть вправо", null),
            Action(ActionIds.RotateLeft, "Повернуть влево", null),
            Action(ActionIds.Mirror, "Отразить", null),
            Action(ActionIds.ImageCompress1Mb, $"Сжать до {JobSettings.TargetLabel(settings.ImageTargetBytes)}",
                sizeBytes <= settings.ImageTargetBytes ? CompressionCheck.AlreadySmallFor(settings.ImageTargetBytes) : null),
            Stub(ActionIds.RemoveBackground, "Убрать фон"),
            Stub(ActionIds.ImageToPdf, "В PDF"),
        ];
    }

    private static IReadOnlyList<DialAction> ForAudio(string path, ProbeInfo? media) =>
    [
        DialAction.Group(ActionIds.AudioConvert, "Формат", Formats(path, AudioFormats, ActionIds.AudioTo)),
        Action(ActionIds.Normalize, "Выровнять громкость", null),
        Action(ActionIds.Denoise, "Убрать шум", null),
        Action(ActionIds.SpeedUp, "Ускорить ×1,5", null),
        Action(ActionIds.SlowDown, "Замедлить ×0,75", null),
        Action(ActionIds.ToMono, "В моно", media is { AudioChannels: 1 } ? "Звук уже моно" : null),
        Action(ActionIds.AudioTrim, "Обрезать", null),
        Stub(ActionIds.SpeechToText, "В текст"),
    ];

    /// <summary>Вложенные секторы форматов; свой же формат неактивен.</summary>
    private static IReadOnlyList<DialAction> Formats(string path, IReadOnlyList<(string Label, string Extension)> formats,
        Func<string, string> id)
    {
        var own = NormalizeExtension(Path.GetExtension(path));
        return formats
            .Select(f => new DialAction(id(f.Extension), f.Label, own != f.Extension,
                own == f.Extension ? $"Файл уже в {f.Label}" : null) { Caption = $"В {f.Label}" })
            .ToList();
    }

    /// <summary>Синонимы расширений: .jpeg = .jpg, .tif = .tiff.</summary>
    public static string NormalizeExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpeg" => ".jpg",
        ".tif" => ".tiff",
        var e => e,
    };

    private static DialAction Action(string id, string label, string? whyNot) => new(id, label, whyNot is null, whyNot);

    private static DialAction Stub(string id, string label) => new(id, label, false, NotInPrototype);
}
