using System.Windows.Media;

namespace FileDial.App.UI;

internal static class Palette
{
    public static readonly SolidColorBrush Background = Brush(0x15, 0x16, 0x1A);
    public static readonly SolidColorBrush Sector = Brush(0x2B, 0x2D, 0x34);
    public static readonly SolidColorBrush SectorDisabledHover = Brush(0x34, 0x36, 0x3E);
    public static readonly SolidColorBrush Accent = Brush(0xC8, 0xF2, 0x50);
    public static readonly SolidColorBrush AccentText = Brush(0x15, 0x16, 0x1A);
    public static readonly SolidColorBrush Text = Brush(0xF2, 0xF2, 0xEE);
    public static readonly SolidColorBrush TextMuted = Brush(0x8A, 0x8C, 0x93);

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
