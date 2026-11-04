using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Documents;
using FileDial.Core.Localization;
using FileDial.Core.Menu;
using FileDial.Core.Settings;
using FileDial.Core.Text;

namespace FileDial.Core.Jobs;

/// <summary>Ход работы. Fraction = null — длительность неизвестна (короткая операция без процентов).</summary>
public sealed record JobProgress(string Stage, double? Fraction);

public enum JobStatus
{
    Done,
    /// <summary>Делать нечего (например, файл уже меньше цели) — новых файлов нет.</summary>
    NothingToDo,
}

public sealed record JobResult(JobStatus Status, string Message, string? OutputPath = null);

/// <summary>Действие над несколькими файлами сразу, результат — один файл (PDF из картинок, архив из файлов).</summary>
public interface IMultiFileJob
{
    Task<JobResult> RunAsync(IReadOnlyList<string> paths, IProgress<JobProgress>? progress, CancellationToken ct);
}

/// <summary>Действие над файлом, создающее новый файл рядом с исходником. Исходник только читается.</summary>
public interface IFileJob
{
    Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken cancellationToken);
}

public static class FileJobs
{
    /// <summary>Задача для действия или null, если действие не реализовано.</summary>
    /// <param name="tools">ffmpeg; для архивов не нужен (может быть null).</param>
    public static IFileJob? Create(string actionId, FfmpegTools? tools, JobSettings? settings = null)
    {
        settings ??= JobSettings.Default;
        var output = settings.OutputFolder;

        // Текст, SVG и значки (SVG и ICO из картинки — через ffmpeg).
        if (TextFiles.TextFileJobs.IsOwn(actionId))
            return TextFiles.TextFileJobs.Create(actionId, tools, output);

        // Архивы — без ffmpeg.
        switch (actionId)
        {
            case ActionIds.ExtractHere: return new ArchiveExtractJob(ExtractMode.Here, output);
            case ActionIds.ExtractToFolder: return new ArchiveExtractJob(ExtractMode.ToFolder, output);
            case ActionIds.TestArchive: return new ArchiveTestJob();
            case ActionIds.CompressToZip: return new ArchiveCreateJob(ArchiveFormat.Zip, output);
            case ActionIds.JoinParts: return new FileJoinJob(output);

            // PDF и документы — PDFsharp, Office или LibreOffice, без ffmpeg.
            case ActionIds.PdfSplit: return new PdfSplitJob(output);
            case ActionIds.PdfRotateRight: return new PdfRotateJob(90, output);
            case ActionIds.PdfRotateLeft: return new PdfRotateJob(-90, output);
            case ActionIds.PdfMerge: return new PdfMergeJob(output);
            case ActionIds.DocToPdf: return new DocumentConvertJob(".pdf", output);
            case ActionIds.DocText: return new DocumentTextJob(output);
            case ActionIds.DocImages: return new DocumentImagesJob(output);
            case ActionIds.DocStrip: return new DocumentStripJob(output);
            case ActionIds.DocsToPdf: return new DocumentsToPdfJob(output);
        }
        if (ActionIds.TargetExtension(actionId, ActionIds.DocToPrefix) is { } documentTarget)
            return new DocumentConvertJob(documentTarget, output);
        if (ActionIds.TargetExtension(actionId, ActionIds.ArchiveToPrefix) is { } archiveTarget)
            return archiveTarget switch
            {
                ".zip" => new ArchiveRepackJob(ArchiveFormat.Zip, output),
                ".7z" => new ArchiveRepackJob(ArchiveFormat.SevenZip, output),
                _ => null,
            };

        if (tools is null)
            return null;
        return actionId switch
        {
            ActionIds.Compress25 => new CompressJob(tools,
                new CompressionOptions { TargetBytes = settings.VideoTargetBytes, OutputDirectory = output }),
            ActionIds.ImageCompress1Mb => new ImageCompressJob(tools, settings.ImageTargetBytes, output),
            ActionIds.ImageToPdf => new ImagesToPdfJob(tools, output),
            ActionIds.DocCompress => new DocumentCompressJob(tools, output),
            ActionIds.RemoveBackground =>
                new RemoveBackgroundJob(tools, settings.BackgroundModel ?? BackgroundModels.FindDefault(), output),
            ActionIds.Subtitles or ActionIds.SpeechToText =>
                Speech.For(actionId, settings) is { } speech ? new ConversionJob(tools, speech, output) : null,
            _ => ConversionSpecs.For(actionId) is { } spec ? new ConversionJob(tools, spec, output) : null,
        };
    }

