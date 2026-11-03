using System.Globalization;
using System.IO.Compression;
using System.Text;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Text;

namespace FileDial.Core.Archives;

public enum ArchiveFormat
{
    Zip,
    SevenZip,
}

/// <summary>Общие сведения об архивах: имена, кодировки, запуск tar.exe.</summary>
public static class ArchiveSupport
{
    static ArchiveSupport()
    {
        // Кодовые страницы вроде cp866 — для имён в zip, созданных старым Проводником.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>tar.exe из Windows (libarchive): 7z, rar, tar.gz/bz2/xz.</summary>
    public static string Tar => FfmpegInstaller.SystemTar;

    public static bool TarAvailable => File.Exists(Tar);

    private static readonly string[] CompoundTar = [".tar.gz", ".tar.bz2", ".tar.xz"];

    /// <summary>Имя архива без расширения: «фото.tar.gz» → «фото», «отчёт.zip» → «отчёт».</summary>
    public static string Stem(string archivePath)
    {
        var name = Path.GetFileName(archivePath);
        foreach (var compound in CompoundTar)
            if (name.EndsWith(compound, StringComparison.OrdinalIgnoreCase))
                return name[..^compound.Length];
        return Path.GetFileNameWithoutExtension(name);
    }

    public static bool IsZip(string path) => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    public static bool Is7z(string path) => Path.GetExtension(path).Equals(".7z", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Кодировка имён в zip без флага UTF-8: так их пишет Проводник — в OEM-кодировке системы (для русской
    /// Windows это cp866). Без этого «Отчёт.docx» превращается в «ЋвзсвЃ.docx».
    /// </summary>
    public static Encoding LegacyZipNames
    {
        get
        {
            try
            {
                var oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                return Encoding.GetEncoding(oem is 0 or 1 ? 866 : oem);
            }
            catch (ArgumentException)
            {
                return Encoding.GetEncoding(866);
            }
        }
    }

    /// <summary>Понятный текст для типичных ошибок распаковки.</summary>
    public static string ExtractError(string detail) =>
        $"Не удалось распаковать: архив повреждён, защищён паролем или формат не поддерживается. {detail}".Trim();

    public static async Task RunTarAsync(IReadOnlyList<string> args, string failureMessage, CancellationToken ct)
    {
        if (!TarAvailable)
            throw new CompressionException("В Windows нет tar.exe — такие архивы не поддерживаются (нужна Windows 10 1803 или новее).");
        var result = await ProcessRunner.RunAsync(Tar, args, null, captureOutput: false, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new CompressionException(failureMessage, result.ErrorTail);
    }

    /// <summary>Распаковывает архив целиком в пустую папку <paramref name="target"/>.</summary>
    public static async Task ExtractAllAsync(string archive, string target, IProgress<JobProgress>? progress,
        string stage, CancellationToken ct)
    {
        Directory.CreateDirectory(target);
        if (IsZip(archive))
            await Task.Run(() => ExtractZip(archive, target, progress, stage, ct), ct).ConfigureAwait(false);
        else
        {
            progress?.Report(new JobProgress(stage, null));
            // bsdtar сам не пускает абсолютные пути и «..» за пределы папки.
            await RunTarAsync(["-x", "-f", archive, "-C", target], ExtractError(""), ct).ConfigureAwait(false);
        }
    }

    /// <summary>zip — средствами .NET: прогресс по байтам, правильные русские имена, защита от «../».</summary>
    private static void ExtractZip(string archive, string target, IProgress<JobProgress>? progress, string stage,
        CancellationToken ct)
    {
        try
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Read, LegacyZipNames);
            var root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            var total = Math.Max(1, zip.Entries.Sum(e => e.Length));
            long done = 0;
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var destination = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new CompressionException($"Архив пытается записать файл за пределы папки: {entry.FullName}");

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: false);
                done += entry.Length;
                progress?.Report(new JobProgress(stage, (double)done / total));
            }
        }
        catch (InvalidDataException ex)
        {
            throw new CompressionException(ExtractError(""), ex.Message);
        }
        catch (NotSupportedException ex)
        {
            // Шифрование и экзотические методы сжатия .NET не поддерживает.
            throw new CompressionException("Архив защищён паролем или сжат неподдерживаемым способом.", ex.Message);
        }
    }

    /// <summary>Сколько файлов в архиве (для подписи в круге); null — не удалось быстро узнать.</summary>
    public static int? CountFiles(string archive, TimeSpan timeout)
    {
        try
        {
            if (IsZip(archive))
            {
                using var zip = ZipFile.Open(archive, ZipArchiveMode.Read, LegacyZipNames);
                return zip.Entries.Count(e => !e.FullName.EndsWith('/'));
            }
            if (!TarAvailable)
                return null;
            using var cts = new CancellationTokenSource(timeout);
            var result = ProcessRunner.RunAsync(Tar, ["-t", "-f", archive], null, captureOutput: true, cts.Token)
                .GetAwaiter().GetResult();
            if (result.ExitCode != 0)
                return null;
            // Имена в выводе могут быть в другой кодировке — считаем только строки; папки оканчиваются на «/».
            return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(l => !l.TrimEnd('\r').EndsWith('/'));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException
                                       or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    public static string FilesLabel(int n) => Plural.Count(n, "файл", "файла", "файлов");
}
