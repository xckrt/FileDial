namespace FileDial.Core.Geometry;

public readonly record struct PointD(double X, double Y);

/// <summary>
/// Геометрия кругового меню в координатах относительно центра (ось Y вниз, как на экране).
/// Угол 0° — вверх, отсчёт по часовой стрелке. Сектор 0 центрирован сверху.
/// </summary>
public sealed class SectorGeometry
{
    public SectorGeometry(int sectorCount, double innerRadius, double outerRadius, double gapDegrees = 0)
    {
        if (sectorCount < 1) throw new ArgumentOutOfRangeException(nameof(sectorCount));
        if (innerRadius < 0 || outerRadius <= innerRadius) throw new ArgumentOutOfRangeException(nameof(outerRadius));
        if (gapDegrees < 0 || gapDegrees >= 360.0 / sectorCount) throw new ArgumentOutOfRangeException(nameof(gapDegrees));

        SectorCount = sectorCount;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
        GapDegrees = gapDegrees;
    }

    public int SectorCount { get; }
    public double InnerRadius { get; }
    public double OuterRadius { get; }
    public double GapDegrees { get; }
    public double SweepDegrees => 360.0 / SectorCount;

    public double CenterAngle(int index) => index * SweepDegrees;

    /// <summary>Видимые границы сектора с учётом зазора; начало может быть отрицательным.</summary>
    public (double Start, double End) DrawnAngles(int index)
    {
        var center = CenterAngle(index);
        var half = (SweepDegrees - GapDegrees) / 2;
        return (center - half, center + half);
    }

    public static PointD PointAt(double angleDegrees, double radius)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        return new PointD(Math.Sin(rad) * radius, -Math.Cos(rad) * radius);
    }

    /// <summary>Угол точки (0..360) по тем же правилам.</summary>
    public static double AngleOf(double dx, double dy)
    {
        var deg = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }

    /// <summary>Индекс сектора под точкой или null, если точка в центре или за кольцом.
    /// Зазоры между секторами относятся к ближайшему сектору, чтобы не было «мёртвых» щелей.</summary>
    public int? HitTest(double dx, double dy)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < InnerRadius || distance > OuterRadius)
            return null;

        var shifted = AngleOf(dx, dy) + SweepDegrees / 2;
        return (int)Math.Floor(shifted / SweepDegrees) % SectorCount;
    }

    /// <summary>Точка для подписи: середина сектора по углу и по ширине кольца.</summary>
    public PointD LabelPoint(int index) =>
        PointAt(CenterAngle(index), (InnerRadius + OuterRadius) / 2);

    /// <summary>Клавиши 1..N соответствуют секторам 0..N-1.</summary>
    public int? IndexForDigit(int digit) =>
        digit >= 1 && digit <= SectorCount ? digit - 1 : null;
}
