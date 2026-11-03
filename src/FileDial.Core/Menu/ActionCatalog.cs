using FileDial.Core.Compression;
using FileDial.Core.Documents;
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
    /// <param name="pages">Число страниц PDF, если известно.</param>
    public static IReadOnlyList<DialAction> ForFile(string path, long sizeBytes, ProbeInfo? media = null,
        JobSettings? settings = null, int? pages = null) =>
        FileKinds.Of(path) switch
        {
            FileKind.Video => ForVideo(path, sizeBytes, media, settings ?? JobSettings.Default),
            FileKind.Image => ForImage(path, sizeBytes, media, settings ?? JobSettings.Default),
            FileKind.Audio => ForAudio(path, media, settings ?? JobSettings.Default),
            FileKind.Archive => ForArchive(path),
            FileKind.Pdf => ForPdf(pages),
            FileKind.Document => ForDocument(path),
            _ => [],
        };

    /// <summary>
    /// Действия для нескольких файлов одного типа: те же секторы, но выполняются для каждого файла.
    /// Неактивно только то, что не нужно ни одному файлу; обрезка — только по одному файлу.
    /// </summary>
    public static IReadOnlyList<DialAction> ForBatch(FileKind kind, IReadOnlyList<string> paths, IReadOnlyList<long> sizes,
        JobSettings? settings = null)
    {
        var extensions = paths.Select(p => NormalizeExtension(Path.GetExtension(p))).ToHashSet();
        var single = ForFile(paths[0], sizes.Count > 0 ? sizes.Max() : 0, null, settings);
        return single.Select(a => a switch
        {
            { IsGroup: true } => DialAction.Group(a.Id, a.Label, a.Children!.Select(c =>
            {
                var target = c.Caption is { } caption ? TargetOf(c.Id) : null;
                var allThere = target is not null && extensions.Count == 1 && extensions.Contains(target);
                return c with
                {
                    IsEnabled = !allThere,
                    DisabledReason = allThere ? $"Все файлы уже в {c.Label}" : null,
                };
            }).ToList()),
            { Id: ActionIds.Trim or ActionIds.AudioTrim } => a with { IsEnabled = false, DisabledReason = "Обрезка — по одному файлу" },
            { Id: ActionIds.ArchiveContents } => a with { IsEnabled = false, DisabledReason = "Содержимое — по одному архиву" },
            { Id: ActionIds.PdfExtract } => a with { IsEnabled = false, DisabledReason = "Страницы — по одному файлу" },
            { Id: ActionIds.PdfMerge } => a with { IsEnabled = true, DisabledReason = null },
            { Id: ActionIds.DocsToPdf } => DocumentsToPdf(paths),
            { Id: ActionIds.ImageToPdf } => a with { Label = "Собрать в PDF", Caption = "Собрать в PDF" },
            { IsEnabled: false, DisabledReason: { } reason } when reason.StartsWith("Файл уже меньше", StringComparison.Ordinal) =>
                a with { DisabledReason = "Все файлы" + reason["Файл".Length..] },
            _ => a,
        }).ToList();
    }

    private static string? TargetOf(string actionId) =>
        ActionIds.TargetExtension(actionId, ActionIds.ImageToPrefix)
        ?? ActionIds.TargetExtension(actionId, ActionIds.AudioToPrefix)
        ?? ActionIds.TargetExtension(actionId, ActionIds.VideoToPrefix)
        ?? ActionIds.TargetExtension(actionId, ActionIds.ArchiveToPrefix)
        ?? ActionIds.TargetExtension(actionId, ActionIds.DocToPrefix);

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
            SpeechAction(ActionIds.Subtitles, "Субтитры", settings, noSound ? "В видео нет звука" : null),
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
            Action(ActionIds.ImageToPdf, "В PDF", null),
        ];
    }

    private static IReadOnlyList<DialAction> ForAudio(string path, ProbeInfo? media, JobSettings settings) =>
    [
        DialAction.Group(ActionIds.AudioConvert, "Формат", Formats(path, AudioFormats, ActionIds.AudioTo)),
        Action(ActionIds.Normalize, "Выровнять громкость", null),
        Action(ActionIds.Denoise, "Убрать шум", null),
        Action(ActionIds.SpeedUp, "Ускорить ×1,5", null),
        Action(ActionIds.SlowDown, "Замедлить ×0,75", null),
        Action(ActionIds.ToMono, "В моно", media is { AudioChannels: 1 } ? "Звук уже моно" : null),
        Action(ActionIds.AudioTrim, "Обрезать", null),
        SpeechAction(ActionIds.SpeechToText, "В текст", settings, null),
    ];

    public static readonly IReadOnlyList<(string Label, string Extension)> ArchiveFormats = [("ZIP", ".zip"), ("7z", ".7z")];

    private static IReadOnlyList<DialAction> ForArchive(string path)
    {
        // zip распаковывает .NET; остальное — встроенный в Windows tar.exe (есть с Windows 10 1803).
        string? noTar = !Archives.ArchiveSupport.IsZip(path) && !Archives.ArchiveSupport.TarAvailable
            ? "Нужен tar.exe из Windows 10 1803 или новее" : null;
        var formats = Formats(path, ArchiveFormats, ActionIds.ArchiveTo)
            .Select(c => c.Id == ActionIds.ArchiveTo(".7z") && !Archives.ArchiveSupport.TarAvailable
                ? c with { IsEnabled = false, DisabledReason = "Нужен tar.exe из Windows 10 1803 или новее" }
                : c)
            .ToList();
        return
        [
            Action(ActionIds.ExtractHere, "Распаковать сюда", noTar),
            Action(ActionIds.ExtractToFolder, "Распаковать в папку", noTar),
            Action(ActionIds.ExtractTo, "Распаковать в…", noTar),
            Action(ActionIds.TestArchive, "Проверить архив", noTar),
            DialAction.Group(ActionIds.ArchiveConvert, "Формат", noTar is null ? formats
                : formats.Select(c => c with { IsEnabled = false, DisabledReason = noTar }).ToList()),
            Action(ActionIds.ArchivePassword, "Защитить паролем", noTar),
            // Деление — по байтам, tar.exe не нужен.
            Action(ActionIds.ArchiveSplit, "Разбить на части", null),
            Action(ActionIds.ArchiveContents, "Содержимое", noTar),
        ];
    }

    private static IReadOnlyList<DialAction> ForPdf(int? pages)
    {
        var single = pages == 1 ? "В документе одна страница" : null;
        return
        [
            Action(ActionIds.PdfSplit, "Разделить по страницам", single),
            Action(ActionIds.PdfExtract, "Извлечь страницы", single) with { Caption = "Извлечь страницы" },
            Action(ActionIds.PdfRotateRight, "Повернуть вправо", null),
            Action(ActionIds.PdfRotateLeft, "Повернуть влево", null),
            Action(ActionIds.PdfToImages, "Страницы в картинки", null),
            Action(ActionIds.PdfOcr, "Распознать текст", null),
            Action(ActionIds.PdfProtect, "Защитить паролем", null),
            Action(ActionIds.PdfMerge, "Объединить", "Выделите несколько PDF") with { Caption = "Объединить PDF" },
        ];
    }

    /// <summary>Форматы для «Формат» у документа — по его виду.</summary>
    public static IReadOnlyList<(string Label, string Extension)> DocumentFormats(DocumentFamily family) => family switch
    {
        DocumentFamily.Sheet => [("XLSX", ".xlsx"), ("ODS", ".ods"), ("CSV", ".csv")],
        DocumentFamily.Slides => [("PPTX", ".pptx"), ("ODP", ".odp")],
        _ => [("DOCX", ".docx"), ("ODT", ".odt"), ("RTF", ".rtf")],
    };

    private static IReadOnlyList<DialAction> ForDocument(string path)
    {
        var family = OfficeDocs.FamilyOf(path);
        string? Needs(string extension) => DocumentConverters.For(family, extension) is null ? DocumentConverters.NoConverter : null;
        var package = OfficeDocs.IsPackage(path) ? null : OfficeDocs.PackageOnly;

        var formats = Formats(path, DocumentFormats(family), ActionIds.DocTo)
            .Select(c => c.IsEnabled && Needs(TargetOf(c.Id)!) is { } reason ? c with { IsEnabled = false, DisabledReason = reason } : c)
            .ToList();
        var text = family == DocumentFamily.Sheet ? "Для таблиц — «Формат» → CSV"
            : OfficeDocs.IsPackage(path) ? null
            : Needs(family == DocumentFamily.Slides ? ".pptx" : ".docx");
        var password = !OfficeDocs.IsMicrosoftFormat(path) ? "Пароль — для форматов Microsoft Office"
            : DocumentConverters.OfficeFor(family) is null ? DocumentConverters.NoOffice
            : null;

        return
        [
            Action(ActionIds.DocToPdf, "В PDF", Needs(".pdf")),
            DialAction.Group(ActionIds.DocConvert, "Формат", formats),
            Action(ActionIds.DocText, "В текст", text),
            Action(ActionIds.DocImages, "Извлечь картинки", package),
            Action(ActionIds.DocCompress, "Сжать картинки", package),
            Action(ActionIds.DocStrip, "Убрать личные данные", package),
            Action(ActionIds.DocProtect, "Защитить паролем", password),
            Action(ActionIds.DocsToPdf, "Собрать в PDF", "Выделите несколько документов"),
        ];
    }

    private static DialAction DocumentsToPdf(IReadOnlyList<string> paths)
    {
        var missing = paths.Select(OfficeDocs.FamilyOf).Distinct()
            .Any(family => DocumentConverters.For(family, ".pdf") is null);
        return new DialAction(ActionIds.DocsToPdf, "Собрать в PDF", !missing, missing ? DocumentConverters.NoConverter : null)
        {
            Caption = "Собрать в PDF",
        };
    }

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

    /// <summary>Распознавание речи: без модели сектор неактивен, но выбор ведёт к её загрузке.</summary>
    private static DialAction SpeechAction(string id, string label, JobSettings settings, string? otherReason)
    {
        var action = Action(id, label, Speech.WhyNot(settings) ?? otherReason);
        return Speech.NeedsModel(settings) ? action with { FixActionId = ActionIds.SetupSpeech } : action;
    }

    private static DialAction Stub(string id, string label) => new(id, label, false, NotInPrototype);
}
