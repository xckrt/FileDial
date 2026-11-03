using System.Diagnostics;

namespace FileDial.App.Setup;

/// <summary>
/// Запущенный FileDial перед заменой или удалением exe: сначала просим закрыться штатно (идущие задачи
/// отменяются, временные файлы убираются), и только если не послушался — завершаем.
/// </summary>
internal static class RunningCopies
{
    /// <summary>Событие, по которому запущенная копия закрывается сама (его ждёт <see cref="App"/>).</summary>
    public const string ExitEventName = @"Local\FileDial.Exit";

    public static bool Any() => Find().Count > 0;

    private static List<Process> Find() =>
        Process.GetProcessesByName("FileDial").Where(p => p.Id != Environment.ProcessId).ToList();

    public static async Task StopAsync(TimeSpan grace)
    {
        var running = Find();
        if (running.Count == 0)
            return;

        if (EventWaitHandle.TryOpenExisting(ExitEventName, out var exit))
            using (exit)
                exit.Set();

        var deadline = DateTime.UtcNow + grace;
        while (DateTime.UtcNow < deadline && running.Any(p => !HasExited(p)))
            await Task.Delay(200).ConfigureAwait(false);

        // Старые версии события не знают — их завершаем.
        foreach (var process in running.Where(p => !HasExited(p)))
        {
            try
            {
                process.Kill();
                await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(3)).Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception
                                           or OperationCanceledException) { }
        }
        foreach (var process in running)
            process.Dispose();
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch (InvalidOperationException) { return true; }
    }
}
