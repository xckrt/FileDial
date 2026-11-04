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
        // Размер, измеренный до показа, — только для места у курсора: окно подстраивается под текст само.
        // Ровно измеренная ширина при отрисовке оказывалась на долю пикселя меньше нужной — последнее слово
        // переносилось на строку, которую обрезала высота окна.
        var size = new Size(Math.Ceiling(border.DesiredSize.Width) + 2, Math.Ceiling(border.DesiredSize.Height) + 2);
        SizeToContent = SizeToContent.WidthAndHeight;
        Content = border;

        var placement = new WindowPlacement(this, cursor, PlacementMode.NearCursor, size.Width, size.Height);
        SourceInitialized += (_, _) =>
        {
            WindowPlacement.MakeToolWindow(this, noActivate: true);
            placement.Apply();
        };
        // Появляется из лёгкого уменьшения, исчезает плавно.
        var scale = new ScaleTransform(Motion.Enabled ? 0.96 : 1, Motion.Enabled ? 0.96 : 1);
        border.RenderTransform = scale;
        border.RenderTransformOrigin = new Point(0.5, 0.5);
        if (Motion.Enabled)
            border.Opacity = 0;
        Loaded += (_, _) =>
        {
            placement.Apply();
            Motion.To(border, OpacityProperty, 1, Motion.Normal);
            Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Normal);
            Motion.To(scale, ScaleTransform.ScaleYProperty, 1, Motion.Normal);
        };
        MouseLeftButtonUp += (_, _) => CloseOnce();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Motion.To(border, OpacityProperty, 0, 200, CloseOnce);
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            _closed = true;
            if (_current == this) _current = null;
        };
        timer.Start();
    }

    private bool _closed;

    /// <summary>Закрытие может прийти дважды (клик и конец анимации) — второй раз игнорируем.</summary>
    private void CloseOnce()
    {
        if (!_closed)
            Close();
    }

    public static void ShowAt(string message, NativeMethods.POINT cursor)
    {
        _current?.CloseOnce();
        _current = new ToastWindow(message, cursor);
        _current.Show();
    }
}
