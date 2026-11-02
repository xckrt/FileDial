using FileDial.Core.Compression;

namespace FileDial.Core.Tests;

public class BitrateCalculatorTests
{
    [Fact]
    public void TotalKbps_UsesTargetAndThreePercentMargin()
    {
        // 25 000 000 байт * 8 * 0.97 / 60 с = 3 233 333 бит/с
        Assert.Equal(3233, BitrateCalculator.TotalKbps(60));
    }

    [Fact]
    public void Plan_SubtractsAudioFromTotal()
    {
        var plan = BitrateCalculator.Plan(60, hasAudio: true, 1920, 1080);

        Assert.Equal(3233, plan.TotalKbps);
        Assert.Equal(128, plan.AudioKbps);
        Assert.Equal(3233 - 128, plan.VideoKbps);
        Assert.Null(plan.ScaleLongSide);
        Assert.True(plan.IsFeasible);
    }

    [Fact]
    public void Plan_WithoutAudio_GivesEverythingToVideo()
    {
        var plan = BitrateCalculator.Plan(60, hasAudio: false, 1920, 1080);

        Assert.Equal(0, plan.AudioKbps);
        Assert.Equal(plan.TotalKbps, plan.VideoKbps);
    }

    [Fact]
    public void Plan_EstimatedSizeFitsTarget()
    {
        foreach (var duration in new[] { 5.0, 37.5, 61.2, 300, 900 })
        {
            var plan = BitrateCalculator.Plan(duration, true, 1280, 720);
            var bytes = plan.TotalKbps * 1000.0 / 8 * duration;
            Assert.True(bytes <= BitrateCalculator.DefaultTargetBytes * 0.97 + 1, $"{duration} с → {bytes} байт");
        }
    }

    [Theory]
    [InlineData(453, 300, null)] // ровно на пороге — не уменьшаем
    [InlineData(454, 299, 854)]  // чуть ниже — 480p
    [InlineData(700, 149, 640)]  // 360p
    [InlineData(1000, 66, 426)]  // 240p
    public void Plan_LowVideoBitrate_ReducesResolution(double duration, int expectedVideo, int? expectedLongSide)
    {
        var plan = BitrateCalculator.Plan(duration, true, 1920, 1080);

        Assert.Equal(expectedVideo, plan.VideoKbps);
        Assert.Equal(expectedLongSide, plan.ScaleLongSide);
    }

    [Fact]
    public void Plan_PortraitVideo_UsesLongSide()
    {
        var plan = BitrateCalculator.Plan(454, true, 1080, 1920);
        Assert.Equal(854, plan.ScaleLongSide);
    }

    [Fact]
    public void Plan_SmallSource_IsNotUpscaled()
    {
        var plan = BitrateCalculator.Plan(1000, true, 320, 240);
        Assert.Null(plan.ScaleLongSide);
    }

    [Fact]
    public void Plan_TooLong_IsNotFeasible()
    {
        var plan = BitrateCalculator.Plan(2000, true, 1920, 1080);
        Assert.False(plan.IsFeasible);
    }

    [Fact]
    public void Plan_CustomTarget()
    {
        Assert.Equal(776, BitrateCalculator.TotalKbps(10, 1_000_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void TotalKbps_InvalidDuration_Throws(double duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BitrateCalculator.TotalKbps(duration));
    }
}
