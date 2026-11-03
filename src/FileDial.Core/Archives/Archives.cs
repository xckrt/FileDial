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

/// <summary>Архиву нужен пароль (или введённый неверен) — окно спросит пароль и повторит действие.</summary>
public sealed class PasswordRequiredException(bool wrongPassword)
    : CompressionException(wrongPassword ? "Неверный пароль." : "Архив защищён паролем.")
{
    public bool WrongPassword { get; } = wrongPassword;
}

/// <summary>Действие, которое можно повторить с паролем от архива.</summary>
public interface IPasswordAware
{
    Jobs.IFileJob WithPassword(string password);
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

    // --- tar.exe и не-ANSI пути ---
    //
    // tar.exe из Windows получает аргументы командной строки в ANSI-кодировке системы (для русской Windows — cp1251).
    // Имена внутри архива и на диске он читает и пишет через Unicode-API, поэтому вложенные «Ünïcödé» и «日本»
    // в порядке. Но путь-аргумент с символами вне cp1251 он открыть не может. Поэтому:
    //  • папку назначения задаём рабочим каталогом процесса (это Unicode), а не аргументом -C;
    //  • путь к архиву, не помещающийся в ANSI, заменяем коротким именем 8.3 (если они включены на диске);
    //  • если и это не помогло — честно говорим, что делать.

    /// <summary>ANSI-кодировка системы без «похожих замен» (Ü→U): непредставимый символ — исключение.</summary>
    private static Encoding StrictAnsi =>
        Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>Кодировка вывода tar.exe (имена в списке) — ANSI-кодировка системы.</summary>
    public static Encoding TarOutput => Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);

    public static bool IsAnsiSafe(string text)
    {
        try
        {
            StrictAnsi.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, uint size);

    /// <summary>Путь существующего файла или папки в виде, понятном tar.exe.</summary>
    public static string TarPath(string path)
    {
        if (IsAnsiSafe(path))
            return path;
        try
        {
            var buffer = new StringBuilder(1024);
            if (GetShortPathName(path, buffer, (uint)buffer.Capacity) > 0 && IsAnsiSafe(buffer.ToString()))
                return buffer.ToString();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
        throw new CompressionException(
            $"В пути «{path}» есть символы, которые встроенный в Windows tar.exe не понимает. " +
            "Переименуйте папку (только русские и латинские буквы) или используйте ZIP.");
    }

    /// <summary>Понятный текст для типичных ошибок распаковки.</summary>
    public static string ExtractError(string detail) =>
        $"Не удалось распаковать: архив повреждён, защищён паролем или формат не поддерживается. {detail}".Trim();

    /// <param name="workingDirectory">Рабочая папка tar (вместо -C: так путь передаётся в Unicode).</param>
    public static async Task RunTarAsync(IReadOnlyList<string> args, string failureMessage, CancellationToken ct,
        string? workingDirectory = null)
    {
        if (!TarAvailable)
            throw new CompressionException("В Windows нет tar.exe — такие архивы не поддерживаются (нужна Windows 10 1803 или новее).");
        var result = await ProcessRunner.RunAsync(Tar, args, null, captureOutput: false, ct, workingDirectory, TarOutput)
            .ConfigureAwait(false);
        if (result.ExitCode == 0)
            return;

        // Сообщения libarchive о шифровании: нет пароля / неверный / шифрование, которое он не умеет.
        var error = result.ErrorTail;
        if (error.Contains("read passphrase", StringComparison.OrdinalIgnoreCase))
            throw new PasswordRequiredException(wrongPassword: false);
        if (error.Contains("Incorrect passphrase", StringComparison.OrdinalIgnoreCase))
            throw new PasswordRequiredException(wrongPassword: true);
        if (error.Contains("encrypt", StringComparison.OrdinalIgnoreCase))
            throw new CompressionException(
                "Архив зашифрован способом, который встроенный в Windows tar.exe не поддерживает (зашифрованные 7z и RAR). " +
                "Откройте его в 7-Zip или WinRAR.", error);
        throw new CompressionException(failureMessage, error);
    }

    /// <summary>Аргумент пароля для tar.exe (он тоже идёт в ANSI-кодировке).</summary>
    public static IReadOnlyList<string> PassphraseArgs(string? password)
    {
        if (password is null)
            return [];
        if (!IsAnsiSafe(password))
            throw new CompressionException("В пароле есть символы, которые встроенный tar.exe не передаёт. Откройте архив в 7-Zip.");
        return ["--passphrase", password];
    }

    /// <summary>Есть ли в zip зашифрованные файлы (тогда .NET его не распакует — нужен пароль и tar.exe).</summary>
    public static bool IsEncryptedZip(string path)
    {
        try
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Read, LegacyZipNames);
            return zip.Entries.Any(e => e.IsEncrypted);
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Распаковывает архив целиком в пустую папку <paramref name="target"/>.</summary>
    /// <param name="password">Пароль от архива (только для zip: tar.exe расшифровывает ZipCrypto и AES).</param>
    public static async Task ExtractAllAsync(string archive, string target, IProgress<JobProgress>? progress,
        string stage, CancellationToken ct, string? password = null)
    {
        Directory.CreateDirectory(target);
        if (IsZip(archive) && password is null)
        {
            if (IsEncryptedZip(archive))
                throw new PasswordRequiredException(wrongPassword: false);
            await Task.Run(() => ExtractZip(archive, target, progress, stage, ct), ct).ConfigureAwait(false);
        }
        else
        {
            progress?.Report(new JobProgress(stage, null));
            // bsdtar сам не пускает абсолютные пути и «..» за пределы папки.
            await RunTarAsync(["-x", .. PassphraseArgs(password), "-f", TarPath(archive)], ExtractError(""), ct,
                workingDirectory: target).ConfigureAwait(false);
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
            var result = ProcessRunner.RunAsync(Tar, ["-t", "-f", TarPath(archive)], null, captureOutput: true, cts.Token,
                    outputEncoding: TarOutput)
                .GetAwaiter().GetResult();
            if (result.ExitCode != 0)
                return null;
            // Имена в выводе могут быть в другой кодировке — считаем только строки; папки оканчиваются на «/».
            return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Count(l => !l.TrimEnd('\r').EndsWith('/'));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException
                                       or UnauthorizedAccessException or System.ComponentModel.Win32Exception
                                       or CompressionException)
        {
            return null;
        }
    }

    public static string FilesLabel(int n) => Plural.Count(n, "файл", "файла", "файлов");
}
