namespace FileDial.Core.Geometry;

/// <summary>
/// Внешнее кольцо с вложенными секторами: веер вокруг оси родительского сектора, снаружи основного круга.
/// Углы — как в <see cref="SectorGeometry"/>: 0° вверх, по часовой стрелке.
/// </summary>
public sealed class SubmenuGeometry
{
    public SubmenuGeometry(double parentAngle, int count, double innerRadius, double outerRadius,
        double sweepDegrees, double gapDegrees = 0)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        if (outerRadius <= innerRadius) throw new ArgumentOutOfRangeException(nameof(outerRadius));
        if (sweepDegrees * count > 360) throw new ArgumentOutOfRangeException(nameof(sweepDegrees));

        ParentAngle = parentAngle;
        Count = count;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
        SweepDegrees = sweepDegrees;
        GapDegrees = gapDegrees;
    }

    public double ParentAngle { get; }
    public int Count { get; }
    public double InnerRadius { get; }
    public double OuterRadius { get; }
    public double SweepDegrees { get; }
    public double GapDegrees { get; }

    /// <summary>Начало веера: веер центрирован на оси родителя.</summary>
    public double FanStart => ParentAngle - SweepDegrees * Count / 2;

    public double FanEnd => FanStart + SweepDegrees * Count;

    public double CenterAngle(int index) => FanStart + SweepDegrees * (index + 0.5);

    public (double Start, double End) DrawnAngles(int index)
    {
        var center = CenterAngle(index);
        var half = (SweepDegrees - GapDegrees) / 2;
        return (center - half, center + half);
    }

    public PointD LabelPoint(int index) =>
        SectorGeometry.PointAt(CenterAngle(index), (InnerRadius + OuterRadius) / 2);

    /// <summary>
    /// Вложенный сектор под точкой. Точки между основным кругом и кольцом (от <paramref name="bridgeRadius"/>)
    /// тоже считаются кольцом — чтобы при движении мыши наружу веер не закрывался в зазоре.
    /// </summary>
    public int? HitTest(double dx, double dy, double bridgeRadius)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < bridgeRadius || distance > OuterRadius)
            return null;

        var offset = Normalize(SectorGeometry.AngleOf(dx, dy) - FanStart);
        var span = SweepDegrees * Count;
        if (offset >= span)
            return null;
        return Math.Min((int)(offset / SweepDegrees), Count - 1);
    }

    private static double Normalize(double degrees)
    {
        degrees %= 360;
        return degrees < 0 ? degrees + 360 : degrees;
    }
}
