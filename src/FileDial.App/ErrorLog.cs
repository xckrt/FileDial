namespace FileDial.App;

/// <summary>Журнал непредвиденных ошибок: %LOCALAPPDATA%\FileDial\errors.log.</summary>
internal static class ErrorLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileDial", "errors.log");

    public static void Write(string context, Exception ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // Журнал не должен ронять приложение.
        }
    }
}