    /// <summary>Нужен ли ffmpeg для действия (архивы, PDF и документы обходятся без него — кроме сжатия картинок).</summary>
    public static bool NeedsFfmpeg(string actionId) =>
        actionId is not (ActionIds.ExtractHere or ActionIds.ExtractToFolder or ActionIds.ExtractTo
            or ActionIds.TestArchive or ActionIds.CompressToZip or ActionIds.ArchivePassword
            or ActionIds.ArchiveSplit or ActionIds.JoinParts
            or ActionIds.PdfMerge or ActionIds.PdfSplit or ActionIds.PdfExtract or ActionIds.PdfRotateRight
            or ActionIds.PdfRotateLeft or ActionIds.PdfToImages or ActionIds.PdfOcr or ActionIds.PdfProtect
            or ActionIds.DocToPdf or ActionIds.DocText or ActionIds.DocImages or ActionIds.DocStrip
            or ActionIds.DocProtect or ActionIds.DocsToPdf)
        && ActionIds.TargetExtension(actionId, ActionIds.ArchiveToPrefix) is null
        && ActionIds.TargetExtension(actionId, ActionIds.DocToPrefix) is null
        && !TextFiles.TextFileJobs.RunsWithoutFfmpeg(actionId);

    /// <summary>Перед запуском нужно спросить, какие страницы взять («Извлечь страницы»).</summary>
    public static bool NeedsPageRange(string actionId) => actionId == ActionIds.PdfExtract;

    /// <summary>Перед запуском нужно спросить размер части («Разбить на части»).</summary>
    public static bool NeedsPartSize(string actionId) => actionId == ActionIds.ArchiveSplit;

    /// <summary>Перед запуском нужно спросить, до какого размера уменьшить («Уменьшить…»).</summary>
    public static bool NeedsResizeInput(string actionId) => actionId == ActionIds.ResizeImage;

    /// <summary>Перед запуском нужно спросить новый пароль («Защитить паролем»).</summary>
    public static bool NeedsNewPassword(string actionId) =>
        actionId is ActionIds.ArchivePassword or ActionIds.PdfProtect or ActionIds.DocProtect;

    /// <summary>Перед запуском нужно спросить папку назначения.</summary>
    public static bool NeedsFolderInput(string actionId) => actionId == ActionIds.ExtractTo;

    public static bool IsImplemented(string actionId) =>
        !NeedsFfmpeg(actionId) || actionId is ActionIds.Compress25 or ActionIds.ImageCompress1Mb or ActionIds.ImageToPdf
            or ActionIds.DocCompress or ActionIds.RemoveBackground
            or ActionIds.Subtitles or ActionIds.SpeechToText || NeedsTrimInput(actionId) || NeedsResizeInput(actionId) || TextFiles.TextFileJobs.IsOwn(actionId) ||
        ConversionSpecs.For(actionId) is not null;

    /// <summary>Перед запуском нужно спросить у пользователя границы отрезка.</summary>
    public static bool NeedsTrimInput(string actionId) => actionId is ActionIds.Trim or ActionIds.AudioTrim;

    public static IFileJob CreateTrim(FfmpegTools tools, string sourcePath, TrimRequest request,
        JobSettings? settings = null) =>
        new ConversionJob(tools, TrimSpecs.For(sourcePath, request), settings?.OutputFolder);

    internal static string DoneMessage(long sourceSize, string outputPath) =>
        $"{SizeFormatter.Format(sourceSize)} → {SizeFormatter.Format(new FileInfo(outputPath).Length)}. " +
        Lang.T.JobSavedAs(Path.GetFileName(outputPath));
}

/// <summary>«Сжать до 25 МБ» поверх <see cref="VideoCompressor"/>.</summary>
public sealed class CompressJob(FfmpegTools tools, CompressionOptions? options = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var relay = progress is null ? null : new SyncRelay(p => progress.Report(new JobProgress(p.Stage, p.Fraction)));
        var result = await new VideoCompressor(tools, options).CompressAsync(sourcePath, relay, ct).ConfigureAwait(false);

        if (result.Status == CompressionStatus.AlreadySmall)
            return new JobResult(JobStatus.NothingToDo,
                Lang.T.JobAlreadySmallNoNeed(SizeFormatter.Format(options?.TargetBytes ?? BitrateCalculator.DefaultTargetBytes),
                    SizeFormatter.Format(result.SourceSize)));

        var target = options?.TargetBytes ?? BitrateCalculator.DefaultTargetBytes;
        var over = result.OutputSize > target ? " " + Lang.T.JobSlightlyOverTarget : string.Empty;
        return new JobResult(JobStatus.Done, FileJobs.DoneMessage(result.SourceSize, result.OutputPath!) + over,
            result.OutputPath);
    }

    private sealed class SyncRelay(Action<CompressionProgress> report) : IProgress<CompressionProgress>
    {
        public void Report(CompressionProgress value) => report(value);
    }
}
