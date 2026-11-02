using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

public class VideoActionRulesTests
{
    private static DialAction Get(string path, ProbeInfo? media, string id) =>
        ActionCatalog.ForFile(path, 80_000_000, media).Single(a => a.Id == id);

    private static ProbeInfo Clip(double seconds, bool audio) => new(seconds, true, audio, 1920, 1080);

    [Fact]
    public void SilentVideo_AudioActionsDisabled()
    {
        Assert.Equal("В видео нет звука", Get(@"C:\v.mp4", Clip(10, false), ActionIds.ExtractAudio).DisabledReason);
        Assert.Equal("В видео и так нет звука", Get(@"C:\v.mp4", Clip(10, false), ActionIds.Mute).DisabledReason);
    }

    [Fact]
    public void Gif_OnlyForShortClips()
    {
        Assert.True(Get(@"C:\v.mov", Clip(30, true), ActionIds.Gif).IsEnabled);
        Assert.Equal("GIF — только для роликов до 30 с", Get(@"C:\v.mov", Clip(31, true), ActionIds.Gif).DisabledReason);
    }

    [Theory]
    [InlineData(@"C:\v.mp4", false)]
    [InlineData(@"C:\v.MP4", false)]
    [InlineData(@"C:\v.mkv", true)]
    [InlineData(@"C:\v.webm", true)]
    public void Convert_ToMp4DisabledForMp4(string path, bool enabled)
    {
        var group = Get(path, null, ActionIds.Convert);
        Assert.True(group.IsGroup);
        Assert.Equal(["MP4", "MKV", "WebM", "MOV"], group.Children!.Select(c => c.Label));
        Assert.Equal(enabled, group.Children!.Single(c => c.Id == ActionIds.ToMp4).IsEnabled);
    }

    [Fact]
    public void BigImage_CanBeCompressedTo1Mb()
    {
        var action = ActionCatalog.ForFile(@"C:\p.png", 1_000_001).Single(a => a.Id == ActionIds.ImageCompress1Mb);
        Assert.True(action.IsEnabled);
    }
}

public class VideoSpecTests
{
    private static ProbeInfo Media(string? video, string? audio, bool hasAudio = true) =>
        new(60, true, hasAudio, 1920, 1080, 2, 48000, video, audio);

    [Fact]
    public void ToMp4_H264Aac_IsRemuxedWithoutReencoding()
    {
        var args = ConversionSpecs.ToMp4(Media("h264", "aac")).ToList();
        Assert.Equal("copy", args[args.IndexOf("-c") + 1]);
        Assert.DoesNotContain("libx264", args);
    }

    [Fact]
    public void ToMp4_Vp9Opus_IsReencoded()
    {
        var args = ConversionSpecs.ToMp4(Media("vp9", "opus")).ToList();
        Assert.Equal("libx264", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void ToMp4_H264WithOpus_CopiesVideoReencodesAudio()
    {
        var args = ConversionSpecs.ToMp4(Media("h264", "opus")).ToList();
        Assert.Equal("copy", args[args.IndexOf("-c:v") + 1]);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void ToMp4_SilentH264_IsCopied()
    {
        var args = ConversionSpecs.ToMp4(Media("h264", null, hasAudio: false));
        Assert.Contains("copy", args);
        Assert.Contains("0:a:0?", args); // необязательная дорожка не ломает файл без звука
    }

    [Theory]
    [InlineData("aac", ".m4a")]
    [InlineData("mp3", ".mp3")]
    [InlineData("opus", ".opus")]
    [InlineData("vorbis", ".ogg")]
    [InlineData("pcm_s16le", ".m4a")]
    [InlineData(null, ".m4a")]
    public void ExtractAudio_PicksContainerByCodec(string? codec, string expected)
    {
        Assert.Equal(expected, ConversionSpecs.ExtractedAudioExtension(Media("h264", codec)));
    }

    [Fact]
    public void ExtractAudio_CopiesAac_ReencodesPcm()
    {
        var spec = ConversionSpecs.For(ActionIds.ExtractAudio)!;
        var copy = spec.Arguments(@"C:\v.mp4", Media("h264", "aac")).ToList();
        var encode = spec.Arguments(@"C:\v.mov", Media("h264", "pcm_s16le")).ToList();
        Assert.Equal("copy", copy[copy.IndexOf("-c:a") + 1]);
        Assert.Equal("aac", encode[encode.IndexOf("-c:a") + 1]);
    }

    [Theory]
    [InlineData(".mp4", "mp4")]
    [InlineData(".MOV", "mov")]
    [InlineData(".mkv", "matroska")]
    [InlineData(".webm", "webm")]
    public void Mute_CopiesVideoIntoSameContainer(string ext, string muxer)
    {
        var args = ConversionSpecs.For(ActionIds.Mute)!.Arguments(@"C:\v" + ext, Media("h264", "aac")).ToList();
        Assert.Equal("copy", args[args.IndexOf("-c:v") + 1]);
        Assert.Contains("-an", args);
        Assert.Equal(muxer, args[args.IndexOf("-f") + 1]);
    }

    [Fact]
    public void Frame_SeeksToThirdOfDuration()
    {
        var spec = ConversionSpecs.For(ActionIds.Frame)!;
        Assert.Equal(["-ss", "20"], spec.InputArguments(@"C:\v.mp4", Media("h264", "aac")));
        Assert.Empty(spec.InputArguments(@"C:\v.mp4", new ProbeInfo(0, true, false, 10, 10)));
        Assert.Equal(".png", spec.OutputExtension(@"C:\v.mp4", Media("h264", "aac")));
    }

    [Fact]
    public void Gif_ScalesOnlyLargeVideo()
    {
        Assert.Contains("480", ConversionSpecs.GifFilter(Media("h264", "aac")));
        Assert.DoesNotContain(",scale='", ConversionSpecs.GifFilter(new ProbeInfo(5, true, false, 320, 240)));
        Assert.Contains("palettegen", ConversionSpecs.GifFilter(new ProbeInfo(5, true, false, 320, 240)));
    }
}

public class QualitySearchTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(16)]
    [InlineData(31)]
    public async Task FindsSmallestFittingValue(int threshold)
    {
        var calls = 0;
        var result = await QualitySearch.SmallestFittingAsync(2, 31, q =>
        {
            calls++;
            return Task.FromResult(q >= threshold);
        });
        Assert.Equal(threshold, result);
        Assert.True(calls <= 5, $"{calls} вызовов — поиск не двоичный");
    }
}
