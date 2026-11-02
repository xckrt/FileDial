using FileDial.Core.Compression;
using FileDial.Core.Text;

namespace FileDial.Core.Jobs;

/// <summary>
/// «Сжать до 1 МБ»: JPG с наилучшим качеством, которое укладывается в цель. Сначала подбираем качество
/// двоичным поиском; если даже худшее не помещается — уменьшаем картинку и пробуем снова.
/// </summary>
public sealed class ImageCompressJob(FfmpegTools tools, long targetBytes = ImageCompressJob.DefaultTargetBytes,
    string? outputDirectory = null) : IFileJob
{
    public const long DefaultTargetBytes = 1_000_000;


    /// <summary>Шкала качества mjpeg: 2 — лучшее, 31 — худшее.</summary>
    public const int BestQuality = 2;
    public const int WorstQuality = 31;

    /// <summary>Шаг уменьшения, если в цель не помещается даже худшее качество.</summary>
    public const double ScaleStep = 0.75;

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var sourceSize = new FileInfo(sourcePath).Length;
        if (sourceSize <= targetBytes)
            return new JobResult(JobStatus.NothingToDo,
                $"Файл уже меньше {SizeFormatter.Format(targetBytes)} ({SizeFormatter.Format(sourceSize)}), сжимать не нужно.");

        progress?.Report(new JobProgress("Анализ файла", null));
        var info = await ProbeInfo.RunAsync(tools, sourcePath, ct).ConfigureAwait(false);
        if (!info.HasVideo)
            throw new CompressionException("Не удалось прочитать изображение.");

        var directory = outputDirectory ?? Path.GetDirectoryName(sourcePath)!;
        if (OutputFolderCheck.WhyNot(directory, targetBytes) is { } folderProblem)
            throw new CompressionException(folderProblem);

        var tempOutput = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            var longSide = Math.Max(info.Width, info.Height);
            for (var scale = 1.0; ; scale *= ScaleStep)
            {
                int? target = scale < 1 && longSide > 0 ? EvenSide(longSide * scale) : null;
                if (target is < 64)
                    throw new CompressionException($"Не удалось уложить картинку в {SizeFormatter.Format(targetBytes)}.");

                progress?.Report(new JobProgress(target is null ? "Подбор качества" : $"Уменьшение до {target} px", null));
                var quality = await FindQualityAsync(sourcePath, info, target, tempOutput, ct).ConfigureAwait(false);
                if (quality is null)
                {
                    if (longSide <= 0)
                        throw new CompressionException($"Не удалось уложить картинку в {SizeFormatter.Format(targetBytes)}.");
                    continue;
                }

                // Последний прогон поиска мог быть не на найденном качестве — кодируем окончательно.
                await EncodeAsync(sourcePath, info, target, quality.Value, tempOutput, ct).ConfigureAwait(false);
                break;
            }

            var finalPath = OutputFiles.MoveToFreeName(tempOutput,
                () => OutputNameGenerator.Generate(sourcePath, Settings.JobSettings.TargetSuffix(targetBytes), ".jpg",
                    OutputNameGenerator.PathTaken, directory));
            progress?.Report(new JobProgress("Готово", 1));
            return new JobResult(JobStatus.Done, FileJobs.DoneMessage(sourceSize, finalPath), finalPath);
        }
        finally
        {
            OutputFiles.TryDeleteFile(tempOutput);
        }
    }

    /// <summary>Наилучшее качество (наименьшее число), при котором файл помещается в цель, или null.</summary>
    private async Task<int?> FindQualityAsync(string source, ProbeInfo info, int? longSide, string output,
        CancellationToken ct)
    {
        if (await EncodeAsync(source, info, longSide, WorstQuality, output, ct).ConfigureAwait(false) > targetBytes)
            return null;

        return await QualitySearch.SmallestFittingAsync(BestQuality, WorstQuality,
            async q => await EncodeAsync(source, info, longSide, q, output, ct).ConfigureAwait(false) <= targetBytes)
            .ConfigureAwait(false);
    }

    private async Task<long> EncodeAsync(string source, ProbeInfo info, int? longSide, int quality, string output,
        CancellationToken ct)
    {
        var resize = longSide is { } side ? ConversionSpecs.ResizeFilter(side) : null;
        var flatten = ConversionSpecs.FlattenOnWhite(info, resize);
        IReadOnlyList<string> map = flatten.Contains("-filter_complex") ? [] : ["-map", "0:v:0"];
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", source };
        args.AddRange([.. map, "-frames:v", "1", "-map_metadata", "-1", .. flatten, .. ConversionSpecs.ImageEncoder(".jpg", quality), output]);

        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, null, captureOutput: false, ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output))
            throw new CompressionException($"ffmpeg не смог обработать файл (код {result.ExitCode}).", result.ErrorTail);
        return new FileInfo(output).Length;
    }

    private static int EvenSide(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);
}

public static class QualitySearch
{
    /// <summary>
    /// Двоичный поиск наименьшего значения в [low, high], для которого fits = true, при условии, что
    /// fits(high) = true и fits монотонна (чем больше число, тем меньше файл).
    /// </summary>
    public static async Task<int> SmallestFittingAsync(int low, int high, Func<int, Task<bool>> fits)
    {
        while (low < high)
        {
            var mid = (low + high) / 2;
            if (await fits(mid).ConfigureAwait(false))
                high = mid;
            else
                low = mid + 1;
        }
        return high;
    }
}
