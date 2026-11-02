using System.Runtime.InteropServices;
using System.Windows.Interop;
using FileDial.App.Interop;
using FileDial.Core.Settings;

namespace FileDial.App.Hotkey;

/// <summary>Глобальная горячая клавиша через RegisterHotKey и невидимое окно сообщений (без хуков).</summary>
internal sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x4644; // "FD"
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;

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

    public event EventHandler? Pressed;

    /// <summary>Текущее зарегистрированное сочетание или null.</summary>
    public HotkeyGesture? Current { get; private set; }

    /// <summary>Регистрирует сочетание вместо текущего. Возвращает код ошибки Win32 или 0.
    /// При ошибке прежнее сочетание восстанавливается, чтобы программа не осталась без клавиши.</summary>
    public int Register(HotkeyGesture gesture)
    {
        if (Current == gesture)
            return 0;

        var previous = Current;
        Unregister();
        if (TryRegister(gesture, out var error))
        {
            Current = gesture;
            return 0;
        }

        if (previous is not null && TryRegister(previous, out _))
            Current = previous;
        return error;
    }

    private bool TryRegister(HotkeyGesture gesture, out int error)
    {
        var modifiers = (uint)gesture.Modifiers | NativeMethods.MOD_NOREPEAT;
        if (NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, modifiers, (uint)gesture.VirtualKey))
        {
            error = 0;
            return true;
        }
        error = Marshal.GetLastWin32Error();
        return false;
    }

    private void Unregister()
    {
        if (Current is null)
            return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        Current = null;
    }

    public static string Describe(int error, HotkeyGesture gesture) =>
        error == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"Сочетание {gesture} уже занято другой программой."
            : $"Не удалось зарегистрировать {gesture} (код ошибки {error}).";

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
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
