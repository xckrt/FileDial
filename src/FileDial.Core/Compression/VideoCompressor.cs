namespace FileDial.Core.Compression;

public sealed record CompressionProgress(string Stage, double Fraction);

public enum CompressionStatus
{
    Done,
    AlreadySmall,
}

public sealed record CompressionResult(CompressionStatus Status, long SourceSize, string? OutputPath, long OutputSize);

/// <summary>Ошибка с текстом для пользователя.</summary>
public sealed class CompressionException(string message, string? details = null) : Exception(message)
{
    public string? Details { get; } = details;
}

public sealed record CompressionOptions
{
    public long TargetBytes { get; init; } = BitrateCalculator.DefaultTargetBytes;

    /// <summary>Папка для журналов двухпроходного кодирования.</summary>
    public string TempRoot { get; init; } = Path.GetTempPath();
}

/// <summary>Сжатие видео до целевого размера. Исходный файл только читается.</summary>
public sealed class VideoCompressor(FfmpegTools tools, CompressionOptions? options = null)
{
    private readonly CompressionOptions _options = options ?? new CompressionOptions();

    public async Task<CompressionResult> CompressAsync(
        string sourcePath,
        IProgress<CompressionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sourceSize = new FileInfo(sourcePath).Length;
        if (sourceSize <= _options.TargetBytes)
            return new CompressionResult(CompressionStatus.AlreadySmall, sourceSize, null, sourceSize);

        progress?.Report(new CompressionProgress("Анализ файла", 0));
        var info = await ProbeAsync(sourcePath, cancellationToken).ConfigureAwait(false);

        var plan = BitrateCalculator.Plan(info.DurationSeconds, info.HasAudio, info.Width, info.Height,
            _options.TargetBytes);
        if (!plan.IsFeasible)
        {
            throw new CompressionException(
                $"Видео слишком длинное: в {_options.TargetBytes / 1_000_000} МБ на изображение остаётся " +
                $"{Math.Max(plan.VideoKbps, 0)} кбит/с, это меньше разумного минимума " +
                $"{BitrateCalculator.MinVideoKbps} кбит/с.");
        }

        var directory = Path.GetDirectoryName(sourcePath)!;
        var workDir = Path.Combine(_options.TempRoot, "FileDial", Guid.NewGuid().ToString("N"));
        var passLog = Path.Combine(workDir, "pass");
        var tempOutput = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");

        try
        {
            Directory.CreateDirectory(workDir);

            await RunPassAsync(1, FfmpegArguments.FirstPass(sourcePath, plan, passLog), info.DurationSeconds,
                progress, cancellationToken).ConfigureAwait(false);
            await RunPassAsync(2, FfmpegArguments.SecondPass(sourcePath, plan, passLog, tempOutput),
                info.DurationSeconds, progress, cancellationToken).ConfigureAwait(false);

            var finalPath = MoveToFreeName(sourcePath, tempOutput);
            progress?.Report(new CompressionProgress("Готово", 1));
            return new CompressionResult(CompressionStatus.Done, sourceSize, finalPath, new FileInfo(finalPath).Length);
        }
        finally
        {
            TryDeleteFile(tempOutput);
            TryDeleteDirectory(workDir);
        }
    }

    private async Task<ProbeInfo> ProbeAsync(string sourcePath, CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync(tools.Ffprobe, ProbeInfo.Arguments(sourcePath), null,
            captureOutput: true, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new CompressionException("ffprobe не смог прочитать файл. Возможно, он повреждён.", result.ErrorTail);

        ProbeInfo info;
        try
        {
            info = ProbeInfo.Parse(result.StandardOutput);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new CompressionException("Не удалось разобрать ответ ffprobe.", ex.Message);
        }

        if (!info.HasVideo)
            throw new CompressionException("В файле нет видеодорожки.");
        if (info.DurationSeconds <= 0)
            throw new CompressionException("ffprobe не сообщил длительность видео.");
        return info;
    }

    private async Task RunPassAsync(int pass, IReadOnlyList<string> args, double duration,
        IProgress<CompressionProgress>? progress, CancellationToken ct)
    {
        var stage = $"Проход {pass} из 2";
        progress?.Report(new CompressionProgress(stage, FfmpegProgressParser.OverallFraction(pass, TimeSpan.Zero, duration)));

        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, line =>
        {
            if (FfmpegProgressParser.TryParseOutTime(line, out var t))
                progress?.Report(new CompressionProgress(stage, FfmpegProgressParser.OverallFraction(pass, t, duration)));
        }, captureOutput: false, ct).ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new CompressionException($"ffmpeg завершился с ошибкой на проходе {pass} (код {result.ExitCode}).",
                result.ErrorTail);
    }

    /// <summary>Переименовывает готовый файл, не перезаписывая существующие (даже появившиеся во время кодирования).</summary>
    private static string MoveToFreeName(string sourcePath, string tempOutput)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var target = OutputNameGenerator.Generate(sourcePath, OutputNameGenerator.PathTaken);
            try
            {
                File.Move(tempOutput, target, overwrite: false);
                return target;
            }
            catch (IOException) when (OutputNameGenerator.PathTaken(target))
            {
                // Имя заняли между проверкой и переносом — берём следующее.
            }
        }

        throw new CompressionException("Не удалось подобрать свободное имя для результата.");
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
