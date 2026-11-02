namespace FileDial.Core.Compression;

/// <summary>Имя результата рядом с исходником: имя_25мб.mp4, имя_25мб_2.mp4, ...</summary>
public static class OutputNameGenerator
{
    public const string Suffix = "_25мб";

    public static string Generate(string sourcePath, Func<string, bool> exists)
    {
        var directory = Path.GetDirectoryName(sourcePath)
                        ?? throw new ArgumentException("У файла нет папки.", nameof(sourcePath));
        var stem = Path.GetFileNameWithoutExtension(sourcePath) + Suffix;

        var candidate = Path.Combine(directory, stem + ".mp4");
        for (var n = 2; exists(candidate); n++)
            candidate = Path.Combine(directory, $"{stem}_{n}.mp4");
        return candidate;
    }

    /// <summary>Существует ли путь как файл или папка (не перезаписываем ни то, ни другое).</summary>
    public static bool PathTaken(string path) => File.Exists(path) || Directory.Exists(path);
}
