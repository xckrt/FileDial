using FileDial.Core.Jobs;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

public class ImageResizeTests
{
    [Theory]
    [InlineData(4032, 3024, 1920, 1920, 1440)]
    [InlineData(3024, 4032, 1920, 1440, 1920)] // портрет: длинная сторона — высота
    [InlineData(1000, 1000, 640, 640, 640)]
    [InlineData(800, 600, 1920, 800, 600)] // меньше цели — не растягиваем
    public void LongSide(int w, int h, int side, int ew, int eh) =>
        Assert.Equal((ew, eh), new ResizeRequest(ResizeBy.LongSide, side).Target(w, h));

    [Theory]
    [InlineData(1000, 600, 50, 500, 300)]
    [InlineData(1001, 601, 33, 330, 198)]
    [InlineData(3, 2, 25, 1, 1)] // не меньше 1 px
    public void Percent(int w, int h, int percent, int ew, int eh) =>
        Assert.Equal((ew, eh), new ResizeRequest(ResizeBy.Percent, percent).Target(w, h));

    [Fact]
    public void Custom_KeepAspect_FitsIntoBox_NeverUpscales()
    {
        var box = new ResizeRequest(ResizeBy.Exact, 0, 800, 800);
        Assert.Equal((800, 600), box.Target(4000, 3000));
        Assert.Equal((450, 800), box.Target(900, 1600));
        Assert.Equal((300, 200), box.Target(300, 200));
        Assert.False(box.Shrinks(300, 200));
    }

    [Fact]
    public void Custom_Exact_IgnoresAspect_ButNotBiggerThanSource()
    {
        var exact = new ResizeRequest(ResizeBy.Exact, 0, 500, 500, KeepAspect: false);
        Assert.Equal((500, 500), exact.Target(4000, 3000));
        Assert.Equal((300, 500), exact.Target(300, 2000)); // ширину не растягиваем
        Assert.True(exact.Shrinks(300, 2000));
    }

    [Theory]
    [InlineData("long:1920", ResizeBy.LongSide, "_1920px")]
    [InlineData("pct:50", ResizeBy.Percent, "_50%")]
    [InlineData("box:800x600", ResizeBy.Exact, "_800x600")]
    [InlineData("exact:640x480", ResizeBy.Exact, "_640x480")]
    public void Parse_Format_RoundTrip(string text, ResizeBy mode, string suffix)
    {
        var request = ResizeRequest.Parse(text);
        Assert.Equal(mode, request.Mode);
        Assert.Equal(suffix, request.Suffix);
        Assert.Equal(text, request.Format());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("long:0")]
    [InlineData("pct:100")]
    [InlineData("box:0x10")]
    [InlineData("что-то")]
    public void Parse_Garbage_GivesDefault(string? text) =>
        Assert.Equal(ResizeRequest.Default, ResizeRequest.Parse(text));

    [Fact]
    public void Settings_RememberValidChoice_NormalizeGarbage()
    {
        Assert.Equal("pct:25", new AppSettings { Resize = "pct:25" }.Normalized().Resize);
        Assert.Equal("long:1920", new AppSettings { Resize = "long:-5" }.Normalized().Resize);
    }
}
