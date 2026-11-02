namespace FileDial.Core.Geometry;

/// <summary>Прямоугольник в физических пикселях экрана.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>Размещение окна у курсора в пределах рабочей области монитора.</summary>
public static class ScreenPlacement
{
    public static int ToPixels(double dips, double dpi) => (int)Math.Round(dips * dpi / 96.0);

    /// <summary>Центрирует окно на курсоре и сдвигает внутрь рабочей области.</summary>
    public static PixelRect CenterOn(int cursorX, int cursorY, int width, int height, PixelRect workArea) =>
        Clamp(cursorX - width / 2, cursorY - height / 2, width, height, workArea);

    /// <summary>Ставит окно правее и ниже курсора, а у края экрана — с другой стороны.</summary>
    public static PixelRect NearCursor(int cursorX, int cursorY, int width, int height, int offset, PixelRect workArea)
    {
        var left = cursorX + offset;
        if (left + width > workArea.Right)
            left = cursorX - offset - width;
        var top = cursorY + offset;
        if (top + height > workArea.Bottom)
            top = cursorY - offset - height;
        return Clamp(left, top, width, height, workArea);
    }

    public static PixelRect Clamp(int left, int top, int width, int height, PixelRect workArea)
    {
        // Если окно больше рабочей области, прижимаем к левому/верхнему краю.
        left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - width));
        top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - height));
        return new PixelRect(left, top, width, height);
    }
}
