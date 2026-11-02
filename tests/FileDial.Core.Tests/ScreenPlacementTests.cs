using FileDial.Core.Geometry;

namespace FileDial.Core.Tests;

public class ScreenPlacementTests
{
    private static readonly PixelRect Primary = new(0, 0, 1920, 1040);
    // Второй монитор слева от основного с отрицательными координатами.
    private static readonly PixelRect LeftMonitor = new(-2560, -200, 2560, 1400);

    [Fact]
    public void CenterOn_CentersOnCursor()
    {
        var r = ScreenPlacement.CenterOn(960, 500, 400, 400, Primary);
        Assert.Equal(new PixelRect(760, 300, 400, 400), r);
    }

    [Fact]
    public void CenterOn_ClampsToTopLeftCorner()
    {
        var r = ScreenPlacement.CenterOn(10, 10, 400, 400, Primary);
        Assert.Equal(new PixelRect(0, 0, 400, 400), r);
    }

    [Fact]
    public void CenterOn_ClampsToBottomRightCorner()
    {
        var r = ScreenPlacement.CenterOn(1915, 1035, 400, 400, Primary);
        Assert.Equal(new PixelRect(1520, 640, 400, 400), r);
    }

    [Fact]
    public void CenterOn_WorksWithNegativeMonitorCoordinates()
    {
        var r = ScreenPlacement.CenterOn(-2555, -190, 600, 600, LeftMonitor);
        Assert.Equal(new PixelRect(-2560, -200, 600, 600), r);

        r = ScreenPlacement.CenterOn(-5, 1195, 600, 600, LeftMonitor);
        Assert.Equal(new PixelRect(-600, 600, 600, 600), r);
    }

    [Fact]
    public void NearCursor_FlipsToOtherSideAtEdge()
    {
        var normal = ScreenPlacement.NearCursor(100, 100, 200, 50, 16, Primary);
        Assert.Equal(new PixelRect(116, 116, 200, 50), normal);

        var flipped = ScreenPlacement.NearCursor(1900, 1030, 200, 50, 16, Primary);
        Assert.Equal(new PixelRect(1684, 964, 200, 50), flipped);
    }

    [Theory]
    [InlineData(380, 96, 380)]
    [InlineData(380, 120, 475)]
    [InlineData(380, 144, 570)]
    [InlineData(380, 192, 760)]
    public void ToPixels_ScalesByDpi(double dips, double dpi, int expected)
    {
        Assert.Equal(expected, ScreenPlacement.ToPixels(dips, dpi));
    }
}
