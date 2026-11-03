using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileDial.Core.Documents;

/// <summary>Чтение и запись PDF через PDFsharp. Исходник только читается; результат пишется рядом под новым именем.</summary>
public static class PdfFiles
{
    public static string Locked => Lang.T.PdfLocked;

    public static string PagesLabel(int n) => Lang.T.PdfPages(n);

    /// <summary>Число страниц для подписи в круге; null — не успели или не смогли (повреждён, под паролем).</summary>
    public static int? TryPageCount(string path, TimeSpan timeout)
    {
        var task = Task.Run(() =>
        {
            try
            {
                using var doc = Open(path, null, PdfDocumentOpenMode.Import);
                return (int?)doc.PageCount;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return null;
            }
        });
        return task.Wait(timeout) ? task.Result : null;
    }

    /// <summary>Открыть PDF. Нужен пароль или он неверен — <see cref="PasswordRequiredException"/>.</summary>
    public static PdfDocument Open(string path, string? password, PdfDocumentOpenMode mode)
    {
        var asked = false;
        try
        {
            var doc = PdfReader.Open(path, password ?? "", mode, args =>
            {
                asked = true;
                args.Abort = true;
            });
            // Отказ от пароля PDFsharp не считает ошибкой: возвращает недочитанный документ. Пароль нужен — так и говорим.
            if (!asked)
                return doc;
            try { doc.Dispose(); } catch (Exception) { }
            throw new PasswordRequiredException(password is not null, Locked);
        }
        catch (Exception ex) when (asked && ex is not PasswordRequiredException)
        {
            throw new PasswordRequiredException(password is not null, Locked);
        }
        catch (Exception ex) when (ex is PdfSharp.PdfSharpException or InvalidOperationException or NotSupportedException
                                       or FormatException or IndexOutOfRangeException or ArgumentException
                                       or NullReferenceException or InvalidCastException)
        {
            throw new CompressionException(Lang.T.PdfCannotRead(Path.GetFileName(path)), ex.Message);
        }
    }

    /// <summary>
    /// Документ для изменения. Обычный PDF открывается целиком — закладки и свойства сохраняются;
    /// защищённый собирается заново из страниц (иначе PDFsharp не даст его сохранить без пароля владельца).
    /// </summary>
    internal static PdfDocument OpenEditable(string path, string? password)
    {
        try
        {
            var doc = Open(path, password, PdfDocumentOpenMode.Modify);
            if (!doc.SecuritySettings.IsEncrypted)
                return doc;
            doc.Dispose();
        }
        catch (CompressionException ex) when (ex is not PasswordRequiredException)
        {
            // Modify не справился (бывает с нестандартными файлами) — пробуем собрать из страниц.
        }

        using var source = Open(path, password, PdfDocumentOpenMode.Import);
        return CopyPages(source, Enumerable.Range(1, source.PageCount));
    }

    internal static PdfDocument CopyPages(PdfDocument source, IEnumerable<int> pages)
    {
        var copy = new PdfDocument();
        copy.Info.Title = source.Info.Title;
        copy.Info.Author = source.Info.Author;
        foreach (var page in pages)
            copy.AddPage(source.Pages[page - 1]);
        return copy;
    }

    /// <summary>Сохранить рядом с исходником (или в папку результатов) под свободным именем.</summary>
    internal static string SaveNear(PdfDocument doc, string sourcePath, string suffix, string? outputDirectory,
        string? nameSource = null)
    {
        var directory = outputDirectory ?? Path.GetDirectoryName(sourcePath)!;
        if (OutputFolderCheck.WhyNot(directory, new FileInfo(sourcePath).Length * 2 + 1_000_000) is { } problem)
            throw new CompressionException(problem);

        var temp = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                doc.Save(stream, closeStream: false);
            return OutputFiles.MoveToFreeName(temp, () =>
                OutputNameGenerator.Generate(nameSource ?? sourcePath, suffix, ".pdf", OutputNameGenerator.PathTaken, directory));
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }
}

/// <summary>Повернуть все страницы на 90° вправо или влево.</summary>
public sealed class PdfRotateJob(int degrees, string? outputDirectory = null) : IFileJob, IPasswordAware
{
    public string? Password { get; init; }

    public IFileJob WithPassword(string password) => new PdfRotateJob(degrees, outputDirectory) { Password = password };

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.PdfStageReading, null));
        using var doc = PdfFiles.OpenEditable(sourcePath, Password);
        var count = doc.PageCount; // после сохранения документ PDFsharp читать уже нельзя
        foreach (var page in doc.Pages)
        {
            ct.ThrowIfCancellationRequested();
            page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
        }

        progress?.Report(new JobProgress(Lang.T.PdfStageSaving, null));
        var output = PdfFiles.SaveNear(doc, sourcePath, Lang.T.PdfRotatedSuffix, outputDirectory);
        progress?.Report(new JobProgress(Lang.T.DocDone, 1));
        return Task.FromResult(new JobResult(JobStatus.Done,
            Lang.T.PdfRotated(count, degrees > 0, Path.GetFileName(output)), output));
    }
}

/// <summary>Каждую страницу — в отдельный PDF, в новую папку «имя (страницы)».</summary>
public sealed class PdfSplitJob(string? outputDirectory = null) : IFileJob, IPasswordAware
{
    public string? Password { get; init; }

