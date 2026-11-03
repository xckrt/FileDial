using FileDial.Core.Compression;
using FileDial.Core.Localization;

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
    public string MissingPictureMessage { get; init; } = Lang.T.JobNoVideoTrack + ".";

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

    /// <summary>Длительность результата для процентов, если она не равна длительности исходника (обрезка).</summary>
    public Func<ProbeInfo, double>? OutputDuration { get; init; }

    /// <summary>
    /// Если результат пишет сам фильтр (например, whisper → destination), аргументы получают путь
    /// временного файла, а ffmpeg ничего не выводит в файл сам. Иначе — null.
    /// </summary>
    public Func<string, ProbeInfo, string, IReadOnlyList<string>>? FilterArguments { get; init; }

    /// <summary>Доработка готового временного файла перед переносом (например, нумерация SRT).</summary>
    public Action<string>? PostProcess { get; init; }

    /// <summary>Что сказать, если результат пуст (например, в записи нет речи). null — пустой результат считается ошибкой.</summary>
    public string? EmptyOutputMessage { get; init; }
}

/// <summary>Преобразование файла в новый файл рядом с исходником.</summary>
/// <param name="outputDirectory">Папка для результата; null — рядом с исходником.</param>
public sealed class ConversionJob(FfmpegTools tools, ConversionSpec spec, string? outputDirectory = null) : IFileJob
{
    public ConversionSpec Spec => spec;

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var sourceSize = new FileInfo(sourcePath).Length;
        progress?.Report(new JobProgress(Lang.T.JobStageAnalyzing, null));
        var info = await ProbeInfo.RunAsync(tools, sourcePath, ct).ConfigureAwait(false);

        if (spec.Needs.HasFlag(MediaNeeds.Picture) && !info.HasVideo)
            throw new CompressionException(spec.MissingPictureMessage);
        if (spec.Needs.HasFlag(MediaNeeds.Sound) && !info.HasAudio)
            throw new CompressionException(Lang.T.JobNoAudioTrack);

        var directory = outputDirectory ?? Path.GetDirectoryName(sourcePath)!;
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
            if (spec.FilterArguments is { } filterArgs)
            {
                args.AddRange(filterArgs(sourcePath, info, tempOutput));
            }
            else
            {
                args.AddRange(spec.Arguments(sourcePath, info));
                args.Add(tempOutput);
            }

            var duration = spec.OutputDuration?.Invoke(info) ?? info.DurationSeconds;
            var timed = spec.Timed && duration > 0;
            progress?.Report(new JobProgress(Lang.T.JobStageProcessing, timed ? 0 : null));

            var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, line =>
            {
                if (timed && FfmpegProgressParser.TryParseOutTime(line, out var t))
                    progress?.Report(new JobProgress(Lang.T.JobStageProcessing, Math.Clamp(t.TotalSeconds / duration, 0, 1)));
            }, captureOutput: false, ct).ConfigureAwait(false);

            if (result.ExitCode != 0)
                throw new CompressionException(Lang.T.JobFfmpegFailed(result.ExitCode), result.ErrorTail);
            if (spec.EmptyOutputMessage is { } empty && (!File.Exists(tempOutput) || new FileInfo(tempOutput).Length == 0))
                return new JobResult(JobStatus.NothingToDo, empty);
            if (!File.Exists(tempOutput))
                throw new CompressionException(Lang.T.JobFfmpegNoOutput, result.ErrorTail);
            spec.PostProcess?.Invoke(tempOutput);

            var finalPath = OutputFiles.MoveToFreeName(tempOutput,
                () => OutputNameGenerator.Generate(sourcePath, spec.Suffix, extension, OutputNameGenerator.PathTaken, directory));
            progress?.Report(new JobProgress(Lang.T.JobStageDone, 1));
            return new JobResult(JobStatus.Done, FileJobs.DoneMessage(sourceSize, finalPath), finalPath);
        }
        finally
        {
            OutputFiles.TryDeleteFile(tempOutput);
        }
    }
}
