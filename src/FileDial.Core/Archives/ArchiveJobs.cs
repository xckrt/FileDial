using System.IO.Compression;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Text;

namespace FileDial.Core.Archives;

public enum ExtractMode
{
    /// <summary>Рядом с архивом; если внутри одна папка или один файл — без лишней вложенности.</summary>
    Here,
    /// <summary>Всегда в отдельную папку с именем архива.</summary>
    ToFolder,
    /// <summary>В выбранную пользователем папку (тоже в подпапку с именем архива, если внутри много всего).</summary>
    ToChosen,
}

/// <summary>
/// Распаковка через временную папку рядом с местом назначения: пока всё не распаковано, ничего «настоящего»
/// не появляется; при ошибке или отмене временная папка удаляется. Существующие файлы и папки не перезаписываются.
/// </summary>
public sealed class ArchiveExtractJob(ExtractMode mode, string? destination = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string archive, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var parent = destination ?? Path.GetDirectoryName(archive)!;
        var size = new FileInfo(archive).Length;
        // Распакованное обычно больше архива; берём с запасом.
        if (OutputFolderCheck.WhyNot(parent, size * 4 + 10_000_000) is { } problem)
            throw new CompressionException(problem);

        var staging = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}");
        try
        {
            await ArchiveSupport.ExtractAllAsync(archive, staging, progress, "Распаковка", ct).ConfigureAwait(false);
            var entries = Directory.GetFileSystemEntries(staging);
            if (entries.Length == 0)
                return new JobResult(JobStatus.NothingToDo, "Архив пуст — распаковывать нечего.");

            var files = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Length;
            string result;
            if (mode != ExtractMode.ToFolder && entries.Length == 1)
            {
                // Внутри одна папка (или файл) — кладём её прямо сюда, без «Архив\Архив\…».
                var single = entries[0];
                result = FreeNames.Child(parent, Path.GetFileName(single), Directory.Exists(single));
                Move(single, result);
            }
            else
            {
                result = FreeNames.Child(parent, ArchiveSupport.Stem(archive), isDirectory: true);
                Directory.Move(staging, result);
            }

            progress?.Report(new JobProgress("Готово", 1));
            return new JobResult(JobStatus.Done,
                $"Распаковано: {ArchiveSupport.FilesLabel(files)} → {Path.GetFileName(result)}.", result);
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(staging);
        }
    }

    private static void Move(string source, string target)
    {
        if (Directory.Exists(source))
            Directory.Move(source, target);
        else
            File.Move(source, target);
    }
}

/// <summary>Проверка целостности: архив читается целиком, контрольные суммы сверяются, на диск ничего не пишется.</summary>
public sealed class ArchiveTestJob : IFileJob
{
    public async Task<JobResult> RunAsync(string archive, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress("Проверка", null));
        int files;
        if (ArchiveSupport.TarAvailable)
        {
            // -O: содержимое в stdout (мы его отбрасываем) — tar читает всё и проверяет CRC каждого файла.
            await ArchiveSupport.RunTarAsync(["-x", "-O", "-f", ArchiveSupport.TarPath(archive)],
                "Архив повреждён или защищён паролем — прочитать его целиком не удалось.", ct).ConfigureAwait(false);
            files = ArchiveSupport.CountFiles(archive, TimeSpan.FromMinutes(5)) ?? 0;
        }
        else if (ArchiveSupport.IsZip(archive))
        {
            files = await Task.Run(() => ReadZipFully(archive, ct), ct).ConfigureAwait(false);
        }
        else
        {
            throw new CompressionException("Для проверки такого архива нужен tar.exe (Windows 10 1803 или новее).");
        }
        return new JobResult(JobStatus.Done, $"Архив в порядке: {ArchiveSupport.FilesLabel(files)}, ошибок нет.");
    }

    private static int ReadZipFully(string archive, CancellationToken ct)
    {
        try
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Read, ArchiveSupport.LegacyZipNames);
            var count = 0;
            foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            {
                ct.ThrowIfCancellationRequested();
                using var stream = entry.Open();
                stream.CopyTo(Stream.Null);
                count++;
            }
            return count;
        }
        catch (InvalidDataException ex)
        {
            throw new CompressionException("Архив повреждён.", ex.Message);
        }
    }
}

