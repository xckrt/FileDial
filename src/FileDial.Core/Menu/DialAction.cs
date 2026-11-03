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
    public const string Resize1920 = "image-resize-1920";
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
    public const string ArchiveToPrefix = "archive-to:";
    public static string ArchiveTo(string extension) => ArchiveToPrefix + extension;

    /// <summary>Открыть настройки распознавания речи (скачать модель).</summary>
    public const string SetupSpeech = "setup-speech";

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
