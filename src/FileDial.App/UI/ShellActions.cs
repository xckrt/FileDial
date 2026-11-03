using System.Diagnostics;
using FileDial.App.Interop;
using FileDial.Core.Localization;

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
    /// <param name="name">Имя файла справки; по умолчанию — на языке интерфейса (README.md или README.ru.md).</param>
    public static string? ReadmePath(string? name = null)
    {
        name ??= ReadmeName;
        var nearby = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(nearby))
            return nearby;
        try
        {
            using var embedded = typeof(ShellActions).Assembly.GetManifestResourceStream(name);
            if (embedded is null)
                return null;
            var copy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileDial", name);
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

    /// <summary>Справка на языке интерфейса.</summary>
    public static string ReadmeName => Lang.T.Code == "ru" ? "README.ru.md" : "README.md";

    /// <summary>Все переводы справки — их кладёт в папку программы установщик.</summary>
    public static IReadOnlyList<string> AllReadmes() =>
        new[] { "README.md", "README.ru.md" }.Select(ReadmePath).OfType<string>().ToList();

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
