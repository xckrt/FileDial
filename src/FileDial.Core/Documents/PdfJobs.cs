using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileDial.Core.Documents;

/// <summary>Чтение и запись PDF через PDFsharp. Исходник только читается; результат пишется рядом под новым именем.</summary>
public static class PdfFiles
{
    public const string Locked = "PDF защищён паролем.";

    public static string PagesLabel(int n) => Plural.Count(n, "страница", "страницы", "страниц");

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
            throw new CompressionException($"Не удалось прочитать {Path.GetFileName(path)}: файл повреждён или это не PDF.",
                ex.Message);
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
        progress?.Report(new JobProgress("Чтение PDF", null));
        using var doc = PdfFiles.OpenEditable(sourcePath, Password);
        var count = doc.PageCount; // после сохранения документ PDFsharp читать уже нельзя
        foreach (var page in doc.Pages)
        {
            ct.ThrowIfCancellationRequested();
            page.Rotate = ((page.Rotate + degrees) % 360 + 360) % 360;
        }

        progress?.Report(new JobProgress("Сохранение", null));
        var output = PdfFiles.SaveNear(doc, sourcePath, "_повёрнут", outputDirectory);
        progress?.Report(new JobProgress("Готово", 1));
        return Task.FromResult(new JobResult(JobStatus.Done,
            $"{PdfFiles.PagesLabel(count)} {(degrees > 0 ? "вправо" : "влево")}. Сохранено как {Path.GetFileName(output)}.",
            output));
    }
}

/// <summary>Каждую страницу — в отдельный PDF, в новую папку «имя (страницы)».</summary>
public sealed class PdfSplitJob(string? outputDirectory = null) : IFileJob, IPasswordAware
{
    public string? Password { get; init; }

    public IFileJob WithPassword(string password) => new PdfSplitJob(outputDirectory) { Password = password };

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress("Чтение PDF", null));
        using var source = PdfFiles.Open(sourcePath, Password, PdfDocumentOpenMode.Import);
        if (source.PageCount < 2)
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, "В документе одна страница — делить нечего."));

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
                progress?.Report(new JobProgress($"Страница {i} из {source.PageCount}", (double)(i - 1) / source.PageCount));
                using var single = PdfFiles.CopyPages(source, [i]);
                single.Save(Path.Combine(staging, $"{stem} — стр. {i.ToString().PadLeft(digits, '0')}.pdf"));
            }

            var result = FreeNames.Child(parent, $"{stem} (страницы)", isDirectory: true);
            Directory.Move(staging, result);
            progress?.Report(new JobProgress("Готово", 1));
            return Task.FromResult(new JobResult(JobStatus.Done,
                $"{PdfFiles.PagesLabel(source.PageCount)} → папка {Path.GetFileName(result)}.", result));
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
        progress?.Report(new JobProgress("Чтение PDF", null));
        using var source = PdfFiles.Open(sourcePath, Password, PdfDocumentOpenMode.Import);
        if (pages.Any(p => p < 1 || p > source.PageCount))
            throw new CompressionException($"В документе {PdfFiles.PagesLabel(source.PageCount)} — такой страницы нет.");

        using var doc = PdfFiles.CopyPages(source, pages);
        progress?.Report(new JobProgress("Сохранение", null));
        var output = PdfFiles.SaveNear(doc, sourcePath, $"_стр_{PageRange.Label(pages)}", outputDirectory);
        progress?.Report(new JobProgress("Готово", 1));
        return Task.FromResult(new JobResult(JobStatus.Done,
            $"{PdfFiles.PagesLabel(pages.Count)} из {source.PageCount}. Сохранено как {Path.GetFileName(output)}.", output));
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
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, "Для объединения выделите несколько PDF."));

        using var merged = new PdfDocument();
        for (var i = 0; i < paths.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress($"Файл {i + 1} из {paths.Count} · {Path.GetFileName(paths[i])}",
                (double)i / paths.Count));
            try
            {
                using var source = PdfFiles.Open(paths[i], null, PdfDocumentOpenMode.Import);
                foreach (var page in source.Pages)
                    merged.AddPage(page);
            }
            catch (PasswordRequiredException)
            {
                throw new CompressionException($"{Path.GetFileName(paths[i])} защищён паролем — снимите защиту и повторите.");
            }
        }

        progress?.Report(new JobProgress("Сохранение", null));
        var pageCount = merged.PageCount;
        // Имя — по папке, как у «Собрать в PDF» из картинок.
        var name = ImagesToPdfJob.PdfName(paths);
        var directory = outputDirectory ?? Path.GetDirectoryName(paths[0])!;
        var output = PdfFiles.SaveNear(merged, paths[0], "_объединено", outputDirectory,
            nameSource: Path.Combine(directory, name + ".src"));
        progress?.Report(new JobProgress("Готово", 1));
        return Task.FromResult(new JobResult(JobStatus.Done,
            $"{Plural.Count(paths.Count, "файл", "файла", "файлов")}, {PdfFiles.PagesLabel(pageCount)}. " +
            $"Сохранено как {Path.GetFileName(output)}.", output));
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
        progress?.Report(new JobProgress("Чтение PDF", null));
        using var doc = PdfFiles.OpenEditable(sourcePath, SourcePassword);
        ct.ThrowIfCancellationRequested();

        var security = doc.SecuritySettings;
        security.UserPassword = newPassword;
        security.OwnerPassword = newPassword;
        doc.SecurityHandler.SetEncryptionToV5(true);

        progress?.Report(new JobProgress("Шифрование", null));
        var output = PdfFiles.SaveNear(doc, sourcePath, "_с_паролем", outputDirectory);
        progress?.Report(new JobProgress("Готово", 1));
        return Task.FromResult(new JobResult(JobStatus.Done,
            $"Сохранено как {Path.GetFileName(output)}. Без пароля документ не открыть — запишите его.", output));
    }
}
