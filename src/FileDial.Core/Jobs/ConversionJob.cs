using FileDial.Core.Compression;

namespace FileDial.Core.Jobs;

/// <summary>Какие дорожки нужны во входном файле.</summary>
[Flags]
public enum MediaNeeds
{
    None = 0,
    Picture = 1,
    Sound = 2,
}

/// <summary>Описание однопроходного преобразования через ffmpeg.</summary>
public sealed record ConversionSpec
{
    /// <summary>Добавка к имени результата (пустая при простой смене формата).</summary>
    public required string Suffix { get; init; }

    public required MediaNeeds Needs { get; init; }

    /// <summary>Текст, если изображения нет (для картинок и видео он разный).</summary>
    public string MissingPictureMessage { get; init; } = "В файле нет видеодорожки.";

    /// <summary>Расширение результата: по пути исходника и сведениям ffprobe (например, кодеку звука).</summary>
    public required Func<string, ProbeInfo, string> OutputExtension { get; init; }

    /// <summary>Аргументы до «-i вход» (например, быстрый переход к нужному месту).</summary>
    public Func<string, ProbeInfo, IReadOnlyList<string>> InputArguments { get; init; } = (_, _) => [];

    /// <summary>Аргументы после «-i вход» и до имени результата (дорожки, фильтры, кодек, формат).</summary>
    public required Func<string, ProbeInfo, IReadOnlyList<string>> Arguments { get; init; }

    /// <summary>Оценка размера результата — для проверки свободного места.</summary>
    public required Func<long, ProbeInfo, long> EstimateBytes { get; init; }

    /// <summary>Показывать проценты по длительности (для однокадровых операций — нет).</summary>
    public bool Timed { get; init; }
}

/// <summary>Преобразование файла в новый файл рядом с исходником.</summary>
public sealed class ConversionJob(FfmpegTools tools, ConversionSpec spec) : IFileJob
{
    public ConversionSpec Spec => spec;

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var sourceSize = new FileInfo(sourcePath).Length;
        progress?.Report(new JobProgress("Анализ файла", null));
        var info = await ProbeInfo.RunAsync(tools, sourcePath, ct).ConfigureAwait(false);

        if (spec.Needs.HasFlag(MediaNeeds.Picture) && !info.HasVideo)
            throw new CompressionException(spec.MissingPictureMessage);
        if (spec.Needs.HasFlag(MediaNeeds.Sound) && !info.HasAudio)
            throw new CompressionException("В файле нет звуковой дорожки.");

        var directory = Path.GetDirectoryName(sourcePath)!;
        if (OutputFolderCheck.WhyNot(directory, spec.EstimateBytes(sourceSize, info)) is { } folderProblem)
            throw new CompressionException(folderProblem);

        var extension = spec.OutputExtension(sourcePath, info);
        var tempOutput = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            var args = new List<string>
            {
                "-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-nostats", "-progress", "pipe:1",
            };
            args.AddRange(spec.InputArguments(sourcePath, info));
            args.AddRange(["-i", sourcePath]);
            args.AddRange(spec.Arguments(sourcePath, info));
            args.Add(tempOutput);

            var timed = spec.Timed && info.DurationSeconds > 0;
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
