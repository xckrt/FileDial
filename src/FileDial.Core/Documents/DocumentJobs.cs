using System.IO.Compression;
using System.Text;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileDial.Core.Documents;

/// <summary>Общее для задач над документами: временная папка, проверка места, перенос результата под свободное имя.</summary>
internal static class DocumentOutput
{
    public static string Directory(string sourcePath, string? outputDirectory) =>
        outputDirectory ?? Path.GetDirectoryName(sourcePath)!;

    public static void CheckSpace(string directory, long bytes)
    {
        if (OutputFolderCheck.WhyNot(directory, bytes) is { } problem)
            throw new CompressionException(problem);
    }

    /// <summary>Временный файл рядом с результатом: перенос в итоговое имя — без копирования между дисками.</summary>
    public static string TempNear(string directory) => Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");

    public static string WorkDirectory()
    {
        var work = Path.Combine(Path.GetTempPath(), "FileDial", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(work);
        return work;
    }

    public static string Finish(string temp, string sourcePath, string suffix, string extension, string directory) =>
        OutputFiles.MoveToFreeName(temp, () =>
            OutputNameGenerator.Generate(sourcePath, suffix, extension, OutputNameGenerator.PathTaken, directory));
}

/// <summary>«В PDF» и «Формат»: документ сохраняется в другом формате через Office или LibreOffice.</summary>
public sealed class DocumentConvertJob(string targetExtension, string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var family = OfficeDocs.FamilyOf(sourcePath);
        var converter = DocumentConverters.For(family, targetExtension)
                        ?? throw new CompressionException(DocumentConverters.NoConverter + ".");
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, new FileInfo(sourcePath).Length * 3 + 5_000_000);

        progress?.Report(new JobProgress(Lang.T.DocStageSavingAs(converter.Name(family), Label(targetExtension)), null));
        var work = DocumentOutput.WorkDirectory();
        try
        {
            // Office пишет во временную папку под простым именем; итог переносится под свободное имя рядом.
            var produced = Path.Combine(work, "document" + targetExtension);
            await converter.ConvertAsync(sourcePath, produced, null, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var temp = DocumentOutput.TempNear(directory);
            File.Move(produced, temp);
            try
            {
                var output = DocumentOutput.Finish(temp, sourcePath, "", targetExtension, directory);
                progress?.Report(new JobProgress(Lang.T.DocDone, 1));
                var note = targetExtension == ".csv" && family == DocumentFamily.Sheet ? Lang.T.DocCsvFirstSheetNote : "";
                return new JobResult(JobStatus.Done, FileJobs.DoneMessage(new FileInfo(sourcePath).Length, output) + note, output);
            }
            finally
            {
                OutputFiles.TryDeleteFile(temp);
            }
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(work);
        }
    }

    public static string Label(string extension) => extension.TrimStart('.').ToUpperInvariant();
}

/// <summary>Копия документа с паролем на открытие — средствами Office.</summary>
public sealed class DocumentProtectJob(string password, string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var family = OfficeDocs.FamilyOf(sourcePath);
        var converter = DocumentConverters.OfficeFor(family) ?? throw new CompressionException(DocumentConverters.NoOffice + ".");
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, new FileInfo(sourcePath).Length * 3 + 5_000_000);

        progress?.Report(new JobProgress(Lang.T.DocStageEncrypting(converter.Name(family)), null));
        var work = DocumentOutput.WorkDirectory();
        try
        {
            var produced = Path.Combine(work, "document" + extension);
            await converter.ConvertAsync(sourcePath, produced, password, ct).ConfigureAwait(false);
            var temp = DocumentOutput.TempNear(directory);
            File.Move(produced, temp);
            try
            {
                var output = DocumentOutput.Finish(temp, sourcePath, Lang.T.DocPasswordSuffix, extension, directory);
                progress?.Report(new JobProgress(Lang.T.DocDone, 1));
                return new JobResult(JobStatus.Done, Lang.T.DocProtected(Path.GetFileName(output)), output);
            }
            finally
            {
                OutputFiles.TryDeleteFile(temp);
            }
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(work);
        }
    }
}

