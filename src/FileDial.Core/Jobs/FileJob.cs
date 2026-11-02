using FileDial.Core.Compression;
using FileDial.Core.Menu;
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
    public static IFileJob? Create(string actionId, FfmpegTools tools) =>
        actionId == ActionIds.Compress25
            ? new CompressJob(tools)
            : ConversionSpecs.For(actionId) is { } spec ? new ConversionJob(tools, spec) : null;

    public static bool IsImplemented(string actionId) =>
        actionId == ActionIds.Compress25 || ConversionSpecs.For(actionId) is not null;

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
                $"Файл уже меньше 25 МБ ({SizeFormatter.Format(result.SourceSize)}), сжимать не нужно.");

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
