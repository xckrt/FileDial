using System.Runtime.InteropServices;
using System.Windows.Interop;
using FileDial.App.Interop;

namespace FileDial.App.Hotkey;

/// <summary>Глобальная горячая клавиша через RegisterHotKey и невидимое окно сообщений (без хуков).</summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x4644; // "FD"
    private const uint VkK = 0x4B;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("FileDialHotkey")
        {
            ParentWindow = HwndMessage,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    public const string DisplayName = "Ctrl+Alt+K";

    public event EventHandler? Pressed;

    /// <summary>Регистрирует Ctrl+Alt+K. Возвращает код ошибки Win32 или 0.</summary>
    public int Register()
    {
        if (_registered)
            return 0;
        if (NativeMethods.RegisterHotKey(_source.Handle, HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT, VkK))
        {
            _registered = true;
            return 0;
        }
        return Marshal.GetLastWin32Error();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
