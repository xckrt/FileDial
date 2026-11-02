using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FileDial.App.Interop;

namespace FileDial.App.UI;

/// <summary>Короткое уведомление у курсора. Не забирает фокус, закрывается само или по клику.</summary>
internal sealed class ToastWindow : Window
{
    private static ToastWindow? _current;

    private ToastWindow(string message, NativeMethods.POINT cursor)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Title = "FileDial";

        var border = new Border
        {
            Background = Palette.Background,
            BorderBrush = Palette.Sector,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 10, 14, 10),
            Child = new TextBlock
            {
                Text = message,
                Foreground = Palette.Text,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 320,
            },
        };
        border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = border.DesiredSize;
        Width = size.Width;
        Height = size.Height;
        Content = border;

        var placement = new WindowPlacement(this, cursor, PlacementMode.NearCursor, size.Width, size.Height);
        SourceInitialized += (_, _) =>
        {
            WindowPlacement.MakeToolWindow(this, noActivate: true);
            placement.Apply();
        };
        Loaded += (_, _) => placement.Apply();
        MouseLeftButtonUp += (_, _) => Close();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Close();
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            if (_current == this) _current = null;
        };
        timer.Start();
    }

    public static void ShowAt(string message, NativeMethods.POINT cursor)
    {
        _current?.Close();
        _current = new ToastWindow(message, cursor);
        _current.Show();
    }
}
