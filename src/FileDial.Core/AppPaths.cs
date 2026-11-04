namespace FileDial.Core;

/// <summary>
/// Где лежит программа. Дистрибутив — сжатый однофайловый exe, который при первом запуске распаковывается
/// в %TEMP%\.net\FileDial\&lt;хеш&gt;: там <see cref="AppContext.BaseDirectory"/>. Всё, что пользователь кладёт
/// «рядом с FileDial.exe» (ffmpeg, models, README), ищем в <see cref="ExeFolder"/>.
/// </summary>
public static class AppPaths
{
    /// <summary>Папка запущенного exe; под dotnet/testhost (тесты) — папка сборок.</summary>
    public static string ExeFolder { get; } = ResolveExeFolder(Environment.ProcessPath, AppContext.BaseDirectory);

    internal static string ResolveExeFolder(string? processPath, string baseDirectory)
    {
        var name = Path.GetFileNameWithoutExtension(processPath ?? "");
        if (string.IsNullOrEmpty(name) || name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("testhost", StringComparison.OrdinalIgnoreCase))
            return baseDirectory;
        return Path.GetDirectoryName(processPath) ?? baseDirectory;
    }

    /// <summary>Запущены из распакованной копии (а не из папки с exe).</summary>
    public static bool IsSelfExtracted => !SameFolder(ExeFolder, AppContext.BaseDirectory);

    private static bool SameFolder(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Старые распаковки: каждая версия распаковывается в свою папку (~180 МБ), а установщик
    /// (FileDial-Setup-….exe) — ещё и под своим именем. Удаляет прежние версии программы и распаковки установщиков
    /// старше часа (только что запустивший нас установщик ещё может работать). Ошибки — пропускаем.
    /// </summary>
    /// <param name="current">Текущая папка распаковки: &lt;корень&gt;\&lt;имя exe&gt;\&lt;хеш&gt;.</param>
    /// <returns>Сколько папок удалено.</returns>
    public static int CleanOldExtractions(string current, DateTime utcNow)
    {
        current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
        var app = Path.GetDirectoryName(current);
        var root = app is null ? null : Path.GetDirectoryName(app);
        if (root is null || !Directory.Exists(root))
            return 0;

        var removed = 0;
        try
        {
            foreach (var appDir in Directory.EnumerateDirectories(root, "FileDial*"))
            {
                var name = Path.GetFileName(appDir);
                var isSetup = name.Contains("setup", StringComparison.OrdinalIgnoreCase);
                if (!isSetup && !string.Equals(appDir, app, StringComparison.OrdinalIgnoreCase))
                    continue; // чужое имя (переименованный exe) — не наше дело
                foreach (var dir in Directory.EnumerateDirectories(appDir))
                {
                    if (string.Equals(dir, current, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (isSetup && utcNow - Directory.GetLastWriteTimeUtc(dir) < TimeSpan.FromHours(1))
                        continue;
                    if (TryDeleteUnused(dir))
                        removed++;
                }
                if (isSetup && !Directory.EnumerateFileSystemEntries(appDir).Any())
                    TryDeleteEmpty(appDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return removed;
    }

    /// <summary>
    /// Удаляет распаковку, только если ей никто не пользуется. Сначала переименовываем папку: Windows не даёт
    /// переименовать папку с открытыми файлами (загруженными DLL работающей копии другой версии), и тогда
    /// не тронуто ничего. Удаляй мы сразу — у работающей копии пропали бы ещё не загруженные сборки.
    /// </summary>
    private static bool TryDeleteUnused(string dir)
    {
        var doomed = dir + ".old-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Directory.Move(dir, doomed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // папкой пользуются (или нет прав) — оставляем как есть
        }
        try
        {
            Directory.Delete(doomed, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return true;
    }

    private static void TryDeleteEmpty(string dir)
    {
        try { Directory.Delete(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
