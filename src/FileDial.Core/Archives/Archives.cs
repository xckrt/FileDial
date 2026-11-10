using System.Globalization;
using System.IO.Compression;
using System.Text;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;

namespace FileDial.Core.Archives;

public enum ArchiveFormat
{
    Zip,
    SevenZip,
}

/// <summary>Архиву (или PDF) нужен пароль, либо введённый неверен — окно спросит пароль и повторит действие.</summary>
/// <param name="lockedMessage">Текст «защищён паролем»; null — про архив.</param>
public sealed class PasswordRequiredException(bool wrongPassword, string? lockedMessage = null)
    : CompressionException(wrongPassword ? Lang.T.ArchiveWrongPassword : lockedMessage ?? Lang.T.ArchiveLocked)
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
        throw new CompressionException(Lang.T.ArchiveTarBadPath(path));
    }

    /// <summary>Понятный текст для типичных ошибок распаковки.</summary>
    public static string ExtractError(string detail) => Lang.T.ArchiveExtractError(detail).Trim();

    /// <param name="workingDirectory">Рабочая папка tar (вместо -C: так путь передаётся в Unicode).</param>
    public static async Task RunTarAsync(IReadOnlyList<string> args, string failureMessage, CancellationToken ct,
        string? workingDirectory = null)
    {
        if (!TarAvailable)
            throw new CompressionException(Lang.T.ArchiveNoTar);
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
            throw new CompressionException(Lang.T.ArchiveTarEncryption, error);
        throw new CompressionException(failureMessage, error);
    }

    /// <summary>Аргумент пароля для tar.exe (он тоже идёт в ANSI-кодировке).</summary>
    public static IReadOnlyList<string> PassphraseArgs(string? password)
    {
        if (password is null)
            return [];
        if (!IsAnsiSafe(password))
            throw new CompressionException(Lang.T.ArchivePasswordNotAnsi);
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
            var buffer = new byte[1 << 20];
            long done = 0;
            progress?.Report(new JobProgress(stage, 0));
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var destination = Path.GetFullPath(Path.Combine(target, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new CompressionException(Lang.T.ArchiveOutsideFolder(entry.FullName));

                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var input = entry.Open())
                using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                    CopyZipData(input, output, buffer, total, ref done, stage, progress, ct);
                File.SetLastWriteTime(destination, entry.LastWriteTime.DateTime);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new CompressionException(ExtractError(""), ex.Message);
        }
        catch (NotSupportedException ex)
        {
            // Шифрование и экзотические методы сжатия .NET не поддерживает.
            throw new CompressionException(Lang.T.ArchiveLockedOrUnsupported, ex.Message);
        }
    }

    internal static void CopyZipData(Stream input, Stream output, byte[] buffer, long total, ref long done,
        string stage, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = input.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read == 0)
                break;
            output.Write(buffer, 0, read);
            done += read;
            if (total > 0)
                progress?.Report(new JobProgress(stage, Math.Min((double)done / total, 1)));
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

    public static string FilesLabel(int n) => Lang.T.Files(n);
}
