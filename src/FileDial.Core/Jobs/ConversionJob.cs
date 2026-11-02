using FileDial.Core.Compression;

namespace FileDial.Core.Jobs;

public enum MediaRequirement
{
    Image,
    Audio,
}

/// <summary>Описание однопроходного преобразования через ffmpeg.</summary>
public sealed record ConversionSpec(
    /// <summary>Добавка к имени результата (пустая при простой смене формата).</summary>
    string Suffix,
    MediaRequirement Requires,
    /// <summary>Расширение результата по пути исходника.</summary>
    Func<string, string> OutputExtension,
    /// <summary>Аргументы ffmpeg после «-i вход» и до имени результата (фильтры, кодек, формат).</summary>
    Func<string, ProbeInfo, IReadOnlyList<string>> Arguments,
    /// <summary>Оценка размера результата — для проверки свободного места.</summary>
    Func<long, ProbeInfo, long> EstimateBytes);

/// <summary>Преобразование файла в новый файл рядом с исходником.</summary>
public sealed class ConversionJob(FfmpegTools tools, ConversionSpec spec) : IFileJob
{
    public ConversionSpec Spec => spec;

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var sourceSize = new FileInfo(sourcePath).Length;
        progress?.Report(new JobProgress("Анализ файла", null));
        var info = await ProbeInfo.RunAsync(tools, sourcePath, ct).ConfigureAwait(false);

        if (spec.Requires == MediaRequirement.Image && !info.HasVideo)
            throw new CompressionException("Не удалось прочитать изображение.");
        if (spec.Requires == MediaRequirement.Audio && !info.HasAudio)
            throw new CompressionException("В файле нет звуковой дорожки.");

        var directory = Path.GetDirectoryName(sourcePath)!;
        if (OutputFolderCheck.WhyNot(directory, spec.EstimateBytes(sourceSize, info)) is { } folderProblem)
            throw new CompressionException(folderProblem);

        var extension = spec.OutputExtension(sourcePath);
        var tempOutput = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            var args = new List<string>
            {
                "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-nostats", "-progress", "pipe:1",
                "-i", sourcePath,
            };
            args.AddRange(spec.Arguments(sourcePath, info));
            args.Add(tempOutput);

            var timed = spec.Requires == MediaRequirement.Audio && info.DurationSeconds > 0;
            progress?.Report(new JobProgress("Обработка", timed ? 0 : null));

            var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, line =>
            {
                if (timed && FfmpegProgressParser.TryParseOutTime(line, out var t))
                    progress?.Report(new JobProgress("Обработка", Math.Clamp(t.TotalSeconds / info.DurationSeconds, 0, 1)));
            }, captureOutput: false, ct).ConfigureAwait(false);

            if (result.ExitCode != 0 || !File.Exists(tempOutput))
                throw new CompressionException($"ffmpeg не смог обработать файл (код {result.ExitCode}).", result.ErrorTail);

            var finalPath = OutputFiles.MoveToFreeName(tempOutput,
                () => OutputNameGenerator.Generate(sourcePath, spec.Suffix, extension, OutputNameGenerator.PathTaken));
            progress?.Report(new JobProgress("Готово", 1));
            return new JobResult(JobStatus.Done, FileJobs.DoneMessage(sourceSize, finalPath), finalPath);
        }
        finally
        {
            OutputFiles.TryDeleteFile(tempOutput);
        }
    }
}
