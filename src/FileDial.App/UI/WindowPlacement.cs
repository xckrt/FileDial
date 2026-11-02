using System.Windows;
using System.Windows.Interop;
using FileDial.App.Interop;
using FileDial.Core.Geometry;

namespace FileDial.App.UI;

internal enum PlacementMode
{
    CenterOnCursor,
    NearCursor,
}

/// <summary>
/// Размещение окна в физических пикселях на мониторе под курсором с учётом DPI этого монитора.
/// Приложение объявлено PerMonitorV2, поэтому WPF масштабирует содержимое под DPI монитора сам,
/// а мы задаём итоговый прямоугольник через SetWindowPos.
/// </summary>
internal sealed class WindowPlacement
{
    private readonly Window _window;
    private readonly NativeMethods.POINT _cursor;
    private readonly PlacementMode _mode;
    private readonly double _widthDip;
    private readonly double _heightDip;

    public WindowPlacement(Window window, NativeMethods.POINT cursor, PlacementMode mode, double widthDip, double heightDip)
    {
        _window = window;
        _cursor = cursor;
        _mode = mode;
        _widthDip = widthDip;
        _heightDip = heightDip;

        // При переходе на монитор с другим DPI WPF подстраивает размер; после этого ставим окно заново.
        _window.DpiChanged += (_, _) => _window.Dispatcher.BeginInvoke(Apply);
    }

    public static NativeMethods.POINT CursorPosition()
    {
        NativeMethods.GetCursorPos(out var p);
        return p;
    }

    public void Apply()
    {
        var hwnd = new WindowInteropHelper(_window).EnsureHandle();
        var monitor = NativeMethods.MonitorFromPoint(_cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return;
        var work = new PixelRect(info.rcWork.Left, info.rcWork.Top,
            info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top);

        double dpi = 96;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            dpi = dpiX;

        var width = ScreenPlacement.ToPixels(_widthDip, dpi);
        var height = ScreenPlacement.ToPixels(_heightDip, dpi);
        var rect = _mode == PlacementMode.CenterOnCursor
            ? ScreenPlacement.CenterOn(_cursor.X, _cursor.Y, width, height, work)
            : ScreenPlacement.NearCursor(_cursor.X, _cursor.Y, width, height, ScreenPlacement.ToPixels(16, dpi), work);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Убирает окно из Alt+Tab; при необходимости делает его неактивируемым.</summary>
    public static void MakeToolWindow(Window window, bool noActivate)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TOOLWINDOW;
        if (noActivate)
            style |= NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }
}
