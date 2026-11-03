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

    /// <summary>
    /// README рядом с программой, а если его нет (exe скопировали отдельно) — копия справки, вшитой в exe,
    /// в папке данных. null — справку взять неоткуда.
    /// </summary>
    public static string? ReadmePath()
    {
        var nearby = Path.Combine(AppContext.BaseDirectory, "README.md");
        if (File.Exists(nearby))
            return nearby;
        try
        {
            using var embedded = typeof(ShellActions).Assembly.GetManifestResourceStream("README.md");
            if (embedded is null)
                return null;
            var copy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileDial", "README.md");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            using (var file = File.Create(copy))
                embedded.CopyTo(file);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Справка", ex);
            return null;
        }
    }

    public static void OpenReadme()
    {
        if (ReadmePath() is not { } readme || OpenWithShell(readme))
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
