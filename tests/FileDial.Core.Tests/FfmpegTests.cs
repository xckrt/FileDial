using FileDial.Core.Compression;
using FileDial.Core.Text;

namespace FileDial.Core.Tests;

public class FfmpegProgressParserTests
{
    [Theory]
    [InlineData("out_time_us=1500000", 1.5)]
    [InlineData("out_time_ms=2500000", 2.5)] // в ffmpeg out_time_ms тоже в микросекундах
    [InlineData("out_time=00:01:02.500000", 62.5)]
    public void ParsesOutTime(string line, double seconds)
    {
        Assert.True(FfmpegProgressParser.TryParseOutTime(line, out var t));
        Assert.Equal(seconds, t.TotalSeconds, 6);
    }

    [Theory]
    [InlineData("out_time_us=N/A")]
    [InlineData("out_time_us=-9223372036854775807")]
    [InlineData("frame=120")]
    [InlineData("progress=continue")]
    [InlineData("")]
    [InlineData(null)]
    public void IgnoresOtherLines(string? line)
    {
        Assert.False(FfmpegProgressParser.TryParseOutTime(line, out _));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(1, 5, 0.25)]
    [InlineData(1, 10, 0.5)]
    [InlineData(2, 0, 0.5)]
    [InlineData(2, 5, 0.75)]
    [InlineData(2, 20, 1)] // выход за длительность ограничен
    public void OverallFraction_SplitsTwoPasses(int pass, double position, double expected)
    {
        Assert.Equal(expected, FfmpegProgressParser.OverallFraction(pass, TimeSpan.FromSeconds(position), 10), 6);
    }
}

public class ProbeInfoTests
{
    [Fact]
    public void ParsesDurationStreamsAndSize()
    {
        const string json = """
        {
          "programs": [],
          "streams": [
            { "codec_type": "video", "width": 1920, "height": 1080 },
            { "codec_type": "audio" },
            { "codec_type": "video", "width": 320, "height": 240 }
          ],
          "format": { "duration": "61.250000" }
        }
        """;
        var info = ProbeInfo.Parse(json);
        Assert.Equal(new ProbeInfo(61.25, true, true, 1920, 1080), info);
    }

    [Fact]
    public void MissingDataGivesZeros()
    {
        var info = ProbeInfo.Parse("""{ "streams": [ { "codec_type": "audio" } ], "format": {} }""");
        Assert.Equal(new ProbeInfo(0, false, true, 0, 0), info);
    }
}

public class FfmpegArgumentsTests
{
    private const string Input = @"D:\Мои видео\файл с пробелами.mp4";
    private const string Output = @"D:\Мои видео\.filedial-1.tmp";
    private const string PassLog = @"C:\Temp\FileDial\x\pass";

    [Fact]
    public void PathsArePassedAsSingleArguments()
    {
        var plan = new CompressionPlan(1000, 872, 128, null);
        var second = FfmpegArguments.SecondPass(Input, plan, PassLog, Output);

        Assert.Contains(Input, second);
        Assert.Equal(Output, second[^1]);
        Assert.Equal(Input, second[second.ToList().IndexOf("-i") + 1]);
        Assert.Contains("pipe:1", second);
    }

    [Fact]
    public void FirstPass_HasNoAudioAndNullOutput()
    {
        var plan = new CompressionPlan(1000, 872, 128, null);
        var first = FfmpegArguments.FirstPass(Input, plan, PassLog).ToList();

        Assert.Equal("1", first[first.IndexOf("-pass") + 1]);
        Assert.Equal("872k", first[first.IndexOf("-b:v") + 1]);
        Assert.Contains("-an", first);
        Assert.Equal(["-f", "null", "-"], first.TakeLast(3));
        Assert.DoesNotContain("-c:a", first);
    }

    [Fact]
    public void SecondPass_UsesH264AndAac128()
    {
        var plan = new CompressionPlan(1000, 872, 128, null);
        var second = FfmpegArguments.SecondPass(Input, plan, PassLog, Output).ToList();

        Assert.Equal("2", second[second.IndexOf("-pass") + 1]);
        Assert.Equal("libx264", second[second.IndexOf("-c:v") + 1]);
        Assert.Equal("aac", second[second.IndexOf("-c:a") + 1]);
        Assert.Equal("128k", second[second.IndexOf("-b:a") + 1]);
        Assert.Equal(PassLog, second[second.IndexOf("-passlogfile") + 1]);
        Assert.DoesNotContain("-vf", second);
    }

    [Fact]
    public void SecondPass_WithoutAudio()
    {
        var plan = new CompressionPlan(1000, 1000, 0, null);
        var second = FfmpegArguments.SecondPass(Input, plan, PassLog, Output);
        Assert.Contains("-an", second);
        Assert.DoesNotContain("-c:a", second);
    }

    [Fact]
    public void Scaling_AddsFilterInBothPasses()
    {
        var plan = new CompressionPlan(400, 272, 128, 854);
        var first = FfmpegArguments.FirstPass(Input, plan, PassLog).ToList();
        var second = FfmpegArguments.SecondPass(Input, plan, PassLog, Output).ToList();

        var filter = "scale='if(gte(iw,ih),854,-2)':'if(gte(iw,ih),-2,854)'";
        Assert.Equal(filter, first[first.IndexOf("-vf") + 1]);
        Assert.Equal(filter, second[second.IndexOf("-vf") + 1]);
    }
}

public class FfmpegLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string MakeDir(string name, params string[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "");
        return dir;
    }

    [Fact]
    public void PrefersAppDirectory()
    {
        var app = MakeDir("app", "ffmpeg.exe", "ffprobe.exe");
        var path = MakeDir("path", "ffmpeg.exe", "ffprobe.exe");

        var tools = FfmpegLocator.Locate(app, path);
        Assert.Equal(Path.Combine(app, "ffmpeg.exe"), tools!.Ffmpeg);
    }

    [Fact]
    public void FallsBackToPath_AndRequiresBothInOneFolder()
    {
        var app = MakeDir("app", "ffmpeg.exe");
        var half = MakeDir("half", "ffprobe.exe");
        var full = MakeDir("полная папка", "ffmpeg.exe", "ffprobe.exe");

        var tools = FfmpegLocator.Locate(app, string.Join(Path.PathSeparator, half, "\"" + full + "\"", "<>|bad"));
        Assert.Equal(Path.Combine(full, "ffprobe.exe"), tools!.Ffprobe);
    }

    [Fact]
    public void ReturnsNullWhenMissing()
    {
        var app = MakeDir("app");
        Assert.Null(FfmpegLocator.Locate(app, null));
    }
}

public class SizeFormatterTests
{
    [Theory]
    [InlineData(512, "512 Б")]
    [InlineData(1_500, "1,5 КБ")]
    [InlineData(25_000_000, "25 МБ")]
    [InlineData(24_123_456, "24,1 МБ")]
    [InlineData(1_250_000_000, "1,25 ГБ")]
    public void FormatsDecimalUnits(long bytes, string expected)
    {
        Assert.Equal(expected, SizeFormatter.Format(bytes));
    }
}
