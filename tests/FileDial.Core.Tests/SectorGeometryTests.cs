using FileDial.Core.Geometry;

namespace FileDial.Core.Tests;

public class SectorGeometryTests
{
    private readonly SectorGeometry _g = new(8, innerRadius: 70, outerRadius: 180, gapDegrees: 2);

    [Theory]
    [InlineData(0, -120, 0)]   // вверх
    [InlineData(85, -85, 1)]   // вправо-вверх
    [InlineData(120, 0, 2)]    // вправо
    [InlineData(85, 85, 3)]
    [InlineData(0, 120, 4)]    // вниз
    [InlineData(-85, 85, 5)]
    [InlineData(-120, 0, 6)]   // влево
    [InlineData(-85, -85, 7)]
    public void HitTest_DirectionsMapClockwiseFromTop(double dx, double dy, int expected)
    {
        Assert.Equal(expected, _g.HitTest(dx, dy));
    }

    [Theory]
    [InlineData(22.4, 0)]
    [InlineData(22.6, 1)]
    [InlineData(337.6, 0)] // переход через 0°
    [InlineData(337.4, 7)]
    [InlineData(359.9, 0)]
    public void HitTest_SectorBoundaries(double angle, int expected)
    {
        var p = SectorGeometry.PointAt(angle, 120);
        Assert.Equal(expected, _g.HitTest(p.X, p.Y));
    }

    [Theory]
    [InlineData(0, 0)]      // центр
    [InlineData(0, -69.9)]  // внутри внутреннего круга
    [InlineData(0, -180.1)] // за кольцом
    [InlineData(200, 200)]
    public void HitTest_OutsideRing_ReturnsNull(double dx, double dy)
    {
        Assert.Null(_g.HitTest(dx, dy));
    }

    [Fact]
    public void HitTest_RingEdgesAreInside()
    {
        Assert.Equal(0, _g.HitTest(0, -70));
        Assert.Equal(0, _g.HitTest(0, -180));
    }

    [Fact]
    public void DrawnAngles_LeaveGap()
    {
        var (start, end) = _g.DrawnAngles(1);
        Assert.Equal(23.5, start, 6);
        Assert.Equal(66.5, end, 6);

        var (start0, end0) = _g.DrawnAngles(0);
        Assert.Equal(-21.5, start0, 6);
        Assert.Equal(21.5, end0, 6);
    }

    [Fact]
    public void PointAt_UsesScreenCoordinates()
    {
        var up = SectorGeometry.PointAt(0, 10);
        var right = SectorGeometry.PointAt(90, 10);
        Assert.Equal(0, up.X, 6);
        Assert.Equal(-10, up.Y, 6);
        Assert.Equal(10, right.X, 6);
        Assert.Equal(0, right.Y, 6);
    }

    [Fact]
    public void LabelPoint_IsInMiddleOfSector()
    {
        var p = _g.LabelPoint(2);
        Assert.Equal(125, p.X, 6);
        Assert.Equal(0, p.Y, 6);
        Assert.Equal(2, _g.HitTest(p.X, p.Y));
    }

    [Fact]
    public void EveryLabelPointHitsItsOwnSector()
    {
        for (var i = 0; i < 8; i++)
        {
            var p = _g.LabelPoint(i);
            Assert.Equal(i, _g.HitTest(p.X, p.Y));
        }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(8, 7)]
    [InlineData(0, null)]
    [InlineData(9, null)]
    public void IndexForDigit(int digit, int? expected)
    {
        Assert.Equal(expected, _g.IndexForDigit(digit));
    }

    [Fact]
    public void Constructor_RejectsInvalidRadii()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SectorGeometry(8, 100, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SectorGeometry(8, 10, 50, gapDegrees: 45));
    }
}
