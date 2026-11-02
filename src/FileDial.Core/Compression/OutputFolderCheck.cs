using FileDial.Core.Text;

namespace FileDial.Core.Compression;

/// <summary>
/// Проверки папки с исходником до начала кодирования, чтобы не тратить минуты на первый проход
/// и упасть только при записи результата.
/// </summary>
public static class OutputFolderCheck
{
    /// <summary>Сколько места нужно: результат плюс 20% запаса на перебор и служебные данные mp4.</summary>
    public static long RequiredFreeBytes(long targetBytes) => targetBytes + targetBytes / 5;

    /// <summary>Причина, по которой в папку нельзя писать, или null.</summary>
    public static string? WhyNot(string directory, long targetBytes)
    {
        var probe = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.check");
        try
        {
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return "Нет прав на запись в папку с файлом. Результат сохраняется рядом с исходником — " +
                   "скопируйте видео в папку, где можно создавать файлы.";
        }

        var free = FreeBytes(directory);
        var required = RequiredFreeBytes(targetBytes);
        if (free is { } f && f < required)
            return $"На диске мало места: свободно {SizeFormatter.Format(f)}, нужно не меньше {SizeFormatter.Format(required)}.";
        return null;
    }

    private static long? FreeBytes(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
                return null; // сетевые пути DriveInfo не понимает — полагаемся на ошибку ffmpeg
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
