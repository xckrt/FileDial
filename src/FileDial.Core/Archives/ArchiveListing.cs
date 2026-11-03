using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FileDial.Core.Compression;

namespace FileDial.Core.Archives;

/// <summary>Элемент архива для окна «Содержимое».</summary>
public sealed record ArchiveEntryInfo(string Path, long Size, bool IsDirectory);

public sealed record ArchiveContents(IReadOnlyList<ArchiveEntryInfo> Entries)
{
    public int Files => Entries.Count(e => !e.IsDirectory);

    /// <summary>Сколько займёт распакованное.</summary>
    public long UnpackedBytes => Entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
}

/// <summary>Список файлов архива без распаковки: zip — через .NET, остальное — «tar -tv».</summary>
public static partial class ArchiveListing
{
    public static async Task<ArchiveContents> ReadAsync(string archive, CancellationToken ct)
    {
        if (ArchiveSupport.IsZip(archive))
            return await Task.Run(() => ReadZip(archive), ct).ConfigureAwait(false);

        if (!ArchiveSupport.TarAvailable)
            throw new CompressionException("Для просмотра такого архива нужен tar.exe (Windows 10 1803 или новее).");

        // Байты как есть (Latin-1 — один в один), чтобы сначала раскрыть экранирование tar, потом декодировать ANSI.
        var result = await ProcessRunner.RunAsync(ArchiveSupport.Tar, ["-t", "-v", "-f", ArchiveSupport.TarPath(archive)],
            null, captureOutput: true, ct, outputEncoding: Encoding.Latin1).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new CompressionException("Не удалось прочитать список файлов: архив повреждён или защищён паролем.", result.ErrorTail);

        var entries = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => ParseTarLine(l.TrimEnd('\r'), ArchiveSupport.TarOutput))
            .OfType<ArchiveEntryInfo>()
            .ToList();
        return new ArchiveContents(entries);
    }

    private static ArchiveContents ReadZip(string archive)
    {
        try
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Read, ArchiveSupport.LegacyZipNames);
            return new ArchiveContents(zip.Entries
                .Select(e => new ArchiveEntryInfo(e.FullName.Replace('\\', '/'), e.Length, e.FullName.EndsWith('/')))
                .ToList());
        }
        catch (InvalidDataException ex)
        {
            throw new CompressionException("Не удалось прочитать список файлов: архив повреждён.", ex.Message);
        }
    }

    // «-rw-rw-rw-  0 0      0        5000 ноя 03 13:24 Папка/файл.txt» (режим, ссылки, владелец, группа, размер, дата, имя)
    [GeneratedRegex(@"^(?<mode>[-dlbcpsh][rwxsStT-]{9})\S*\s+\d+\s+\S+\s+\S+\s+(?<size>\d+)\s+\S+\s+\d+\s+\S+\s(?<name>.+)$")]
    private static partial Regex TarLine();

    /// <summary>
    /// Разбор строки «tar -tv». Строка — байты вывода, прочитанные как Latin-1. tar экранирует «непечатаемые»
    /// байты как \ooo (так выглядит, например, «№» в cp1251), а сам обратный слеш — как \\.
    /// </summary>
    public static ArchiveEntryInfo? ParseTarLine(string latin1Line, Encoding ansi)
    {
        var match = TarLine().Match(latin1Line);
        if (!match.Success)
            return null;

        var name = DecodeName(match.Groups["name"].Value, ansi);
        // Символьная ссылка: «имя -> цель» — показываем только имя.
        if (match.Groups["mode"].Value[0] == 'l' && name.IndexOf(" -> ", StringComparison.Ordinal) is var arrow and > 0)
            name = name[..arrow];

        var isDirectory = match.Groups["mode"].Value[0] == 'd' || name.EndsWith('/');
        var size = long.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
        return new ArchiveEntryInfo(name, isDirectory ? 0 : size, isDirectory);
    }

    private static string DecodeName(string latin1, Encoding ansi)
    {
        var bytes = new List<byte>(latin1.Length);
        for (var i = 0; i < latin1.Length; i++)
        {
            var ch = latin1[i];
            if (ch == '\\' && i + 1 < latin1.Length && latin1[i + 1] == '\\')
            {
                bytes.Add((byte)'\\');
                i++;
            }
            else if (ch == '\\' && i + 3 < latin1.Length + 0 && IsOctal(latin1, i + 1))
            {
                bytes.Add((byte)Convert.ToInt32(latin1.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                bytes.Add((byte)ch);
            }
        }
        return ansi.GetString(bytes.ToArray());
    }

    private static bool IsOctal(string s, int start) =>
        start + 3 <= s.Length && s.Substring(start, 3).All(c => c is >= '0' and <= '7');
}
