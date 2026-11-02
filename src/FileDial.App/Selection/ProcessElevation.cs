using System.Runtime.InteropServices;
using FileDial.App.Interop;

namespace FileDial.App.Selection;

/// <summary>
/// Приложение работает без прав администратора. Окна процессов с повышенными правами
/// (например, Проводник, запущенный от администратора) закрыты для него из-за UIPI и уровня целостности,
/// поэтому выделение из них не читаем, а честно сообщаем пользователю.
/// </summary>
internal static class ProcessElevation
{
    public static bool IsWindowElevatedAboveUs(IntPtr hwnd)
    {
        if (Environment.IsPrivilegedProcess)
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && IsProcessElevated(pid);
    }

    private static bool IsProcessElevated(uint pid)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
            return false;
        try
        {
            if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out var token))
            {
                // Токен процесса с более высоким уровнем целостности обычно не открывается.
                return Marshal.GetLastWin32Error() == NativeMethods.ERROR_ACCESS_DENIED;
            }
            try
            {
                return NativeMethods.GetTokenInformation(token, NativeMethods.TokenElevation, out var elevated,
                    sizeof(int), out _) && elevated != 0;
            }
            finally
            {
                NativeMethods.CloseHandle(token);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }
}
