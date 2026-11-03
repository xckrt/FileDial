using System.Globalization;
using System.Text.RegularExpressions;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;
using FileDial.Core.Text;

namespace FileDial.Core.Archives;

/// <summary>
/// Части файла: «имя.zip.001», «имя.zip.002», … — простое деление по байтам. Такие части понимают
/// 7-Zip и WinRAR (открывают .001 и сами собирают), а FileDial собирает их действием «Собрать из частей».
/// </summary>
public static partial class FileParts
{
    /// <summary>Готовые размеры части: почта и мессенджеры, CD, флешки FAT32 (не больше 4 ГиБ − 1 байт).</summary>
    public static IReadOnlyList<(string Label, long Bytes)> Presets =>
    [
        ($"10 {Lang.T.Megabytes}", 10_000_000),
        ($"25 {Lang.T.Megabytes}", 25_000_000),
        ($"100 {Lang.T.Megabytes}", 100_000_000),
        ($"700 {Lang.T.Megabytes}", 700_000_000),
        ($"1 {Lang.T.Gigabytes}", 1_000_000_000),
        ($"4 {Lang.T.Gigabytes} (FAT32)", uint.MaxValue),
    ];

    public static long PartCount(long fileSize, long partSize) =>
        fileSize <= 0 ? 0 : (fileSize + partSize - 1) / partSize;

    public static string PartName(string baseName, int index) =>
        baseName + "." + index.ToString("000", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<base>.+)\.(?<n>\d{3})$")]
    private static partial Regex PartPattern();

    /// <summary>Если путь — часть («…\имя.zip.003»), возвращает путь без номера («…\имя.zip»); иначе null.</summary>
    public static string? BaseOf(string path)
    {
        var match = PartPattern().Match(path);
        return match.Success && match.Groups["n"].Value != "000" ? match.Groups["base"].Value : null;
    }

    /// <summary>Все части по порядку, начиная с .001, пока следующая существует.</summary>
    public static IReadOnlyList<string> Find(string basePath)
    {
        var parts = new List<string>();
        for (var i = 1; i < 1000 && File.Exists(PartName(basePath, i)); i++)
            parts.Add(PartName(basePath, i));
        return parts;
    }

    /// <summary>Проверка набора: хотя бы две части, все кроме последней — одного размера, последняя не больше.</summary>
    public static string? Problem(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0)
            return Lang.T.ArchiveNoFirstPart;
        if (parts.Count == 1)
            return Lang.T.ArchiveOnlyOnePart;
        var first = new FileInfo(parts[0]).Length;
        for (var i = 1; i < parts.Count - 1; i++)
            if (new FileInfo(parts[i]).Length != first)
                return Lang.T.ArchivePartWrongSize(Path.GetFileName(parts[i]));
        if (new FileInfo(parts[^1]).Length > first)
            return Lang.T.ArchiveLastPartBigger;
        return null;
    }

    public static string PartsLabel(long n) => Lang.T.ArchiveParts(n);
}

/// <summary>Делит файл на части заданного размера рядом с ним. Исходный файл не меняется.</summary>
public sealed class FileSplitJob(long partSize, string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var size = new FileInfo(sourcePath).Length;
        var count = FileParts.PartCount(size, partSize);
        if (count <= 1)
            return new JobResult(JobStatus.NothingToDo, Lang.T.ArchiveSplitNothing(SizeFormatter.Format(size)));
        if (count > 999)
            throw new CompressionException(Lang.T.ArchiveTooManyParts);

        var parent = outputDirectory ?? Path.GetDirectoryName(sourcePath)!;
        if (OutputFolderCheck.WhyNot(parent, size + 10_000_000) is { } problem)
            throw new CompressionException(problem);

        // Свободное «семейство» имён: если имя.zip.001 уже есть — имя_2.zip.001 и т. д.
        var name = Path.GetFileName(sourcePath);
        string basePath;
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? name : $"{Path.GetFileNameWithoutExtension(name)}_{n}{Path.GetExtension(name)}";
            basePath = Path.Combine(parent, candidate);
            var taken = Enumerable.Range(1, (int)count).Any(i => OutputNameGenerator.PathTaken(FileParts.PartName(basePath, i)));
            if (!taken)
                break;
        }

        // Пишем во временные имена; переименовываем все разом только в конце — без «половины набора».
        var temps = new List<string>();
        try
        {
            await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            var buffer = new byte[1 << 20];
            long done = 0;
            for (var i = 1; i <= count; i++)
            {
                var temp = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}.tmp");
                temps.Add(temp);
                await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var left = Math.Min(partSize, size - done);
                while (left > 0)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, left)), ct).ConfigureAwait(false);
                    if (read == 0)
                        throw new IOException(Lang.T.ArchiveFileChanged);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    left -= read;
                    done += read;
                    progress?.Report(new JobProgress(Lang.T.ArchiveStagePart(i, count), (double)done / size));
                }
            }

            for (var i = 0; i < temps.Count; i++)
                File.Move(temps[i], FileParts.PartName(basePath, i + 1));
            temps.Clear();

            progress?.Report(new JobProgress(Lang.T.ArchiveDone, 1));
            return new JobResult(JobStatus.Done,
                Lang.T.ArchiveSplitDone(count, SizeFormatter.Format(partSize), Path.GetFileName(FileParts.PartName(basePath, 1))),
                FileParts.PartName(basePath, 1));
        }
        finally
        {
            foreach (var temp in temps)
                OutputFiles.TryDeleteFile(temp);
        }
    }
}

/// <summary>Собирает файл из частей .001, .002, … рядом с ними. Части не удаляются.</summary>
public sealed class FileJoinJob(string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string anyPart, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var basePath = FileParts.BaseOf(anyPart) ?? throw new CompressionException(Lang.T.ArchiveNotAPart);
        var parts = FileParts.Find(basePath);
        if (FileParts.Problem(parts) is { } problem)
            throw new CompressionException(problem);

        var total = parts.Sum(p => new FileInfo(p).Length);
        var parent = outputDirectory ?? Path.GetDirectoryName(basePath)!;
        if (OutputFolderCheck.WhyNot(parent, total + 10_000_000) is { } space)
            throw new CompressionException(space);

        var temp = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                long done = 0;
                var buffer = new byte[1 << 20];
                for (var i = 0; i < parts.Count; i++)
                {
                    await using var input = new FileStream(parts[i], FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        done += read;
                        progress?.Report(new JobProgress(Lang.T.ArchiveStagePart(i + 1, parts.Count), (double)done / total));
                    }
                }
            }

            // Если исходный файл ещё лежит рядом — не перезаписываем: «имя_2.zip».
            var final = OutputFiles.MoveToFreeName(temp,
                () => FreeNames.Child(parent, Path.GetFileName(basePath), isDirectory: false));
            progress?.Report(new JobProgress(Lang.T.ArchiveDone, 1));
            return new JobResult(JobStatus.Done,
                Lang.T.ArchiveJoined(parts.Count, Path.GetFileName(final), SizeFormatter.Format(total)), final);
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }
}