    public IFileJob WithPassword(string password) => new PdfSplitJob(outputDirectory) { Password = password };

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.PdfStageReading, null));
        using var source = PdfFiles.Open(sourcePath, Password, PdfDocumentOpenMode.Import);
        if (source.PageCount < 2)
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.PdfSinglePageNothing));

        var parent = outputDirectory ?? Path.GetDirectoryName(sourcePath)!;
        if (OutputFolderCheck.WhyNot(parent, new FileInfo(sourcePath).Length * 3 + 1_000_000) is { } problem)
            throw new CompressionException(problem);

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var digits = Math.Max(2, source.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        // Сначала во временную папку рядом: при отмене или ошибке не остаётся половины страниц.
        var staging = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            for (var i = 1; i <= source.PageCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new JobProgress(Lang.T.PdfStagePage(i, source.PageCount), (double)(i - 1) / source.PageCount));
                using var single = PdfFiles.CopyPages(source, [i]);
                single.Save(Path.Combine(staging, Lang.T.PdfPageFileName(stem, i.ToString().PadLeft(digits, '0')) + ".pdf"));
            }

            var result = FreeNames.Child(parent, Lang.T.PdfPagesFolder(stem), isDirectory: true);
            Directory.Move(staging, result);
            progress?.Report(new JobProgress(Lang.T.DocDone, 1));
            return Task.FromResult(new JobResult(JobStatus.Done,
                Lang.T.PdfSplitDone(source.PageCount, Path.GetFileName(result)), result));
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(staging);
        }
    }
}

/// <summary>Выбранные страницы (в указанном порядке) — в новый PDF.</summary>
public sealed class PdfExtractJob(IReadOnlyList<int> pages, string? outputDirectory = null) : IFileJob, IPasswordAware
{
    public string? Password { get; init; }

    public IFileJob WithPassword(string password) => new PdfExtractJob(pages, outputDirectory) { Password = password };

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.PdfStageReading, null));
        using var source = PdfFiles.Open(sourcePath, Password, PdfDocumentOpenMode.Import);
        if (pages.Any(p => p < 1 || p > source.PageCount))
            throw new CompressionException(Lang.T.PdfNoSuchPage(source.PageCount));

        using var doc = PdfFiles.CopyPages(source, pages);
        progress?.Report(new JobProgress(Lang.T.PdfStageSaving, null));
        var output = PdfFiles.SaveNear(doc, sourcePath, Lang.T.PdfPagesSuffix(PageRange.Label(pages)), outputDirectory);
        progress?.Report(new JobProgress(Lang.T.DocDone, 1));
        return Task.FromResult(new JobResult(JobStatus.Done,
            Lang.T.PdfExtracted(pages.Count, source.PageCount, Path.GetFileName(output)), output));
    }
}

/// <summary>Несколько PDF — в один, в порядке выделения.</summary>
public sealed class PdfMergeJob(string? outputDirectory = null) : IFileJob, IMultiFileJob
{
    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct) =>
        RunAsync([sourcePath], progress, ct);

    public Task<JobResult> RunAsync(IReadOnlyList<string> paths, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (paths.Count < 2)
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.PdfMergeNeedsSeveral));

        using var merged = new PdfDocument();
        for (var i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress(Lang.T.PdfStageFile(i + 1, paths.Count, Path.GetFileName(paths[i])),
                (double)i / paths.Count));
            try
            {
                using var source = PdfFiles.Open(paths[i], null, PdfDocumentOpenMode.Import);
                foreach (var page in source.Pages)
                    merged.AddPage(page);
            }
            catch (PasswordRequiredException)
            {
                throw new CompressionException(Lang.T.PdfMergeLocked(Path.GetFileName(paths[i])));
            }
        }

        progress?.Report(new JobProgress(Lang.T.PdfStageSaving, null));
        var pageCount = merged.PageCount;
        // Имя — по папке, как у «Собрать в PDF» из картинок.
        var name = ImagesToPdfJob.PdfName(paths);
        var directory = outputDirectory ?? Path.GetDirectoryName(paths[0])!;
        var output = PdfFiles.SaveNear(merged, paths[0], Lang.T.PdfMergedSuffix, outputDirectory,
            nameSource: Path.Combine(directory, name + ".src"));
        progress?.Report(new JobProgress(Lang.T.DocDone, 1));
        return Task.FromResult(new JobResult(JobStatus.Done,
            Lang.T.PdfMerged(paths.Count, pageCount, Path.GetFileName(output)), output));
    }
}

/// <summary>Копия PDF с паролем на открытие (AES-256).</summary>
public sealed class PdfProtectJob(string newPassword, string? outputDirectory = null) : IFileJob, IPasswordAware
{
    public string? SourcePassword { get; init; }

    public IFileJob WithPassword(string password) =>
        new PdfProtectJob(newPassword, outputDirectory) { SourcePassword = password };

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.PdfStageReading, null));
        using var doc = PdfFiles.OpenEditable(sourcePath, SourcePassword);
        ct.ThrowIfCancellationRequested();

        var security = doc.SecuritySettings;
        security.UserPassword = newPassword;
        security.OwnerPassword = newPassword;
        doc.SecurityHandler.SetEncryptionToV5(true);

        progress?.Report(new JobProgress(Lang.T.PdfStageEncrypting, null));
        var output = PdfFiles.SaveNear(doc, sourcePath, Lang.T.DocPasswordSuffix, outputDirectory);
        progress?.Report(new JobProgress(Lang.T.DocDone, 1));
        return Task.FromResult(new JobResult(JobStatus.Done, Lang.T.DocProtected(Path.GetFileName(output)), output));
    }
}
