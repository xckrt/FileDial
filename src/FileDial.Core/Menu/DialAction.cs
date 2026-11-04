namespace FileDial.Core.Menu;

/// <summary>
/// Один сектор круга. Для неактивного сектора <see cref="DisabledReason"/> объясняет почему.
/// Если есть <see cref="Children"/>, это группа: при наведении снаружи круга раскрываются вложенные секторы.
/// </summary>
public sealed record DialAction(string Id, string Label, bool IsEnabled, string? DisabledReason = null)
{
    public IReadOnlyList<DialAction>? Children { get; init; }

    /// <summary>Заголовок окна действия; по умолчанию — подпись сектора.</summary>
    public string? Caption { get; init; }

    public bool IsGroup => Children is { Count: > 0 };

    /// <summary>Для неактивного сектора: действие, которое устраняет причину (например, открыть загрузку модели).</summary>
    public string? FixActionId { get; init; }

    public string Title => Caption ?? Label;

    /// <summary>Группа активна, если активен хотя бы один вложенный сектор.</summary>
    public static DialAction Group(string id, string label, IReadOnlyList<DialAction> children) =>
        new(id, label, children.Any(c => c.IsEnabled),
            children.Any(c => c.IsEnabled) ? null : children.FirstOrDefault()?.DisabledReason)
        {
            Children = children,
        };
}

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
    /// <summary>Группа «Формат» у видео.</summary>
    public const string Convert = "convert";

    // Изображения
    public const string ImageConvert = "image-convert";
    /// <summary>«Уменьшить…»: размер выбирается в окне перед запуском.</summary>
    public const string ResizeImage = "image-resize";
    public const string RotateRight = "image-rotate-right";
    public const string RotateLeft = "image-rotate-left";
    public const string Mirror = "image-mirror";
    public const string ImageCompress1Mb = "image-compress-1mb";
    public const string RemoveBackground = "image-remove-background";
    public const string ImageToPdf = "image-to-pdf";

    // Звук
    public const string AudioConvert = "audio-convert";
    public const string Normalize = "audio-normalize";
    public const string Denoise = "audio-denoise";
    public const string SpeedUp = "audio-speed-up";
    public const string SlowDown = "audio-slow-down";
    public const string ToMono = "audio-to-mono";
    public const string AudioTrim = "audio-trim";
    public const string SpeechToText = "audio-to-text";

    // Архивы
    public const string ExtractHere = "archive-extract-here";
    public const string ExtractToFolder = "archive-extract-folder";
    public const string ExtractTo = "archive-extract-to";
    public const string TestArchive = "archive-test";
    public const string ArchiveConvert = "archive-convert";
    public const string ArchivePassword = "archive-password";
    public const string ArchiveSplit = "archive-split";
    public const string ArchiveContents = "archive-contents";
    /// <summary>Сжать выбранные файлы и папки в ZIP (для всего, у чего нет своего круга).</summary>
    public const string CompressToZip = "compress-to-zip";
    /// <summary>Собрать файл из частей .001, .002, … (компактный круг для частей).</summary>
    public const string JoinParts = "join-parts";
    public const string ArchiveToPrefix = "archive-to:";
    public static string ArchiveTo(string extension) => ArchiveToPrefix + extension;

    // PDF
    public const string PdfMerge = "pdf-merge";
    public const string PdfSplit = "pdf-split";
    public const string PdfExtract = "pdf-extract";
    public const string PdfRotateRight = "pdf-rotate-right";
    public const string PdfRotateLeft = "pdf-rotate-left";
    public const string PdfToImages = "pdf-to-images";
    public const string PdfOcr = "pdf-ocr";
    public const string PdfProtect = "pdf-password";

    // Документы
    public const string DocToPdf = "doc-to-pdf";
    /// <summary>Группа «Формат» у документа.</summary>
    public const string DocConvert = "doc-convert";
    public const string DocText = "doc-text";
    public const string DocImages = "doc-images";
    public const string DocCompress = "doc-compress-images";
    public const string DocStrip = "doc-strip-personal";
    public const string DocProtect = "doc-password";
    public const string DocsToPdf = "doc-merge-pdf";
    public const string DocToPrefix = "doc-to:";
    public static string DocTo(string extension) => DocToPrefix + extension;

    // Текст и данные
    public const string TextToUtf8 = "text-to-utf8";
    /// <summary>Группа «Переводы строк».</summary>
    public const string TextLineEndings = "text-line-endings";
    public const string TextToCrlf = "text-eol:crlf";
    public const string TextToLf = "text-eol:lf";
    public const string CsvToXlsx = "text-csv-to-xlsx";
    public const string JsonFormat = "text-json-format";
    public const string JsonMinify = "text-json-minify";
    public const string MarkdownToHtml = "text-md-to-html";
    public const string MarkdownToPdf = "text-md-to-pdf";

    // SVG и значки
    public const string SvgToPng = "svg-to-png";
    public const string SvgPngSizes = "svg-png-sizes";
    public const string SvgToIco = "svg-to-ico";
    public const string IcoToPng = "ico-to-png";
    public const string IcoAllPng = "ico-all-png";
    /// <summary>«ICO» в группе «Формат» у картинок.</summary>
    public static readonly string ImageToIco = ImageTo(".ico");

    /// <summary>Открыть настройки распознавания речи (скачать модель).</summary>
    public const string SetupSpeech = "setup-speech";

    /// <summary>Открыть настройки удаления фона (скачать модель).</summary>
    public const string SetupBackground = "setup-background";

    // Конвертация: id вида «image-to:.png». Расширение после двоеточия — целевой формат.
    public const string ImageToPrefix = "image-to:";
    public const string AudioToPrefix = "audio-to:";
    public const string VideoToPrefix = "video-to:";

    public static string ImageTo(string extension) => ImageToPrefix + extension;
    public static string AudioTo(string extension) => AudioToPrefix + extension;
    public static string VideoTo(string extension) => VideoToPrefix + extension;

    public static readonly string ToJpg = ImageTo(".jpg");
    public static readonly string ToPng = ImageTo(".png");
    public static readonly string ToWebp = ImageTo(".webp");
    public static readonly string ToMp3 = AudioTo(".mp3");
    public static readonly string ToM4a = AudioTo(".m4a");
    public static readonly string ToWav = AudioTo(".wav");
    public static readonly string ToMp4 = VideoTo(".mp4");

    /// <summary>Целевое расширение из id конвертации или null.</summary>
    public static string? TargetExtension(string actionId, string prefix) =>
        actionId.StartsWith(prefix, StringComparison.Ordinal) ? actionId[prefix.Length..] : null;
}
