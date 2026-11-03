using System.Diagnostics;
using System.Text;

namespace FileDial.Core.Compression;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string ErrorTail);

/// <summary>Запуск внешней программы со списком аргументов (без склейки командной строки вручную).</summary>
public static class ProcessRunner
{
    private const int ErrorTailLines = 15;

    public static async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        Action<string>? onOutputLine,
        bool captureOutput,
        CancellationToken cancellationToken,
        string? workingDirectory = null,
        Encoding? outputEncoding = null)
    {
        var psi = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // ffmpeg пишет UTF-8; tar.exe — в ANSI-кодировке системы (вызывающий передаёт свою).
            StandardOutputEncoding = outputEncoding ?? Encoding.UTF8,
            StandardErrorEncoding = outputEncoding ?? Encoding.UTF8,
        };
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        var output = new StringBuilder();
        var errors = new Queue<string>();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            if (captureOutput)
                lock (output) output.AppendLine(e.Data);
            onOutputLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (errors)
            {
                errors.Enqueue(e.Data);
                while (errors.Count > ErrorTailLines) errors.Dequeue();
            }
        };

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using (cancellationToken.Register(() => Kill(process)))
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        string outText, errText;
        lock (output) outText = output.ToString();
        lock (errors) errText = string.Join(Environment.NewLine, errors);
        return new ProcessResult(process.ExitCode, outText, errText);
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Процесс уже завершился.
        }
    }
}