/// <summary>Несколько документов — в один PDF: каждый сохраняется в PDF, затем страницы собираются по порядку.</summary>
public sealed class DocumentsToPdfJob(string? outputDirectory = null) : IFileJob, IMultiFileJob
{
    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct) =>
        RunAsync([sourcePath], progress, ct);

    public async Task<JobResult> RunAsync(IReadOnlyList<string> paths, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var directory = DocumentOutput.Directory(paths[0], outputDirectory);
        DocumentOutput.CheckSpace(directory, paths.Sum(p => new FileInfo(p).Length) * 3 + 10_000_000);

        var work = DocumentOutput.WorkDirectory();
        try
        {
            using var merged = new PdfDocument();
            for (var i = 0; i < paths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var path = paths[i];
                progress?.Report(new JobProgress(Lang.T.DocStageDocument(i + 1, paths.Count, Path.GetFileName(path)),
                    (double)i / paths.Count));
                var pdf = Path.Combine(work, $"{i}.pdf");
                var family = OfficeDocs.FamilyOf(path);
                var converter = DocumentConverters.For(family, ".pdf")
                                ?? throw new CompressionException(DocumentConverters.NoConverter + ".");
                await converter.ConvertAsync(path, pdf, null, ct).ConfigureAwait(false);

                using var part = PdfFiles.Open(pdf, null, PdfDocumentOpenMode.Import);
                foreach (var page in part.Pages)
                    merged.AddPage(page);
            }

            progress?.Report(new JobProgress(Lang.T.DocStageBuildingPdf, null));
            var pageCount = merged.PageCount;
            var temp = DocumentOutput.TempNear(directory);
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                    merged.Save(stream, closeStream: false);
                var nameSource = Path.Combine(directory, ImagesToPdfJob.PdfName(paths) + ".src");
                var output = DocumentOutput.Finish(temp, nameSource, "", ".pdf", directory);
                progress?.Report(new JobProgress(Lang.T.DocDone, 1));
                return new JobResult(JobStatus.Done, Lang.T.DocCombined(paths.Count, pageCount, Path.GetFileName(output)), output);
            }
            finally
            {
                OutputFiles.TryDeleteFile(temp);
            }
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(work);
        }
    }
}

/// <summary>Текст документа в «имя.txt» (UTF-8). Старые форматы сначала пересохраняются через Office или LibreOffice.</summary>
public sealed class DocumentTextJob(string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, new FileInfo(sourcePath).Length + 5_000_000);
        progress?.Report(new JobProgress(Lang.T.DocStageReadingText, null));

        string text;
        if (OfficeDocs.IsPackage(sourcePath))
        {
            text = OfficeDocs.ExtractText(sourcePath);
        }
        else
        {
            // DOC, RTF, PPT: пересохраняем в новый формат и читаем его.
            var modern = OfficeDocs.FamilyOf(sourcePath) == DocumentFamily.Slides ? ".pptx" : ".docx";
            var converter = DocumentConverters.For(OfficeDocs.FamilyOf(sourcePath), modern)
                            ?? throw new CompressionException(DocumentConverters.NoConverter + ".");
            var work = DocumentOutput.WorkDirectory();
            try
            {
                var copy = Path.Combine(work, "document" + modern);
                await converter.ConvertAsync(sourcePath, copy, null, ct).ConfigureAwait(false);
                text = OfficeDocs.ExtractText(copy);
            }
            finally
            {
                OutputFiles.TryDeleteDirectory(work);
            }
        }

        if (string.IsNullOrWhiteSpace(text))
            return new JobResult(JobStatus.NothingToDo, Lang.T.DocNoText);

        var temp = DocumentOutput.TempNear(directory);
        try
        {
            // С BOM: так старый Блокнот и Excel сразу видят UTF-8.
            await File.WriteAllTextAsync(temp, text, new UTF8Encoding(true), ct).ConfigureAwait(false);
            var output = DocumentOutput.Finish(temp, sourcePath, "", ".txt", directory);
            progress?.Report(new JobProgress(Lang.T.DocDone, 1));
            var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            return new JobResult(JobStatus.Done, Lang.T.DocTextDone(words, Path.GetFileName(output)), output);
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }
}

/// <summary>Картинки из документа — в папку «имя (картинки)», как они лежат внутри (без пересжатия).</summary>
public sealed class DocumentImagesJob(string? outputDirectory = null) : IFileJob
{
    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var parent = DocumentOutput.Directory(sourcePath, outputDirectory);
        using var zip = OfficeDocs.OpenPackage(sourcePath);
        var images = OfficeDocs.ImageEntries(zip).ToList();
        if (images.Count == 0)
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.DocNoImages));

        DocumentOutput.CheckSpace(parent, images.Sum(e => e.Length) + 1_000_000);
        var staging = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            for (var i = 0; i < images.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new JobProgress(Lang.T.DocStageImage(i + 1, images.Count), (double)i / images.Count));
                // Имена внутри разных частей могут совпасть (image1.png у слайда и у заметок) — берём свободное.
                var target = FreeNames.Child(staging, Path.GetFileName(images[i].FullName), isDirectory: false);
                using var from = images[i].Open();
                using var to = File.Create(target);
                from.CopyTo(to);
            }

            var result = FreeNames.Child(parent, Lang.T.DocImagesFolder(Path.GetFileNameWithoutExtension(sourcePath)), isDirectory: true);
            Directory.Move(staging, result);
            progress?.Report(new JobProgress(Lang.T.DocDone, 1));
            return Task.FromResult(new JobResult(JobStatus.Done,
                Lang.T.DocImagesDone(images.Count, Path.GetFileName(result)), result));
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(staging);
        }
    }
}

