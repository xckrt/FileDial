namespace FileDial.Core.Compression;

/// <summary>Имя результата рядом с исходником: имя_25мб.mp4, имя.png, имя_2.png, ...</summary>
public static class OutputNameGenerator
{
    public static string Suffix => Settings.JobSettings.TargetSuffix(BitrateCalculator.DefaultTargetBytes);

    /// <summary>Имя для сжатия до 25 МБ.</summary>
    public static string Generate(string sourcePath, Func<string, bool> exists) =>
        Generate(sourcePath, Suffix, ".mp4", exists);

    /// <param name="suffix">Добавка к имени (может быть пустой — например, при смене формата).</param>
    /// <param name="extension">Расширение результата с точкой.</param>
    /// <param name="directory">Папка результата; по умолчанию — папка исходника.</param>
    public static string Generate(string sourcePath, string suffix, string extension, Func<string, bool> exists,
        string? directory = null)
    {
        directory ??= Path.GetDirectoryName(sourcePath)
                      ?? throw new ArgumentException("У файла нет папки.", nameof(sourcePath));
        var stem = Path.GetFileNameWithoutExtension(sourcePath) + suffix;

        var candidate = Path.Combine(directory, stem + extension);
        // Исходник считается занятым всегда: результат не должен его перезаписать ни при каком совпадении имён.
        for (var n = 2; exists(candidate) || SamePath(candidate, sourcePath); n++)
            candidate = Path.Combine(directory, $"{stem}_{n}{extension}");
        return candidate;
    }

    /// <summary>Существует ли путь как файл или папка (не перезаписываем ни то, ни другое).</summary>
    public static bool PathTaken(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public static class OutputFiles
{
    /// <summary>Переименовывает готовый временный файл, не перезаписывая существующие (даже появившиеся во время работы).</summary>
    public static string MoveToFreeName(string tempOutput, Func<string> nextName)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var target = nextName();
            try
            {
                File.Move(tempOutput, target, overwrite: false);
                return target;
            }
            catch (IOException) when (OutputNameGenerator.PathTaken(target))
            {
                // Имя заняли между проверкой и переносом — берём следующее.
            }
        }

        throw new CompressionException(Localization.Lang.T.JobNoFreeName);
    }

    public static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