/// <summary>
/// Сжатие файлов и папок в архив рядом с ними. ZIP — средствами .NET (имена в UTF-8, читаются везде);
/// 7z — встроенным в Windows tar.exe (сжимает заметно лучше). Исходные файлы не трогаются.
/// </summary>
public sealed class ArchiveCreateJob(ArchiveFormat format, string? outputDirectory = null) : IFileJob, IMultiFileJob
{
    public string Extension => format == ArchiveFormat.Zip ? ".zip" : ".7z";

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct) =>
        RunAsync([sourcePath], progress, ct);

    public async Task<JobResult> RunAsync(IReadOnlyList<string> paths, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var parent = outputDirectory ?? Path.GetDirectoryName(paths[0].TrimEnd('\\', '/'))!;
        var path = await CreateAsync(paths, parent, ArchiveName(paths), progress, ct).ConfigureAwait(false);
        var files = paths.Sum(p => Directory.Exists(p) ? Directory.GetFiles(p, "*", SearchOption.AllDirectories).Length : 1);
        return new JobResult(JobStatus.Done,
            $"{ArchiveSupport.FilesLabel(files)} → {SizeFormatter.Format(new FileInfo(path).Length)}. " +
            $"Сохранено как {Path.GetFileName(path)}.", path);
    }

    /// <summary>Имя архива: для одного файла или папки — его имя, для нескольких — имя их папки.</summary>
    public static string ArchiveName(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1)
        {
            var single = paths[0].TrimEnd('\\', '/');
            return Directory.Exists(single) ? Path.GetFileName(single) : Path.GetFileNameWithoutExtension(single);
        }
        var folder = Path.GetFileName(Path.GetDirectoryName(paths[0].TrimEnd('\\', '/'))?.TrimEnd('\\', '/') ?? "");
        return string.IsNullOrWhiteSpace(folder) ? "Архив" : folder;
    }

    /// <summary>Создаёт архив из <paramref name="paths"/> (у всех один родитель) в <paramref name="parent"/>.</summary>
    public async Task<string> CreateAsync(IReadOnlyList<string> paths, string parent, string name,
        IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var total = paths.Sum(SizeOf);
        if (OutputFolderCheck.WhyNot(parent, total + 10_000_000) is { } problem)
            throw new CompressionException(problem);

        var temp = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            if (format == ArchiveFormat.Zip)
                await Task.Run(() => WriteZip(paths, temp, total, progress, ct), ct).ConfigureAwait(false);
            else
                await WriteSevenZipAsync(paths, temp, progress, ct).ConfigureAwait(false);

            var final = OutputFiles.MoveToFreeName(temp, () => FreeNames.Child(parent, name + Extension, isDirectory: false));
            progress?.Report(new JobProgress("Готово", 1));
            return final;
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }

    private static long SizeOf(string path) =>
        Directory.Exists(path)
            ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : new FileInfo(path).Length;

    private static void WriteZip(IReadOnlyList<string> paths, string temp, long total, IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        long done = 0;
        using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        void AddFile(string file, string entryName)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress($"Сжатие · {Path.GetFileName(file)}", total > 0 ? (double)done / total : null));
            try
            {
                zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new CompressionException($"Не удалось прочитать {Path.GetFileName(file)}: файл занят или нет доступа.");
            }
            done += new FileInfo(file).Length;
        }

        foreach (var path in paths)
        {
            var trimmed = path.TrimEnd('\\', '/');
            if (!Directory.Exists(trimmed))
            {
                AddFile(trimmed, Path.GetFileName(trimmed));
                continue;
            }

            // Папка входит в архив целиком, со своим именем; пустые папки тоже сохраняются.
            var root = Path.GetDirectoryName(trimmed)!;
            foreach (var dir in Directory.GetDirectories(trimmed, "*", SearchOption.AllDirectories).Prepend(trimmed))
            {
                var relative = Path.GetRelativePath(root, dir).Replace('\\', '/');
                if (Directory.EnumerateFileSystemEntries(dir).Any())
                    foreach (var file in Directory.GetFiles(dir))
                        AddFile(file, relative + "/" + Path.GetFileName(file));
                else
                    zip.CreateEntry(relative + "/");
            }
        }
    }

    private static async Task WriteSevenZipAsync(IReadOnlyList<string> paths, string temp, IProgress<JobProgress>? progress,
        CancellationToken ct)
    {
        progress?.Report(new JobProgress("Сжатие в 7z", null));
        var root = Path.GetDirectoryName(paths[0].TrimEnd('\\', '/'))!;
        var names = paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).ToList();
        // Имена верхнего уровня идут аргументами — им нужен ANSI; вложенные tar читает с диска в Unicode.
        if (names.FirstOrDefault(n => !ArchiveSupport.IsAnsiSafe(n)) is { } bad)
            throw new CompressionException(
                $"В имени «{bad}» есть символы, которые встроенный tar.exe не передаёт в 7z. Сожмите в ZIP — там такой проблемы нет.");
        // Временный файл: относительно рабочей папки, если он там же, иначе — по короткому пути папки.
        var tempDir = Path.GetDirectoryName(temp)!;
        var tempArg = string.Equals(Path.GetFullPath(tempDir), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(temp)
            : Path.Combine(ArchiveSupport.TarPath(tempDir), Path.GetFileName(temp));
        await ArchiveSupport.RunTarAsync(["--format", "7zip", "-c", "-f", tempArg, .. names],
            "Не удалось создать архив 7z.", ct, workingDirectory: root).ConfigureAwait(false);
    }
}

/// <summary>Перепаковка в другой формат: распаковка во временную папку и сжатие заново.</summary>
public sealed class ArchiveRepackJob(ArchiveFormat format, string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string archive, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "FileDial", Guid.NewGuid().ToString("N"));
        try
        {
            await ArchiveSupport.ExtractAllAsync(archive, work, progress, "Распаковка", ct).ConfigureAwait(false);
            var entries = Directory.GetFileSystemEntries(work);
            if (entries.Length == 0)
                return new JobResult(JobStatus.NothingToDo, "Архив пуст — перепаковывать нечего.");

            var creator = new ArchiveCreateJob(format);
            var parent = outputDirectory ?? Path.GetDirectoryName(archive)!;
            var result = await creator.CreateAsync(entries, parent, ArchiveSupport.Stem(archive), progress, ct).ConfigureAwait(false);
            return new JobResult(JobStatus.Done,
                $"{SizeFormatter.Format(new FileInfo(archive).Length)} → {SizeFormatter.Format(new FileInfo(result).Length)}. " +
                $"Сохранено как {Path.GetFileName(result)}.", result);
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(work);
        }
    }
}

/// <summary>Свободное имя в папке: «Фото», «Фото_2», …; у файлов номер ставится перед расширением.</summary>
public static class FreeNames
{
    public static string Child(string parent, string name, bool isDirectory)
    {
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var ext = isDirectory ? "" : Path.GetExtension(name);
        var candidate = Path.Combine(parent, name);
        for (var n = 2; OutputNameGenerator.PathTaken(candidate); n++)
            candidate = Path.Combine(parent, $"{stem}_{n}{ext}");
        return candidate;
    }
}
