using System.Diagnostics;
using FileDial.App.Interop;

namespace FileDial.App.UI;

internal static class ShellActions
{
    /// <summary>Открывает папку и выделяет файл (без разбора командной строки explorer.exe).</summary>
    public static void ShowInFolder(string path)
    {
        var pidl = NativeMethods.ILCreateFromPathW(path);
        if (pidl == IntPtr.Zero)
        {
            OpenWithShell(Path.GetDirectoryName(path)!);
            return;
        }
        try
        {
            if (NativeMethods.SHOpenFolderAndSelectItems(pidl, 0, null, 0) != 0)
                OpenWithShell(Path.GetDirectoryName(path)!);
        }
        finally
        {
            NativeMethods.ILFree(pidl);
        }
    }

    public static void OpenReadme()
    {
        var readme = Path.Combine(AppContext.BaseDirectory, "README.md");
        if (!File.Exists(readme) || OpenWithShell(readme))
            return;

        // Для .md может не быть программы по умолчанию — открываем в Блокноте.
        try
        {
            var psi = new ProcessStartInfo("notepad.exe");
            psi.ArgumentList.Add(readme);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Открытие README", ex);
        }
    }

    private static bool OpenWithShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Открытие через оболочку", ex);
            return false;
        }
    }
}
