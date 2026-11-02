using FileDial.Core.Compression;

namespace FileDial.Core.Tests;

public class OutputFolderCheckTests
{
    [Fact]
    public void WritableFolder_WithSpace_IsFine_AndLeavesNoFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "папка с пробелом");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(OutputFolderCheck.WhyNot(dir, 25_000_000));
            Assert.Empty(Directory.GetFileSystemEntries(dir));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public void MissingFolder_IsReported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "нет");
        Assert.StartsWith("Нет прав на запись", OutputFolderCheck.WhyNot(dir, 25_000_000));
    }

    [Fact]
    public void NotEnoughSpace_IsReported()
    {
        // Цель больше любого реального диска — проверка места должна сработать.
        var message = OutputFolderCheck.WhyNot(Path.GetTempPath(), long.MaxValue / 4);
        Assert.StartsWith("На диске мало места", message);
    }

    [Fact]
    public void RequiredSpace_HasTwentyPercentMargin()
    {
        Assert.Equal(30_000_000, OutputFolderCheck.RequiredFreeBytes(25_000_000));
    }
}

public class AudioChannelTests
{
    [Fact]
    public void Probe_ReadsChannelsOfFirstAudioStream()
    {
        const string json = """
        { "streams": [ { "codec_type": "video", "width": 1920, "height": 1080 },
                       { "codec_type": "audio", "channels": 6 },
                       { "codec_type": "audio", "channels": 2 } ],
          "format": { "duration": "10" } }
        """;
        Assert.Equal(6, ProbeInfo.Parse(json).AudioChannels);
    }

    [Fact]
    public void SecondPass_DownmixOnlyWhenAsked()
    {
        var plan = new CompressionPlan(1000, 872, 128, null);
        var plain = FfmpegArguments.SecondPass("in.mp4", plan, "log", "out.tmp").ToList();
        var downmix = FfmpegArguments.SecondPass("in.mp4", plan, "log", "out.tmp", downmixToStereo: true).ToList();

        Assert.DoesNotContain("-ac", plain);
        Assert.Equal("2", downmix[downmix.IndexOf("-ac") + 1]);
    }

    [Fact]
    public void NoAudio_IgnoresDownmix()
    {
        var plan = new CompressionPlan(1000, 1000, 0, null);
        Assert.DoesNotContain("-ac", FfmpegArguments.SecondPass("in.mp4", plan, "log", "out.tmp", downmixToStereo: true));
    }
}
