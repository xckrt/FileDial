using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;

namespace FileDial.Core.Custom;

/// <summary>
/// Запуск своего действия для одного файла. Аргументы передаются списком (ArgumentList), без cmd.exe.
/// Успех — код 0 и, если в шаблоне есть {out}, появившийся файл результата.
/// Видимое окно без {out} (например, «открыть в Блокноте») запускается и не ждётся.
/// </summary>
/// <param name="tools">ffmpeg для {ffmpeg}; null — не найден.</param>
/// <param name="outputDirectory">Папка для {out}; null — рядом с исходником.</param>
public sealed class CustomActionJob(CustomAction action, FfmpegTools? tools, string? outputDirectory = null) : IFileJob
{
    private const int TailLines = 15;

    static CustomActionJob()
    {
        // Консольные программы Windows пишут в OEM-кодировке (cp866 у русской Windows).
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public CustomAction Action => action;

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var sourceSize = new FileInfo(sourcePath).Length; // нет файла — FileNotFoundException, как у остальных задач
        if (CustomCommand.UsesFfmpeg(action) && tools is null)
            throw new CompressionException(Lang.T.CstNeedsFfmpeg);

        var usesOutput = CustomCommand.UsesOutput(action);
        var output = usesOutput
            ? CustomCommand.OutputPath(action, sourcePath, outputDirectory, OutputNameGenerator.PathTaken)
            : null;
        var command = CustomCommand.Build(action, sourcePath, output, tools?.Ffmpeg);
        var program = ProgramLocator.Find(command.Program)
                      ?? throw new CompressionException(Lang.T.CstProgramNotFound(CustomCommand.ProgramText(action)));
        var programName = Path.GetFileName(program);
        var workingDirectory = Path.GetDirectoryName(sourcePath);

        progress?.Report(new JobProgress(Lang.T.CstStageRunning(programName), null));
        var finished = false;
        try
        {
            if (action.ShowWindow && !usesOutput)
            {
                StartDetached(program, command.Arguments, workingDirectory);
                finished = true;
                return new JobResult(JobStatus.Done, Lang.T.CstStarted(programName));
            }

            var (exitCode, details) = action.ShowWindow
                ? (await RunVisibleAsync(program, command.Arguments, workingDirectory, ct).ConfigureAwait(false), "")
                : await RunHiddenAsync(program, command.Arguments, workingDirectory, ct).ConfigureAwait(false);

            if (exitCode != 0)
                throw new CompressionException(Lang.T.CstExitCode(exitCode), details.Length > 0 ? details : null);
            if (output is not null && !File.Exists(output))
                throw new CompressionException(Lang.T.CstNoOutput, details.Length > 0 ? details : null);

            finished = true;
            progress?.Report(new JobProgress(Lang.T.JobStageDone, 1));
            return output is null
                ? new JobResult(JobStatus.Done, Lang.T.CstFinished(programName))
                : new JobResult(JobStatus.Done, FileJobs.DoneMessage(sourceSize, output), output);
        }
        finally
        {
            // Ошибка или отмена: недописанный результат убираем. Имя было свободно до запуска — чужого не трогаем.
            if (!finished && output is not null)
                OutputFiles.TryDeleteFile(output);
        }
    }

    private static ProcessStartInfo StartInfo(string program, IReadOnlyList<string> arguments, string? workingDirectory,
        bool visible)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = !visible };
        if (workingDirectory is { Length: > 0 } && Directory.Exists(workingDirectory))
            psi.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);
        return psi;
    }

    private static void StartDetached(string program, IReadOnlyList<string> arguments, string? workingDirectory)
    {
        try
        {
            using var process = Process.Start(StartInfo(program, arguments, workingDirectory, visible: true));
        }
        catch (Win32Exception ex)
        {
            throw new CompressionException(Lang.T.CstCannotStart(ex.Message));
        }
    }

    /// <summary>Без окна: вывод собираем, чтобы при ошибке показать хвост (stderr, а если он пуст — stdout).</summary>
    private static async Task<(int ExitCode, string Details)> RunHiddenAsync(string program, IReadOnlyList<string> arguments,
        string? workingDirectory, CancellationToken ct)
    {
        var stdout = new Queue<string>();
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(program, arguments, line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                lock (stdout)
                {
                    stdout.Enqueue(line);
                    while (stdout.Count > TailLines) stdout.Dequeue();
                }
            }, captureOutput: false, ct, workingDirectory, OutputEncoding(program)).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            throw new CompressionException(Lang.T.CstCannotStart(ex.Message));
        }
        string tail;
        lock (stdout) tail = string.Join(Environment.NewLine, stdout);
        return (result.ExitCode, result.ErrorTail.Length > 0 ? result.ErrorTail : tail);
    }

    /// <summary>С окном: вывод видит пользователь, мы ждём только код возврата. Отмена закрывает программу.</summary>
    private static async Task<int> RunVisibleAsync(string program, IReadOnlyList<string> arguments, string? workingDirectory,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = StartInfo(program, arguments, workingDirectory, visible: true) };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new CompressionException(Lang.T.CstCannotStart(ex.Message));
        }
        await using (ct.Register(() =>
                     {
                         try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                         catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
                     }))
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    /// <summary>ffmpeg пишет UTF-8, остальные консольные программы Windows — OEM-кодировку.</summary>
    private static Encoding OutputEncoding(string program)
    {
        if (Path.GetFileNameWithoutExtension(program).StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8;
        try
        {
            var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
            return Encoding.GetEncoding(oem is 0 or 1 ? 437 : oem);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }
}
