namespace FileDial.Core.Compression;

public sealed record FfmpegTools(string Ffmpeg, string Ffprobe);

/// <summary>Ищет ffmpeg.exe и ffprobe.exe рядом с программой, затем в PATH.</summary>
public static class FfmpegLocator
{
    public static FfmpegTools? Locate(string appDirectory, string? pathVariable)
    {
        var directories = new List<string> { appDirectory };
        if (!string.IsNullOrEmpty(pathVariable))
        {
            directories.AddRange(pathVariable
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(d => d.Trim('"')));
        }

        // Пару берём из одной папки, чтобы версии ffmpeg и ffprobe совпадали.
        foreach (var dir in directories)
        {
            try
            {
                var ffmpeg = Path.Combine(dir, "ffmpeg.exe");
                var ffprobe = Path.Combine(dir, "ffprobe.exe");
                if (File.Exists(ffmpeg) && File.Exists(ffprobe))
                    return new FfmpegTools(ffmpeg, ffprobe);
            }
            catch (ArgumentException)
            {
                // Битый элемент PATH — пропускаем.
            }
        }

        return null;
    }

    /// <summary>
    /// Порядок поиска: папка из настроек → рядом с программой → установленный программой
    /// (%LOCALAPPDATA%\FileDial\ffmpeg) → PATH.
    /// </summary>
    /// <param name="preferredFolder">Папка из настроек — проверяется первой.</param>
    public static FfmpegTools? LocateDefault(string? preferredFolder = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredFolder) && Locate(preferredFolder, null) is { } preferred)
            return preferred;
        var path = FfmpegInstaller.InstallFolder + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        return Locate(AppContext.BaseDirectory, path);
    }
}
