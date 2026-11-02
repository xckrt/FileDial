using FileDial.Core.Text;

namespace FileDial.Core.Settings;

/// <summary>Настройки, влияющие на действия над файлами: цели сжатия и папка для результатов.</summary>
public sealed record JobSettings
{
    public const long DefaultVideoTarget = 25_000_000;
    public const long DefaultImageTarget = 1_000_000;

    public static readonly JobSettings Default = new();

    public long VideoTargetBytes { get; init; } = DefaultVideoTarget;

    public long ImageTargetBytes { get; init; } = DefaultImageTarget;

    /// <summary>Папка для результатов; null — рядом с исходным файлом.</summary>
    public string? OutputFolder { get; init; }

    public string OutputDirectoryFor(string sourcePath) =>
        string.IsNullOrWhiteSpace(OutputFolder) ? Path.GetDirectoryName(sourcePath)! : OutputFolder;

    /// <summary>«25 МБ», «500 КБ» — с неразрывным пробелом, чтобы подпись сектора не рвалась.</summary>
    public static string TargetLabel(long bytes) => SizeFormatter.Format(bytes).Replace(' ', ' ');

    /// <summary>Добавка к имени: «_25мб», «_500кб», «_0,5мб».</summary>
    public static string TargetSuffix(long bytes) =>
        "_" + SizeFormatter.Format(bytes).Replace(" ", "").ToLowerInvariant();
}
