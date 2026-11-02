namespace FileDial.Core.Selection;

public enum FileStateKind
{
    File,
    Directory,
    /// <summary>Пути нет на диске (в том числе виртуальные элементы оболочки вида ::{GUID}).</summary>
    Missing,
    /// <summary>Файл есть, но открыть на чтение нельзя.</summary>
    Unavailable,
}

public readonly record struct FileState(FileStateKind Kind, long Size = 0);

public interface IFileProbe
{
    FileState Probe(string path);
}

/// <summary>Проверяет файл только на чтение, ничего в нём не меняя.</summary>
public sealed class FileSystemProbe : IFileProbe
{
    public FileState Probe(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("::", StringComparison.Ordinal))
                return new(FileStateKind.Missing);
            if (Directory.Exists(path))
                return new(FileStateKind.Directory);
            if (!File.Exists(path))
                return new(FileStateKind.Missing);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return new(FileStateKind.File, stream.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return new(FileStateKind.Unavailable);
        }
    }
}
