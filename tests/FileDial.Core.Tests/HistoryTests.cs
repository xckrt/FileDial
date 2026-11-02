using FileDial.Core.History;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

public class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "История");
    private string File_ => Path.Combine(_dir, "history.json");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static HistoryEntry Entry(int minute, HistoryStatus status = HistoryStatus.Done, string action = "В WebP") => new()
    {
        Time = new DateTimeOffset(2026, 11, 2, 12, minute, 0, TimeSpan.FromHours(3)),
        Action = action,
        SourcePath = $@"D:\Фото\снимок {minute}.png",
        OutputPath = status == HistoryStatus.Done ? $@"D:\Фото\снимок {minute}.webp" : null,
        Status = status,
        Message = "3,4 МБ → 0,9 МБ.",
    };

    [Fact]
    public void Empty_WhenNoFile()
    {
        Assert.Empty(HistoryStore.Load(File_));
    }

    [Fact]
    public void Append_KeepsNewestFirst_AndRoundTrips()
    {
        HistoryStore.Append(File_, Entry(1));
        HistoryStore.Append(File_, Entry(2, HistoryStatus.Failed, "Сжать до 25 МБ"));

        var loaded = HistoryStore.Load(File_);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(Entry(2, HistoryStatus.Failed, "Сжать до 25 МБ"), loaded[0]);
        Assert.Equal(Entry(1), loaded[1]);
        Assert.Contains("\"Failed\"", File.ReadAllText(File_)); // статус словом, файл читаем руками
        Assert.Contains("снимок", File.ReadAllText(File_));
    }

    [Fact]
    public void Append_TrimsOldEntries()
    {
        for (var i = 0; i < 7; i++)
            HistoryStore.Append(File_, Entry(i), maxEntries: 5);

        var loaded = HistoryStore.Load(File_);
        Assert.Equal(5, loaded.Count);
        Assert.Equal(Entry(6).Time, loaded[0].Time);
        Assert.Equal(Entry(2).Time, loaded[^1].Time);
    }

    [Theory]
    [InlineData("мусор")]
    [InlineData("{}")]
    [InlineData("")]
    public void CorruptedFile_GivesEmptyHistory_AndNextAppendRepairsIt(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, content);

        Assert.Empty(HistoryStore.Load(File_));
        HistoryStore.Append(File_, Entry(1));
        Assert.Single(HistoryStore.Load(File_));
    }

    [Fact]
    public void Clear_RemovesFile()
    {
        HistoryStore.Append(File_, Entry(1));
        HistoryStore.Clear(File_);
        Assert.Empty(HistoryStore.Load(File_));
        HistoryStore.Clear(File_); // повторная очистка не падает
    }

    [Fact]
    public void KeepHistory_IsOnByDefault()
    {
        Assert.True(new AppSettings().KeepHistory);
    }
}

public class HistoryTextTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 15, 0, 0);

    [Theory]
    [InlineData(2026, 11, 2, "Сегодня")]
    [InlineData(2026, 11, 1, "Вчера")]
    [InlineData(2026, 10, 28, "28 октября")]
    [InlineData(2025, 12, 31, "31 декабря 2025")]
    public void DayLabel(int y, int m, int d, string expected)
    {
        Assert.Equal(expected, HistoryText.DayLabel(new DateTime(y, m, d, 23, 59, 0), Now));
    }

    [Fact]
    public void GroupByDay_NewestFirst()
    {
        HistoryEntry At(DateTime local) => new()
        {
            Time = new DateTimeOffset(local),
            Action = "a",
            SourcePath = @"C:\a",
            Status = HistoryStatus.Done,
        };

        var groups = HistoryText.GroupByDay(
            [At(Now.AddDays(-1)), At(Now), At(Now.AddHours(-1)), At(Now.AddDays(-5))], Now);

        Assert.Equal(["Сегодня", "Вчера", "28 октября"], groups.Select(g => g.Label));
        Assert.Equal(2, groups[0].Entries.Count);
        Assert.True(groups[0].Entries[0].Time > groups[0].Entries[1].Time);
    }

    [Theory]
    [InlineData(HistoryStatus.Done, "Готово")]
    [InlineData(HistoryStatus.Failed, "Ошибка")]
    [InlineData(HistoryStatus.Cancelled, "Отменено")]
    [InlineData(HistoryStatus.NothingToDo, "Ничего не нужно")]
    public void StatusLabel(HistoryStatus status, string expected)
    {
        Assert.Equal(expected, HistoryText.StatusLabel(status));
    }
}
