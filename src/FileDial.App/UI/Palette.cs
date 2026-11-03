using System.Windows;
using System.Windows.Media;
using FileDial.Core.Localization;
using Microsoft.Win32;

namespace FileDial.App.UI;

/// <summary>Цвета одной темы. Только плоские цвета, без градиентов.</summary>
internal sealed record Theme(
    string Id,
    Color Background,
    Color Sector,
    Color SectorHover,
    Color Accent,
    Color AccentText,
    Color Text,
    Color TextMuted,
    Color Rim)
{
    /// <summary>Название на текущем языке интерфейса.</summary>
    public string Name => Id switch
    {
        "brass" => Lang.T.UiThemeBrass,
        "paper" => Lang.T.UiThemePaper,
        _ => Lang.T.UiThemeGraphite,
    };

    private static Color C(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>Палитра из исходного брифа: графит и салатовый акцент.</summary>
    public static readonly Theme Graphite = new("graphite",
        C(0x15161A), C(0x2B2D34), C(0x383A42), C(0xC8F250), C(0x15161A), C(0xF2F2EE), C(0x8A8C93), C(0x3A3C44));

    /// <summary>Как старый измерительный прибор: тёмный орех и латунь.</summary>
    public static readonly Theme Brass = new("brass",
        C(0x1A1611), C(0x2A241C), C(0x3A3227), C(0xD9A646), C(0x1A1611), C(0xF3E9D7), C(0x9C8F7A), C(0x4A3F30));

    /// <summary>Светлая: прохладный серый и кобальт.</summary>
    public static readonly Theme Paper = new("paper",
        C(0xE6E8EC), C(0xF7F8FA), C(0xD9DCE2), C(0x2F5BD3), C(0xFFFFFF), C(0x1C1F26), C(0x6B7080), C(0xB9BDC7));

    public static readonly IReadOnlyList<Theme> All = [Graphite, Brass, Paper];

    /// <summary>«auto» — Графит или Бумага по светлой/тёмной теме приложений Windows.</summary>
    public static Theme Resolve(string id) => id switch
    {
        "graphite" => Graphite,
        "brass" => Brass,
        "paper" => Paper,
        _ => WindowsUsesLightTheme() ? Paper : Graphite,
    };

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

/// <summary>
/// Кисти текущей темы — для кода (круг, уведомления) и для XAML (ресурсы приложения с теми же именами).
/// Окна, открытые после смены темы, берут новые цвета.
/// </summary>
internal static class Palette
{
    public static Theme Current { get; private set; } = Theme.Graphite;

    public static SolidColorBrush Background { get; private set; } = Brush(Theme.Graphite.Background);
    public static SolidColorBrush Sector { get; private set; } = Brush(Theme.Graphite.Sector);
    public static SolidColorBrush SectorDisabledHover { get; private set; } = Brush(Theme.Graphite.SectorHover);
    public static SolidColorBrush Accent { get; private set; } = Brush(Theme.Graphite.Accent);
    public static SolidColorBrush AccentText { get; private set; } = Brush(Theme.Graphite.AccentText);
    public static SolidColorBrush Text { get; private set; } = Brush(Theme.Graphite.Text);
    public static SolidColorBrush TextMuted { get; private set; } = Brush(Theme.Graphite.TextMuted);
    public static SolidColorBrush Rim { get; private set; } = Brush(Theme.Graphite.Rim);

    /// <summary>Шрифт шкалы и подписей круга: Bahnschrift (DIN-подобный, есть в Windows 10+), узкое начертание.</summary>
    public static readonly FontFamily DialFont = new("Bahnschrift SemiCondensed, Bahnschrift, Segoe UI");

    /// <summary>Заголовки в окнах — тот же Bahnschrift, обычной ширины.</summary>
    public static readonly FontFamily HeadingFont = new("Bahnschrift, Segoe UI");

    public static void Apply(Theme theme)
    {
        Current = theme;
        Background = Brush(theme.Background);
        Sector = Brush(theme.Sector);
        SectorDisabledHover = Brush(theme.SectorHover);
        Accent = Brush(theme.Accent);
        AccentText = Brush(theme.AccentText);
        Text = Brush(theme.Text);
        TextMuted = Brush(theme.TextMuted);
        Rim = Brush(theme.Rim);

        // Те же цвета — ресурсам XAML (BackgroundBrush, AccentBrush, …).
        if (Application.Current is { } app)
        {
            var r = app.Resources;
            r["BackgroundBrush"] = Background;
            r["SectorBrush"] = Sector;
            r["SectorHoverBrush"] = SectorDisabledHover;
            r["AccentBrush"] = Accent;
            r["AccentTextBrush"] = AccentText;
            r["TextBrush"] = Text;
            r["MutedBrush"] = TextMuted;
            r["RimBrush"] = Rim;
        }
    }

    private static SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
