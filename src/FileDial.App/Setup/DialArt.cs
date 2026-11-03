using System.Windows;
using System.Windows.Media;
using FileDial.App.UI;

namespace FileDial.App.Setup;

/// <summary>
/// Циферблат установщика — тот же круг, что и в программе: шкала делений и восемь секторов.
/// Ход установки заполняет его акцентом по часовой стрелке; до начала подсвечен один сектор, как при наведении.
/// </summary>
internal sealed class DialArt : FrameworkElement
{
    private const int Sectors = 8;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(DialArt),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(int), typeof(DialArt),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>0…1 — какая часть круга залита акцентом.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>Подсвеченный сектор (−1 — нет).</summary>
    public int Highlight
    {
        get => (int)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
            return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = size / 2;
        // Заливка идёт по часовой стрелке от левого края верхнего сектора: так секторы загораются целиком по очереди.
        const double origin = -22.5;
        var front = origin + Math.Clamp(Progress, 0, 1) * 360;

        dc.DrawEllipse(Palette.Background, new Pen(Palette.Rim, 1), center, r - 0.5, r - 0.5);

        // Шкала: деления через 5°, длинные — напротив середин секторов; пройденные — акцентом.
        var minorPen = Frozen(new Pen(Palette.Rim, 1));
        var majorPen = Frozen(new Pen(Palette.TextMuted, 1.4));
        var litPen = Frozen(new Pen(Palette.Accent, 1.6));
        for (var angle = -20; angle < 340; angle += 5)
        {
            var major = (angle + 360) % (360 / Sectors) == 0;
            var outer = r - 5;
            var inner = outer - (major ? 8 : 4.5);
            var lit = Progress > 0 && angle <= front;
            dc.DrawLine(lit ? litPen : major ? majorPen : minorPen, At(center, angle, outer), At(center, angle, inner));
        }

        // Секторы: подложка, затем заливка; сектор на фронте закрашивается частично — фронт движется плавно.
        var innerR = r * 0.42;
        var outerR = r * 0.80;
        const double gap = 1.5;
        for (var i = 0; i < Sectors; i++)
        {
            var start = origin + i * 45.0 + gap / 2;
            var end = start + 45 - gap;
            dc.DrawGeometry(i == Highlight ? Palette.Accent : Palette.Sector, null, Sector(center, start, end, innerR, outerR));
            var filled = Math.Min(end, front) - start;
            if (filled > 0.2)
                dc.DrawGeometry(Palette.Accent, null, Sector(center, start, start + filled, innerR, outerR));
        }

        dc.DrawEllipse(null, new Pen(Palette.Rim, 1), center, innerR - 8, innerR - 8);
    }

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    /// <summary>Точка на окружности: 0° — сверху, по часовой стрелке.</summary>
    private static Point At(Point c, double angle, double radius)
    {
        var rad = angle * Math.PI / 180;
        return new Point(c.X + radius * Math.Sin(rad), c.Y - radius * Math.Cos(rad));
    }

    private static Geometry Sector(Point c, double start, double end, double inner, double outer)
    {
        var large = end - start > 180;
        var figure = new PathFigure { StartPoint = At(c, start, inner), IsClosed = true };
        figure.Segments.Add(new LineSegment(At(c, start, outer), false));
        figure.Segments.Add(new ArcSegment(At(c, end, outer), new Size(outer, outer), 0, large, SweepDirection.Clockwise, false));
        figure.Segments.Add(new LineSegment(At(c, end, inner), false));
        figure.Segments.Add(new ArcSegment(At(c, start, inner), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, false));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