/// <summary>«Убрать личные данные»: копия без имён авторов, компании и учётных записей.</summary>
public sealed class DocumentStripJob(string? outputDirectory = null) : IFileJob
{
    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, new FileInfo(sourcePath).Length * 2 + 1_000_000);
        progress?.Report(new JobProgress(Lang.T.DocStageCleaning, null));

        var temp = DocumentOutput.TempNear(directory);
        try
        {
            var report = OfficeDocs.StripPersonalData(sourcePath, temp);
            ct.ThrowIfCancellationRequested();
            var removed = new List<string>();
            if (report.Properties) removed.Add(Lang.T.DocRemovedProperties);
            if (report.Comments > 0) removed.Add(Lang.T.DocRemovedCommentNames);
            if (report.Revisions > 0) removed.Add(Lang.T.DocRemovedRevisionNames);
            if (report.Links > 0) removed.Add(Lang.T.DocRemovedTemplatePath);
            if (removed.Count == 0)
                return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.DocNothingPersonal));

            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            var output = DocumentOutput.Finish(temp, sourcePath, Lang.T.DocCleanSuffix, extension, directory);
            progress?.Report(new JobProgress(Lang.T.DocDone, 1));
            return Task.FromResult(new JobResult(JobStatus.Done,
                Lang.T.DocStripped(string.Join(", ", removed), Path.GetFileName(output)), output));
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }
}

/// <summary>
/// «Сжать картинки»: большие фото внутри документа уменьшаются до 2000 px по длинной стороне,
/// JPEG пересжимается. Имена и форматы картинок не меняются — документ открывается как прежде.
/// </summary>
public sealed class DocumentCompressJob(FfmpegTools tools, string? outputDirectory = null) : IFileJob
{
    public const int MaxSide = 2000;
    private const long MinImageBytes = 150_000;

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        var sourceSize = new FileInfo(sourcePath).Length;
        DocumentOutput.CheckSpace(directory, sourceSize * 2 + 1_000_000);

        var work = DocumentOutput.WorkDirectory();
        var temp = DocumentOutput.TempNear(directory);
        try
        {
            // Сначала пересжимаем подходящие картинки во временную папку, потом собираем документ заново.
            var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var zip = OfficeDocs.OpenPackage(sourcePath))
            {
                var candidates = OfficeDocs.ImageEntries(zip)
                    .Where(e => e.Length >= MinImageBytes && Path.GetExtension(e.FullName).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png")
                    .ToList();
                for (var i = 0; i < candidates.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new JobProgress(Lang.T.DocStageImage(i + 1, candidates.Count), (double)i / (candidates.Count + 1)));
                    var ext = Path.GetExtension(candidates[i].FullName).ToLowerInvariant();
                    var input = Path.Combine(work, $"{i}-in{ext}");
                    candidates[i].ExtractToFile(input);
                    if (await ShrinkAsync(input, Path.Combine(work, $"{i}-out{ext}"), ct).ConfigureAwait(false) is { } smaller &&
                        smaller.Length < candidates[i].Length * 9 / 10)
                        replacements[candidates[i].FullName] = smaller;
                }
            }

            if (replacements.Count == 0)
                return new JobResult(JobStatus.NothingToDo, Lang.T.DocImagesAlreadyCompact);

            progress?.Report(new JobProgress(Lang.T.DocStageRebuilding, null));
            OfficeDocs.RewritePackage(sourcePath, temp, (name, _) => replacements.GetValueOrDefault(name));
            if (new FileInfo(temp).Length >= sourceSize)
                return new JobResult(JobStatus.NothingToDo, Lang.T.DocCompressFailed);

            var output = DocumentOutput.Finish(temp, sourcePath, Lang.T.DocCompressedSuffix, Path.GetExtension(sourcePath).ToLowerInvariant(), directory);
            progress?.Report(new JobProgress(Lang.T.DocDone, 1));
            return new JobResult(JobStatus.Done,
                Lang.T.DocImagesRecompressed(replacements.Count) + " " + FileJobs.DoneMessage(sourceSize, output), output);
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
            OutputFiles.TryDeleteDirectory(work);
        }
    }

    /// <summary>Уменьшенная копия картинки или null (ffmpeg не справился — картинку оставляем как есть).</summary>
    private async Task<byte[]?> ShrinkAsync(string input, string output, CancellationToken ct)
    {
        // Только уменьшение: маленькие картинки не растягиваем. Чётные размеры не нужны — это не видео.
        var scale = $"scale='if(gt(iw,ih),min(iw,{MaxSide}),-2)':'if(gt(iw,ih),-2,min(ih,{MaxSide}))'";
        var png = output.EndsWith(".png", StringComparison.Ordinal);
        List<string> args = ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-i", input, "-frames:v", "1",
            "-map_metadata", "-1", "-vf", scale];
        args.AddRange(png ? ["-c:v", "png", "-compression_level", "9"] : ["-c:v", "mjpeg", "-q:v", "4", "-pix_fmt", "yuvj420p"]);
        args.AddRange(["-f", "image2", "-update", "1", output]);

        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, null, captureOutput: false, ct).ConfigureAwait(false);
        return result.ExitCode == 0 && File.Exists(output) ? await File.ReadAllBytesAsync(output, ct).ConfigureAwait(false) : null;
    }
}
