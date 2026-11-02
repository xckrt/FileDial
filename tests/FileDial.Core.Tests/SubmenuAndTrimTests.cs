using FileDial.Core.Compression;
using FileDial.Core.Geometry;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

public class SubmenuGeometryTests
{
    // Веер из 6 секторов по 30° над верхним сектором: от −90° до +90°.
    private readonly SubmenuGeometry _top = new(parentAngle: 0, count: 6, innerRadius: 196, outerRadius: 264, sweepDegrees: 30);

    [Fact]
    public void Fan_IsCenteredOnParent()
    {
        Assert.Equal(-90, _top.FanStart, 6);
        Assert.Equal(90, _top.FanEnd, 6);
        Assert.Equal(-75, _top.CenterAngle(0), 6);
        Assert.Equal(75, _top.CenterAngle(5), 6);
    }

    [Theory]
    [InlineData(-80, 0)]
    [InlineData(-1, 2)]
    [InlineData(1, 3)]
    [InlineData(89, 5)]
    public void HitTest_ByAngle_ClockwiseFromFanStart(double angle, int expected)
    {
        var p = SectorGeometry.PointAt(angle, 230);
        Assert.Equal(expected, _top.HitTest(p.X, p.Y, bridgeRadius: 182));
    }

    [Fact]
    public void HitTest_WrapsAroundZero()
    {
        // Веер у сектора «Формат» (−45°) из 4 по 30°: от −105° (= 255°) до 15°.
        var fan = new SubmenuGeometry(-45, 4, 196, 264, 30);
        var p = SectorGeometry.PointAt(260, 230);
        Assert.Equal(0, fan.HitTest(p.X, p.Y, 182));
        var q = SectorGeometry.PointAt(10, 230);
        Assert.Equal(3, fan.HitTest(q.X, q.Y, 182));
    }

    [Theory]
    [InlineData(180, 230)] // вне веера по углу
    [InlineData(0, 150)]   // внутри основного круга
    [InlineData(0, 270)]   // за внешним краем
    public void HitTest_OutsideFan_IsNull(double angle, double radius)
    {
        var p = SectorGeometry.PointAt(angle, radius);
        Assert.Null(_top.HitTest(p.X, p.Y, 182));
    }

    [Fact]
    public void Bridge_BetweenCircleAndRing_CountsAsRing()
    {
        // Мышь идёт от сектора наружу и проходит зазор 182–196 — веер не должен закрываться.
        var p = SectorGeometry.PointAt(5, 188);
        Assert.Equal(3, _top.HitTest(p.X, p.Y, bridgeRadius: 182));
    }

    [Fact]
    public void EveryLabelPointHitsItsChild()
    {
        for (var i = 0; i < 6; i++)
        {
            var p = _top.LabelPoint(i);
            Assert.Equal(i, _top.HitTest(p.X, p.Y, 182));
        }
    }

    [Fact]
    public void Constructor_RejectsTooWideFan()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SubmenuGeometry(0, 13, 196, 264, 30));
    }
}

public class TimeInputTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("83", 83)]
    [InlineData("83,5", 83.5)]
    [InlineData("1:23", 83)]
    [InlineData("1:23.5", 83.5)]
    [InlineData(" 01:02:03 ", 3723)]
    [InlineData("90:00", 5400)] // минуты больше 59 в первой части допустимы
    public void Parses(string text, double seconds)
    {
        Assert.True(TimeInput.TryParse(text, out var value));
        Assert.Equal(seconds, value.TotalSeconds, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:60")]
    [InlineData("1.5:00")]
    [InlineData("-5")]
    [InlineData("1:2:3:4")]
    [InlineData("1::2")]
    [InlineData(null)]
    public void Rejects(string? text)
    {
        Assert.False(TimeInput.TryParse(text, out _));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(83.5, "1:23.5")]
    [InlineData(3723, "1:02:03")]
    [InlineData(59.96, "1:00")]
    public void Formats_RoundTrip(double seconds, string expected)
    {
        var text = TimeInput.Format(TimeSpan.FromSeconds(seconds));
        Assert.Equal(expected, text);
        Assert.True(TimeInput.TryParse(text, out _));
    }
}

public class TrimRequestTests
{
    private static string? Check(double start, double end, double duration = 60) =>
        TrimRequest.Validate(TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), duration);

    [Fact]
    public void ValidRange() => Assert.Null(Check(10, 20));

    [Fact]
    public void EndBeforeStart() => Assert.Equal("Конец должен быть позже начала.", Check(20, 10));

    [Fact]
    public void TooShort() => Assert.Equal("Слишком короткий отрезок.", Check(10, 10.05));

    [Fact]
    public void PastTheEnd() => Assert.Equal("Конец позже конца файла (1:00).", Check(10, 61));

    [Fact]
    public void WholeFile_IsNothingToTrim() => Assert.Equal("Выбран весь файл — обрезать нечего.", Check(0, 60));

    [Fact]
    public void RoundedDuration_IsTolerated() => Assert.Null(Check(5, 60.04));
}

