using FileDial.Core.Compression;
using FileDial.Core.History;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

/// <summary>Задачи, сжатие, история и настройки по-английски — на настоящих путях кода.</summary>
[Collection(LanguageCollection.Name)]
public class LocalizationJobsTests
{
    [Fact]
    public void Suffixes_InEnglish()
    {
        using var _ = new EnglishScope();
        Assert.Equal("_25mb", JobSettings.TargetSuffix(25_000_000));
        Assert.Equal("_1.5mb", JobSettings.TargetSuffix(1_500_000));
        Assert.Equal("_25mb", OutputNameGenerator.Suffix);
        Assert.Equal(@"C:\v\clip_25mb.mp4", OutputNameGenerator.Generate(@"C:\v\clip.mkv", _ => false));
        Assert.Equal("_muted", ConversionSpecs.For(ActionIds.Mute)!.Suffix);
        Assert.Equal("_frame", ConversionSpecs.For(ActionIds.Frame)!.Suffix);
        Assert.Equal("_rotated_left", ConversionSpecs.For(ActionIds.RotateLeft)!.Suffix);
        Assert.Equal("_x1.5", ConversionSpecs.For(ActionIds.SpeedUp)!.Suffix);
        Assert.Equal("_trimmed", TrimSpecs.Suffix);
    }

    [Fact]
    public void Suffixes_BackInRussian()
    {
        using (new EnglishScope())
            Assert.Equal("_muted", ConversionSpecs.For(ActionIds.Mute)!.Suffix);
        Assert.Equal("_без_звука", ConversionSpecs.For(ActionIds.Mute)!.Suffix);
        Assert.Equal("_25мб", OutputNameGenerator.Suffix);
    }

    [Fact]
    public void CompressionCheck_InEnglish()
    {
        using var _ = new EnglishScope();
        Assert.Equal("The file is already under 25 MB", CompressionCheck.WhyNot(534_323, null));
        Assert.Equal("The file has no video track",
            CompressionCheck.WhyNot(80_000_000, new ProbeInfo(60, false, true, 0, 0)));
        Assert.Equal("Videos over 18 min do not fit in 25 MB",
            CompressionCheck.WhyNot(900_000_000, new ProbeInfo(25 * 60, true, true, 1920, 1080)));
    }

    [Fact]
    public void Messages_InEnglish()
    {
        using var _ = new EnglishScope();
        Assert.Equal("The end must be after the start.",
            TrimRequest.Validate(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), 60));
        Assert.Equal("Add Ctrl, Alt or Win.", new HotkeyGesture(HotkeyModifiers.Shift, 'K').Problem());
        Assert.Equal("Needs a speech recognition model", Speech.WhyNot(JobSettings.Default));
        Assert.Equal("Auto", Speech.Languages[0].Label);
        Assert.Equal("Accurate", SpeechModels.Small.Label);
        Assert.Equal("Already PNG.", BatchRunner.SkipWhenAlreadyInFormat(ActionIds.ToPng)!(@"C:\a\b.png"));

        var summary = new BatchSummary(
        [
            new FileOutcome(@"C:\a\1.png", HistoryStatus.Done, "ok"),
            new FileOutcome(@"C:\a\2.png", HistoryStatus.NothingToDo, "skip"),
            new FileOutcome(@"C:\a\3.png", HistoryStatus.Failed, "File not found."),
        ]);
        Assert.Equal("Done: 1 of 3. Skipped: 1 (nothing to do). Errors: 1. 3.png: File not found.", summary.Message);
    }

    [Fact]
    public void History_InEnglish()
    {
        using var _ = new EnglishScope();
        var now = new DateTime(2026, 11, 2, 15, 0, 0);
        Assert.Equal("Today", HistoryText.DayLabel(now, now));
        Assert.Equal("Yesterday", HistoryText.DayLabel(now.AddDays(-1), now));
        Assert.Equal("October 28", HistoryText.DayLabel(new DateTime(2026, 10, 28), now));
        Assert.Equal("December 31, 2025", HistoryText.DayLabel(new DateTime(2025, 12, 31), now));
        Assert.Equal("Nothing to do", HistoryText.StatusLabel(HistoryStatus.NothingToDo));
    }
}
