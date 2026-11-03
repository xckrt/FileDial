using FileDial.Core.Compression;
using FileDial.Core.History;
using FileDial.Core.Menu;
using FileDial.Core.Text;

namespace FileDial.Core.Jobs;

/// <summary>Итог по одному файлу пакета.</summary>
public sealed record FileOutcome(string SourcePath, HistoryStatus Status, string Message, string? OutputPath = null);

public sealed record BatchSummary(IReadOnlyList<FileOutcome> Outcomes)
{
    public int Done => Outcomes.Count(o => o.Status == HistoryStatus.Done);
    public int Skipped => Outcomes.Count(o => o.Status == HistoryStatus.NothingToDo);
    public int Failed => Outcomes.Count(o => o.Status == HistoryStatus.Failed);

    /// <summary>«Готово: 4 из 5. Пропущено: 1 (уже в нужном виде).»</summary>
    public string Message
    {
        get
        {
            var parts = new List<string> { $"Готово: {Done} из {Outcomes.Count}." };
            if (Skipped > 0)
                parts.Add($"Пропущено: {Skipped} (делать нечего).");
            if (Failed > 0)
            {
                var first = Outcomes.First(o => o.Status == HistoryStatus.Failed);
                parts.Add($"Ошибок: {Failed}. {Path.GetFileName(first.SourcePath)}: {first.Message}");
            }
            return string.Join(" ", parts);
        }
    }

    /// <summary>Последний созданный файл — для «Показать в папке».</summary>
    public string? LastOutput => Outcomes.LastOrDefault(o => o.OutputPath is not null)?.OutputPath;
}

/// <summary>
/// Выполняет одно действие для нескольких файлов по очереди. Ошибка одного файла не останавливает
/// остальные; отмена останавливает всё (текущий файл чистится как обычно).
/// </summary>
public static class BatchRunner
{
    public static async Task<BatchSummary> RunAsync(
        IReadOnlyList<string> paths,
        Func<string, IFileJob> jobFor,
        Func<string, string?>? skipReason,
        IProgress<JobProgress>? progress,
        Action<FileOutcome>? onFile,
        CancellationToken ct)
    {
        var outcomes = new List<FileOutcome>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = paths[i];
            var prefix = $"{i + 1} из {paths.Count} · {Path.GetFileName(path)}";

            FileOutcome outcome;
            if (skipReason?.Invoke(path) is { } reason)
            {
                outcome = new FileOutcome(path, HistoryStatus.NothingToDo, reason);
            }
            else
            {
                var index = i;
                var inner = progress is null ? null : new Relay(p => progress.Report(new JobProgress(
                    $"{prefix} · {p.Stage}",
                    (index + (p.Fraction ?? 0)) / paths.Count)));
                inner?.Report(new JobProgress("Подготовка", 0));
                try
                {
                    var result = await jobFor(path).RunAsync(path, inner, ct).ConfigureAwait(false);
                    outcome = new FileOutcome(path,
                        result.Status == JobStatus.Done ? HistoryStatus.Done : HistoryStatus.NothingToDo,
                        result.Message, result.OutputPath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (CompressionException ex)
                {
                    outcome = new FileOutcome(path, HistoryStatus.Failed, ex.Message);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    outcome = new FileOutcome(path, HistoryStatus.Failed,
                        ex is FileNotFoundException or DirectoryNotFoundException ? "Файл не найден." : "Нет доступа к файлу.");
                }
            }

            outcomes.Add(outcome);
            onFile?.Invoke(outcome);
            progress?.Report(new JobProgress(prefix, (i + 1.0) / paths.Count));
        }
        return new BatchSummary(outcomes);
    }

    /// <summary>Для конвертации: файл уже в целевом формате — пропускаем, а не делаем копию.</summary>
    public static Func<string, string?>? SkipWhenAlreadyInFormat(string actionId)
    {
        var target = ActionIds.TargetExtension(actionId, ActionIds.ImageToPrefix)
                     ?? ActionIds.TargetExtension(actionId, ActionIds.AudioToPrefix)
                     ?? ActionIds.TargetExtension(actionId, ActionIds.VideoToPrefix)
                     ?? ActionIds.TargetExtension(actionId, ActionIds.ArchiveToPrefix)
                     ?? ActionIds.TargetExtension(actionId, ActionIds.DocToPrefix);
        if (target is null)
            return null;
        return path => ActionCatalog.NormalizeExtension(Path.GetExtension(path)) == target
            ? $"Уже в формате {target.TrimStart('.').ToUpperInvariant()}."
            : null;
    }

    private sealed class Relay(Action<JobProgress> report) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) => report(value);
    }
}
