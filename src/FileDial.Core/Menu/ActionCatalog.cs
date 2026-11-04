using FileDial.Core.Compression;
using FileDial.Core.Documents;
using FileDial.Core.Localization;
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
    public static string NotInPrototype => Lang.T.NotInPrototype;
    public static string AlreadySmall => CompressionCheck.AlreadySmall;

    /// <summary>Форматы для конвертации: подпись и расширение. Порядок = порядок во внешнем кольце.</summary>
    public static readonly IReadOnlyList<(string Label, string Extension)> VideoFormats =
        [("MP4", ".mp4"), ("MKV", ".mkv"), ("WebM", ".webm"), ("MOV", ".mov")];

    public static readonly IReadOnlyList<(string Label, string Extension)> ImageFormats =
        [("JPG", ".jpg"), ("PNG", ".png"), ("WebP", ".webp"), ("BMP", ".bmp"), ("TIFF", ".tiff"), ("GIF", ".gif"), ("ICO", ".ico")];

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
            FileKind.Text => TextFileCatalog.ForText(path),
            FileKind.Svg => TextFileCatalog.ForSvg(path, (settings ?? JobSettings.Default).FfmpegFolder),
            FileKind.Icon => TextFileCatalog.ForIcon(path),
            _ => [],
        };

    /// <summary>
    /// Действия для нескольких файлов одного типа: те же секторы, но выполняются для каждого файла.
    /// Неактивно только то, что не нужно ни одному файлу; обрезка — только по одному файлу.
    /// </summary>
    public static IReadOnlyList<DialAction> ForBatch(FileKind kind, IReadOnlyList<string> paths, IReadOnlyList<long> sizes,
        JobSettings? settings = null)
    {
        if (kind is FileKind.Text or FileKind.Svg or FileKind.Icon)
            return TextFileCatalog.ForBatch(kind, paths, settings?.FfmpegFolder);
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
                    DisabledReason = allThere ? Lang.T.AllAlreadyIn(c.Label) : null,
                };
            }).ToList()),
            { Id: ActionIds.Trim or ActionIds.AudioTrim } => a with { IsEnabled = false, DisabledReason = Lang.T.TrimOneFile },
            { Id: ActionIds.ArchiveContents } => a with { IsEnabled = false, DisabledReason = Lang.T.ContentsOneArchive },
            { Id: ActionIds.PdfExtract } => a with { IsEnabled = false, DisabledReason = Lang.T.PagesOneFile },
            { Id: ActionIds.PdfMerge } => a with { IsEnabled = true, DisabledReason = null },
            { Id: ActionIds.DocsToPdf } => DocumentsToPdf(paths),
            { Id: ActionIds.ImageToPdf } => a with { Label = Lang.T.CombineIntoPdf, Caption = Lang.T.CombineIntoPdf },
            // «Сжать до…» неактивно, только если меньше цели все файлы.
            { Id: ActionIds.Compress25, IsEnabled: false } =>
                a with { DisabledReason = Lang.T.AllSmallerThan(Text.SizeFormatter.Format((settings ?? JobSettings.Default).VideoTargetBytes)) },
            { Id: ActionIds.ImageCompress1Mb, IsEnabled: false } =>
                a with { DisabledReason = Lang.T.AllSmallerThan(Text.SizeFormatter.Format((settings ?? JobSettings.Default).ImageTargetBytes)) },
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
            Action(ActionIds.Compress25, Lang.T.CompressTo(JobSettings.TargetLabel(settings.VideoTargetBytes)),
                CompressionCheck.WhyNot(sizeBytes, media, settings.VideoTargetBytes)),
            Action(ActionIds.Trim, Lang.T.Trim, null),
            Action(ActionIds.Gif, Lang.T.MakeGif, tooLongForGif ? Lang.T.GifTooLong(ConversionSpecs.GifMaxSeconds) : null),
            Action(ActionIds.ExtractAudio, Lang.T.ExtractAudio, noSound ? Lang.T.NoSoundInVideo : null),
            Action(ActionIds.Frame, Lang.T.FrameToImage, null),
            SpeechAction(ActionIds.Subtitles, Lang.T.Subtitles, settings, noSound ? Lang.T.NoSoundInVideo : null),
            Action(ActionIds.Mute, Lang.T.RemoveSound, noSound ? Lang.T.AlreadySilent : null),
            DialAction.Group(ActionIds.Convert, Lang.T.Format, Formats(path, VideoFormats, ActionIds.VideoTo)),
        ];
    }

    private static IReadOnlyList<DialAction> ForImage(string path, long sizeBytes, ProbeInfo? media, JobSettings settings)
    {
        return
        [
            DialAction.Group(ActionIds.ImageConvert, Lang.T.Format, Formats(path, ImageFormats, ActionIds.ImageTo)),
            Action(ActionIds.ResizeImage, Lang.T.RszLabel, null) with { Caption = Lang.T.RszCaption },
            Action(ActionIds.RotateRight, Lang.T.RotateRight, null),
            Action(ActionIds.RotateLeft, Lang.T.RotateLeft, null),
            Action(ActionIds.Mirror, Lang.T.Mirror, null),
            Action(ActionIds.ImageCompress1Mb, Lang.T.CompressTo(JobSettings.TargetLabel(settings.ImageTargetBytes)),
                sizeBytes <= settings.ImageTargetBytes ? CompressionCheck.AlreadySmallFor(settings.ImageTargetBytes) : null),
            BackgroundAction(settings),
            Action(ActionIds.ImageToPdf, Lang.T.ToPdf, null),
        ];
    }

    private static IReadOnlyList<DialAction> ForAudio(string path, ProbeInfo? media, JobSettings settings) =>
    [
        DialAction.Group(ActionIds.AudioConvert, Lang.T.Format, Formats(path, AudioFormats, ActionIds.AudioTo)),
        Action(ActionIds.Normalize, Lang.T.NormalizeVolume, null),
        Action(ActionIds.Denoise, Lang.T.RemoveNoise, null),
        Action(ActionIds.SpeedUp, Lang.T.SpeedUp, null),
        Action(ActionIds.SlowDown, Lang.T.SlowDown, null),
        Action(ActionIds.ToMono, Lang.T.ToMono, media is { AudioChannels: 1 } ? Lang.T.AlreadyMono : null),
        Action(ActionIds.AudioTrim, Lang.T.Trim, null),
        SpeechAction(ActionIds.SpeechToText, Lang.T.ToText, settings, null),
    ];

    public static readonly IReadOnlyList<(string Label, string Extension)> ArchiveFormats = [("ZIP", ".zip"), ("7z", ".7z")];

    private static IReadOnlyList<DialAction> ForArchive(string path)
    {
        // zip распаковывает .NET; остальное — встроенный в Windows tar.exe (есть с Windows 10 1803).
        string? noTar = !Archives.ArchiveSupport.IsZip(path) && !Archives.ArchiveSupport.TarAvailable ? Lang.T.NeedsTar : null;
        var formats = Formats(path, ArchiveFormats, ActionIds.ArchiveTo)
            .Select(c => c.Id == ActionIds.ArchiveTo(".7z") && !Archives.ArchiveSupport.TarAvailable
                ? c with { IsEnabled = false, DisabledReason = Lang.T.NeedsTar }
                : c)
            .ToList();
        return
        [
            Action(ActionIds.ExtractHere, Lang.T.ExtractHere, noTar),
            Action(ActionIds.ExtractToFolder, Lang.T.ExtractToFolder, noTar),
            Action(ActionIds.ExtractTo, Lang.T.ExtractTo, noTar),
            Action(ActionIds.TestArchive, Lang.T.TestArchive, noTar),
            DialAction.Group(ActionIds.ArchiveConvert, Lang.T.Format, noTar is null ? formats
                : formats.Select(c => c with { IsEnabled = false, DisabledReason = noTar }).ToList()),
            Action(ActionIds.ArchivePassword, Lang.T.ProtectWithPassword, noTar),
            // Деление — по байтам, tar.exe не нужен.
            Action(ActionIds.ArchiveSplit, Lang.T.SplitIntoParts, null),
            Action(ActionIds.ArchiveContents, Lang.T.Contents, noTar),
        ];
    }

    private static IReadOnlyList<DialAction> ForPdf(int? pages)
    {
        var single = pages == 1 ? Lang.T.SinglePage : null;
        return
        [
            Action(ActionIds.PdfSplit, Lang.T.SplitPages, single),
            Action(ActionIds.PdfExtract, Lang.T.ExtractPages, single),
            Action(ActionIds.PdfRotateRight, Lang.T.RotateRight, null),
            Action(ActionIds.PdfRotateLeft, Lang.T.RotateLeft, null),
            Action(ActionIds.PdfToImages, Lang.T.PagesToImages, null),
            Action(ActionIds.PdfOcr, Lang.T.RecognizeText, null),
            Action(ActionIds.PdfProtect, Lang.T.ProtectWithPassword, null),
            Action(ActionIds.PdfMerge, Lang.T.Merge, Lang.T.SelectSeveralPdfs) with { Caption = Lang.T.MergePdfs },
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
        var text = family == DocumentFamily.Sheet ? Lang.T.SheetsUseCsv
            : OfficeDocs.IsPackage(path) ? null
            : Needs(family == DocumentFamily.Slides ? ".pptx" : ".docx");
        var password = !OfficeDocs.IsMicrosoftFormat(path) ? Lang.T.PasswordOfficeOnly
            : DocumentConverters.OfficeFor(family) is null ? DocumentConverters.NoOffice
            : null;

        return
        [
            Action(ActionIds.DocToPdf, Lang.T.ToPdf, Needs(".pdf")),
            DialAction.Group(ActionIds.DocConvert, Lang.T.Format, formats),
            Action(ActionIds.DocText, Lang.T.ToText, text),
            Action(ActionIds.DocImages, Lang.T.ExtractImages, package),
            Action(ActionIds.DocCompress, Lang.T.CompressImages, package),
            Action(ActionIds.DocStrip, Lang.T.RemovePersonalData, package),
            Action(ActionIds.DocProtect, Lang.T.ProtectWithPassword, password),
            Action(ActionIds.DocsToPdf, Lang.T.CombineIntoPdf, Lang.T.SelectSeveralDocuments),
        ];
    }

    private static DialAction DocumentsToPdf(IReadOnlyList<string> paths)
    {
        var missing = paths.Select(OfficeDocs.FamilyOf).Distinct()
            .Any(family => DocumentConverters.For(family, ".pdf") is null);
        return new DialAction(ActionIds.DocsToPdf, Lang.T.CombineIntoPdf, !missing, missing ? DocumentConverters.NoConverter : null)
        {
            Caption = Lang.T.CombineIntoPdf,
        };
    }

    /// <summary>Вложенные секторы форматов; свой же формат неактивен.</summary>
    private static IReadOnlyList<DialAction> Formats(string path, IReadOnlyList<(string Label, string Extension)> formats,
        Func<string, string> id)
    {
        var own = NormalizeExtension(Path.GetExtension(path));
        return formats
            .Select(f => new DialAction(id(f.Extension), f.Label, own != f.Extension,
                own == f.Extension ? Lang.T.AlreadyIn(f.Label) : null) { Caption = Lang.T.ConvertTo(f.Label) })
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

    /// <summary>«Убрать фон»: без модели сектор неактивен, но выбор ведёт к её загрузке.</summary>
    private static DialAction BackgroundAction(JobSettings settings) =>
        settings.BackgroundModel is { } model && File.Exists(model)
            ? Action(ActionIds.RemoveBackground, Lang.T.RemoveBackground, null)
            : Action(ActionIds.RemoveBackground, Lang.T.RemoveBackground, BackgroundModels.NeedsModel)
                with { FixActionId = ActionIds.SetupBackground };
}
