using System.Text.Json;
using System.Text.Json.Serialization;
using FileDial.Core.Localization;

namespace FileDial.Core.History;

public enum HistoryStatus
{
    Done,
    NothingToDo,
    Cancelled,
    Failed,
}

/// <summary>Одно выполненное (или прерванное) действие над файлом.</summary>
public sealed record HistoryEntry
{
    public required DateTimeOffset Time { get; init; }

    /// <summary>Заголовок действия, как в окне: «Сжать до 25 МБ», «В WebP».</summary>
    public required string Action { get; init; }

    public required string SourcePath { get; init; }

    public string? OutputPath { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required HistoryStatus Status { get; init; }

    /// <summary>Итог для пользователя: «30 МБ → 24,1 МБ…», текст ошибки и т. п.</summary>
    public string Message { get; init; } = "";
}

public static class HistoryStore
{
    public const int MaxEntries = 200;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileDial", "history.json");

    /// <summary>Записи от новых к старым. Нет файла или он испорчен — пустая история (без падения).</summary>
    public static IReadOnlyList<HistoryEntry> Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return [];
            var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(path), Json);
            return entries?.Where(e => e is not null).OrderByDescending(e => e.Time).ToList() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Добавляет запись в начало; старые сверх <see cref="MaxEntries"/> отбрасываются.</summary>
    public static IReadOnlyList<HistoryEntry> Append(string path, HistoryEntry entry, int maxEntries = MaxEntries)
    {
        var entries = Load(path).Prepend(entry).Take(maxEntries).ToList();
        Save(path, entries);
        return entries;
    }

    public static void Clear(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void Save(string path, IReadOnlyList<HistoryEntry> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries, Json));
        File.Move(temp, path, overwrite: true);
    }
}

public static class HistoryText
{
    /// <summary>Заголовок группы: «Сегодня», «Вчера», «28 октября», «28 октября 2025».</summary>
    public static string DayLabel(DateTime day, DateTime today)
    {
        var d = day.Date;
        if (d == today.Date) return Lang.T.JobToday;
        if (d == today.Date.AddDays(-1)) return Lang.T.JobYesterday;
        return Lang.T.JobDayLabel(d, withYear: d.Year != today.Year);
    }

    public static string StatusLabel(HistoryStatus status) => status switch
    {
        HistoryStatus.Done => Lang.T.JobStatusDone,
        HistoryStatus.NothingToDo => Lang.T.JobStatusNothingToDo,
        HistoryStatus.Cancelled => Lang.T.JobStatusCancelled,
        HistoryStatus.Failed => Lang.T.JobStatusFailed,
        _ => "",
    };

    /// <summary>Группы по дням (по местному времени), от новых к старым.</summary>
    public static IReadOnlyList<(string Label, IReadOnlyList<HistoryEntry> Entries)> GroupByDay(
        IEnumerable<HistoryEntry> entries, DateTime now) =>
        entries
            .OrderByDescending(e => e.Time)
            .GroupBy(e => e.Time.ToLocalTime().Date)
            .Select(g => (DayLabel(g.Key, now), (IReadOnlyList<HistoryEntry>)g.ToList()))
            .ToList();
}