public class TrimSpecTests
{
    private static readonly ProbeInfo Clip = new(60, true, true, 1280, 720, 2, 48000, "h264", "aac");

    private static List<string> Args(string path, bool precise, out IReadOnlyList<string> input)
    {
        var spec = TrimSpecs.For(path, new TrimRequest(TimeSpan.FromSeconds(12.5), TimeSpan.FromSeconds(20), precise));
        input = spec.InputArguments(path, Clip);
        return spec.Arguments(path, Clip).ToList();
    }

    [Fact]
    public void SeeksBeforeInput_AndLimitsLength()
    {
        var args = Args(@"C:\v.mp4", precise: true, out var input);
        Assert.Equal(["-ss", "12.5"], input);
        Assert.Equal("7.5", args[args.IndexOf("-t") + 1]);
    }

    [Fact]
    public void Precise_Reencodes_Fast_Copies()
    {
        var precise = Args(@"C:\v.mp4", precise: true, out _);
        var fast = Args(@"C:\v.mp4", precise: false, out _);
        Assert.Equal("libx264", precise[precise.IndexOf("-c:v") + 1]);
        Assert.Equal("copy", fast[fast.IndexOf("-c") + 1]);
    }

    [Fact]
    public void Webm_PreciseUsesVp9()
    {
        var args = Args(@"C:\v.webm", precise: true, out _);
        Assert.Equal("libvpx-vp9", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("webm", args[args.IndexOf("-f") + 1]);
    }

    [Fact]
    public void Audio_KeepsFormat()
    {
        var spec = TrimSpecs.For(@"C:\a.flac", new TrimRequest(TimeSpan.Zero, TimeSpan.FromSeconds(5), false));
        var args = spec.Arguments(@"C:\a.flac", Clip).ToList();
        Assert.Equal("flac", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal(".flac", spec.OutputExtension(@"C:\a.flac", Clip));
        Assert.Equal("_обрезка", spec.Suffix);
        Assert.Equal(5, spec.OutputDuration!(Clip));
    }

    [Fact]
    public void TrimNeedsInput_AndIsImplemented()
    {
        Assert.True(FileJobs.NeedsTrimInput(ActionIds.Trim));
        Assert.True(FileJobs.NeedsTrimInput(ActionIds.AudioTrim));
        Assert.True(FileJobs.IsImplemented(ActionIds.Trim));
        Assert.False(FileJobs.NeedsTrimInput(ActionIds.Gif));
    }
}

public class NewFormatSpecTests
{
    private static ProbeInfo Media(string? video, string? audio) => new(60, true, audio is not null, 1920, 1080, 2, 48000, video, audio);

    [Theory]
    [InlineData("vp9", "opus", true)]
    [InlineData("av1", "vorbis", true)]
    [InlineData("h264", "aac", false)]
    public void Webm_CopiesOnlyWebCodecs(string video, string audio, bool copy)
    {
        var args = ConversionSpecs.ToWebm(Media(video, audio));
        Assert.Equal(copy, args.Contains("copy") && !args.Contains("libvpx-vp9"));
    }

    [Fact]
    public void Mov_CopiesHevc()
    {
        var args = ConversionSpecs.ToMov(Media("hevc", "aac")).ToList();
        Assert.Equal("copy", args[args.IndexOf("-c") + 1]);
    }

    [Fact]
    public void Mkv_AlwaysCopies()
    {
        Assert.Contains("copy", ConversionSpecs.ToMkv(Media("mpeg4", "pcm_s16le")));
    }

    [Fact]
    public void AllMenuFormats_HaveSpecs()
    {
        foreach (var (_, ext) in ActionCatalog.ImageFormats)
            Assert.NotNull(ConversionSpecs.For(ActionIds.ImageTo(ext)));
        foreach (var (_, ext) in ActionCatalog.AudioFormats)
            Assert.NotNull(ConversionSpecs.For(ActionIds.AudioTo(ext)));
        foreach (var (_, ext) in ActionCatalog.VideoFormats)
            Assert.NotNull(ConversionSpecs.For(ActionIds.VideoTo(ext)));
        Assert.Null(ConversionSpecs.For(ActionIds.ImageTo(".psd")));
    }

    [Fact]
    public void GroupsThemselvesAreNotRunnable()
    {
        Assert.False(FileJobs.IsImplemented(ActionIds.Convert));
        Assert.False(FileJobs.IsImplemented(ActionIds.ImageConvert));
        Assert.False(FileJobs.IsImplemented(ActionIds.AudioConvert));
    }
}
