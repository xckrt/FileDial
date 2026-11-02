using FileDial.Core.Compression;
using FileDial.Core.Text;

namespace FileDial.Core.Jobs;

/// <summary>
/// Картинки → один PDF, по картинке на страницу, в порядке выделения. Каждая картинка приводится
/// к JPEG через ffmpeg (прозрачность — на белом фоне), затем всё собирается <see cref="PdfWriter"/>.
/// </summary>
public sealed class ImagesToPdfJob(FfmpegTools tools, string? outputDirectory = null) : IFileJob
{
    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct) =>
        RunAsync([sourcePath], progress, ct);

    public async Task<JobResult> RunAsync(IReadOnlyList<string> paths, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (paths.Count == 0)
            throw new ArgumentException("Нет картинок.", nameof(paths));

        var first = paths[0];
        var directory = outputDirectory ?? Path.GetDirectoryName(first)!;
        var totalSize = paths.Sum(p => new FileInfo(p).Length);
        if (OutputFolderCheck.WhyNot(directory, totalSize * 2 + 10_000_000) is { } folderProblem)
            throw new CompressionException(folderProblem);

        var workDir = Path.Combine(Path.GetTempPath(), "FileDial", Guid.NewGuid().ToString("N"));
        var tempOutput = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(workDir);
            var pages = new List<PdfImagePage>(paths.Count);
            for (var i = 0; i < paths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new JobProgress($"Страница {i + 1} из {paths.Count} · {Path.GetFileName(paths[i])}",
                    (double)i / paths.Count));
                pages.Add(await ToJpegPageAsync(paths[i], Path.Combine(workDir, $"{i}.jpg"), ct).ConfigureAwait(false));
            }

            progress?.Report(new JobProgress("Сборка PDF", null));
            await using (var stream = new FileStream(tempOutput, FileMode.CreateNew, FileAccess.Write))
                PdfWriter.Write(stream, pages);

            var name = PdfName(paths);
            var finalPath = OutputFiles.MoveToFreeName(tempOutput,
                () => OutputNameGenerator.Generate(Path.Combine(directory, name + ".src"), "", ".pdf",
                    OutputNameGenerator.PathTaken, directory));
            progress?.Report(new JobProgress("Готово", 1));

            var what = paths.Count == 1 ? Path.GetFileName(first) : Plural.Count(paths.Count, "картинка", "картинки", "картинок");
            return new JobResult(JobStatus.Done,
                $"{what} → {SizeFormatter.Format(new FileInfo(finalPath).Length)}. Сохранено как {Path.GetFileName(finalPath)}.",
                finalPath);
        }
        finally
        {
            OutputFiles.TryDeleteFile(tempOutput);
            OutputFiles.TryDeleteDirectory(workDir);
        }
    }

    /// <summary>Имя документа: для одной картинки — её имя, для нескольких — имя папки первой картинки.</summary>
    public static string PdfName(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1)
            return Path.GetFileNameWithoutExtension(paths[0]);
        var folder = Path.GetFileName(Path.GetDirectoryName(paths[0])?.TrimEnd('\\', '/') ?? "");
        return string.IsNullOrWhiteSpace(folder) ? "Картинки" : folder;
    }

    private async Task<PdfImagePage> ToJpegPageAsync(string source, string jpeg, CancellationToken ct)
    {
        var info = await ProbeInfo.RunAsync(tools, source, ct).ConfigureAwait(false);
        if (!info.HasVideo || info.Width <= 0 || info.Height <= 0)
            throw new CompressionException($"Не удалось прочитать изображение {Path.GetFileName(source)}.");

        var flatten = ConversionSpecs.FlattenOnWhite(info, null);
        IReadOnlyList<string> map = flatten.Contains("-filter_complex") ? [] : ["-map", "0:v:0"];
        // 4:2:0 и RGB-JPEG понимают все просмотрщики PDF.
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", source };
        args.AddRange([.. map, "-frames:v", "1", "-map_metadata", "-1", .. flatten,
            "-c:v", "mjpeg", "-q:v", "3", "-pix_fmt", "yuvj420p", "-f", "image2", "-update", "1", jpeg]);

        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, null, captureOutput: false, ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(jpeg))
            throw new CompressionException($"ffmpeg не смог обработать {Path.GetFileName(source)}.", result.ErrorTail);

        return new PdfImagePage(await File.ReadAllBytesAsync(jpeg, ct).ConfigureAwait(false), info.Width, info.Height);
    }
}
