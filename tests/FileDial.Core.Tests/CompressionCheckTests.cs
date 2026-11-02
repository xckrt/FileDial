using FileDial.Core.Compression;
using FileDial.Core.Menu;
using FileDial.Core.Selection;
using FileDial.Core.Text;

namespace FileDial.Core.Tests;

public class CompressionCheckTests
{
    private static ProbeInfo Video(double seconds, bool audio = true) => new(seconds, true, audio, 1920, 1080);

    [Fact]
    public void SmallFile_IsAlreadySmall_EvenWithoutProbe()
    {
        Assert.Equal(CompressionCheck.AlreadySmall, CompressionCheck.WhyNot(534_323, null));
    }

    [Fact]
    public void LargeFile_WithoutProbe_IsAllowed()
    {
        Assert.Null(CompressionCheck.WhyNot(80_000_000, null));
    }

    [Fact]
    public void NormalVideo_IsAllowed()
    {
        Assert.Null(CompressionCheck.WhyNot(80_000_000, Video(120)));
    }

    [Fact]
    public void TooLongVideo_ExplainsLimit()
    {
        Assert.Equal("Видео длиннее 18 мин в 25 МБ не уместить", CompressionCheck.WhyNot(900_000_000, Video(25 * 60)));
    }

    [Fact]
    public void LongVideoWithoutAudio_HasMoreRoom()
    {
        Assert.Null(CompressionCheck.WhyNot(900_000_000, Video(25 * 60, audio: false)));
    }

    [Fact]
    public void NoVideoStream_IsRejected()
    {
        Assert.Equal(CompressionCheck.NoVideo,
            CompressionCheck.WhyNot(80_000_000, new ProbeInfo(60, false, true, 0, 0)));
    }

    [Fact]
    public void UnknownDuration_IsAllowed()
    {
        Assert.Null(CompressionCheck.WhyNot(80_000_000, Video(0)));
    }

    [Fact]
    public void MaxDuration_MatchesFeasibility()
    {
        var max = BitrateCalculator.MaxDurationSeconds(hasAudio: true);
        Assert.Equal(1089.9, max, 1);
        Assert.True(BitrateCalculator.Plan(max - 1, true, 1920, 1080).IsFeasible);
        Assert.False(BitrateCalculator.Plan(max + 10, true, 1920, 1080).IsFeasible);
    }

    [Fact]
    public void CorrectedVideoKbps_SubtractsOvershoot()
    {
        // Цель 1 МБ, вышло 1,05 МБ за 10 с: перебор над (цель − 3%) = 80 000 байт = 64 кбит/с.
        Assert.Equal(936, BitrateCalculator.CorrectedVideoKbps(1000, 1_050_000, 10, 1_000_000));
    }

    [Fact]
    public void CorrectedVideoKbps_NoOvershoot_KeepsBitrate()
    {
        Assert.Equal(1000, BitrateCalculator.CorrectedVideoKbps(1000, 900_000, 10, 1_000_000));
    }
}

public class MenuWithMediaProbeTests
{
    private sealed class FakeFile(long size) : IFileProbe
    {
        public FileState Probe(string path) => new(FileStateKind.File, size);
    }

    private sealed class FakeMedia(ProbeInfo? info) : IMediaProbe
    {
        public int Calls { get; private set; }

        public ProbeInfo? TryProbe(string path)
        {
            Calls++;
            return info;
        }
    }

    private static DialMenu Menu(HotkeyOutcome outcome) => Assert.IsType<HotkeyOutcome.ShowMenu>(outcome).Menu;

    [Fact]
    public void LongVideo_CompressDisabled_DurationShown()
    {
        var media = new FakeMedia(new ProbeInfo(40 * 60, true, true, 1920, 1080));
        var menu = Menu(SelectionInterpreter.Interpret(
            SelectionResult.Success([@"C:\v\лекция.mkv"]), new FakeFile(900_000_000), media));

        Assert.Equal("900 МБ · 40:00", menu.Subtitle);
        Assert.False(menu.Actions[0].IsEnabled);
        Assert.StartsWith("Видео длиннее", menu.Actions[0].DisabledReason);
    }

    [Fact]
    public void ProbeFailure_KeepsCompressEnabled()
    {
        var menu = Menu(SelectionInterpreter.Interpret(
            SelectionResult.Success([@"C:\v\clip.mp4"]), new FakeFile(80_000_000), new FakeMedia(null)));

        Assert.Equal("80 МБ", menu.Subtitle);
        Assert.True(menu.Actions[0].IsEnabled);
    }

    [Fact]
    public void NonVideo_IsNotProbed()
    {
        var media = new FakeMedia(null);
        SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\v\doc.pdf"]), new FakeFile(1000), media);
        Assert.Equal(0, media.Calls);
    }

    [Fact]
    public void LongName_IsShortenedInTheMiddle_PathKept()
    {
        const string path = @"C:\v\d5911ee0384c8c634bc9fae27297e18a_очень_длинное_имя_файла.mp4";
        var menu = Menu(SelectionInterpreter.Interpret(
            SelectionResult.Success([path]), new FakeFile(80_000_000), null));

        Assert.Equal(SelectionInterpreter.MaxTitleLength, menu.Title.Length);
        Assert.StartsWith("d5911ee0", menu.Title);
        Assert.EndsWith(".mp4", menu.Title);
        Assert.Contains("…", menu.Title);
        Assert.Equal(path, menu.SourcePath);
    }
}

public class TextFormattingTests
{
    [Theory]
    [InlineData("short.mp4", 20, "short.mp4")]
    [InlineData("abcdefghijklmnopqrstuvwxyz.mp4", 12, "abcdef…z.mp4")]
    public void NameShortener_Middle(string name, int max, string expected)
    {
        Assert.Equal(expected, NameShortener.Middle(name, max));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(42.4, "0:42")]
    [InlineData(725, "12:05")]
    [InlineData(3723, "1:02:03")]
    public void DurationFormatter_Format(double seconds, string expected)
    {
        Assert.Equal(expected, DurationFormatter.Format(seconds));
    }
}
