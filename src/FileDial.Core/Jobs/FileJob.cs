using FileDial.Core.Compression;
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

/// <summary>Действие над файлом, создающее новый файл рядом с исходником. Исходник только читается.</summary>
public interface IFileJob
{
    Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken cancellationToken);
}

public static class FileJobs
{
    /// <summary>Задача для действия или null, если действие не реализовано.</summary>
    public static IFileJob? Create(string actionId, FfmpegTools tools, JobSettings? settings = null)
    {
        settings ??= JobSettings.Default;
        var output = settings.OutputFolder;
        return actionId switch
        {
            ActionIds.Compress25 => new CompressJob(tools,
                new CompressionOptions { TargetBytes = settings.VideoTargetBytes, OutputDirectory = output }),
            ActionIds.ImageCompress1Mb => new ImageCompressJob(tools, settings.ImageTargetBytes, output),
            ActionIds.ImageToPdf => new ImagesToPdfJob(tools, output),
            ActionIds.Subtitles or ActionIds.SpeechToText =>
                Speech.For(actionId, settings) is { } speech ? new ConversionJob(tools, speech, output) : null,
            _ => ConversionSpecs.For(actionId) is { } spec ? new ConversionJob(tools, spec, output) : null,
        };
    }

    public static bool IsImplemented(string actionId) =>
        actionId is ActionIds.Compress25 or ActionIds.ImageCompress1Mb or ActionIds.ImageToPdf
            or ActionIds.Subtitles or ActionIds.SpeechToText || NeedsTrimInput(actionId) ||
        ConversionSpecs.For(actionId) is not null;

    /// <summary>Перед запуском нужно спросить у пользователя границы отрезка.</summary>
    public static bool NeedsTrimInput(string actionId) => actionId is ActionIds.Trim or ActionIds.AudioTrim;

    public static IFileJob CreateTrim(FfmpegTools tools, string sourcePath, TrimRequest request,
        JobSettings? settings = null) =>
        new ConversionJob(tools, TrimSpecs.For(sourcePath, request), settings?.OutputFolder);

    internal static string DoneMessage(long sourceSize, string outputPath) =>
        $"{SizeFormatter.Format(sourceSize)} → {SizeFormatter.Format(new FileInfo(outputPath).Length)}. " +
        $"Сохранено как {Path.GetFileName(outputPath)}.";
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
                $"Файл уже меньше {SizeFormatter.Format(options?.TargetBytes ?? BitrateCalculator.DefaultTargetBytes)} " +
                $"({SizeFormatter.Format(result.SourceSize)}), сжимать не нужно.");

        var target = options?.TargetBytes ?? BitrateCalculator.DefaultTargetBytes;
        var over = result.OutputSize > target ? " Чуть больше цели: кодер не попал точно." : string.Empty;
        return new JobResult(JobStatus.Done, FileJobs.DoneMessage(result.SourceSize, result.OutputPath!) + over,
            result.OutputPath);
    }

    private sealed class SyncRelay(Action<CompressionProgress> report) : IProgress<CompressionProgress>
    {
        public void Report(CompressionProgress value) => report(value);
    }
}
