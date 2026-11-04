using System.Text;
using FileDial.Core.Compression;
using FileDial.Core.Documents;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;
using FileDial.Core.Menu;

namespace FileDial.Core.TextFiles;

/// <summary>Задачи для текстовых файлов, SVG и значков: что создать для действия и нужен ли ffmpeg.</summary>
public static class TextFileJobs
{
    private static readonly HashSet<string> WithoutFfmpeg =
    [
        ActionIds.TextToUtf8, ActionIds.TextToCrlf, ActionIds.TextToLf, ActionIds.CsvToXlsx, ActionIds.JsonFormat,
        ActionIds.JsonMinify, ActionIds.MarkdownToHtml, ActionIds.MarkdownToPdf, ActionIds.IcoToPng, ActionIds.IcoAllPng,
    ];

    private static readonly HashSet<string> WithFfmpeg =
        [ActionIds.SvgToPng, ActionIds.SvgPngSizes, ActionIds.SvgToIco, ActionIds.ImageToIco];

    /// <summary>Действие из этого набора (а не видео, архивы и т. п.).</summary>
    public static bool IsOwn(string actionId) => WithoutFfmpeg.Contains(actionId) || WithFfmpeg.Contains(actionId);

    public static bool RunsWithoutFfmpeg(string actionId) => WithoutFfmpeg.Contains(actionId);

    /// <summary>Задача или null (не наше действие или для него нужен ffmpeg, а его нет).</summary>
    public static IFileJob? Create(string actionId, FfmpegTools? tools, string? output)
    {
        IFileJob? plain = actionId switch
        {
            ActionIds.TextToUtf8 => new TextToUtf8Job(output),
            ActionIds.TextToCrlf => new LineEndingsJob(LineEndingStyle.Crlf, output),
            ActionIds.TextToLf => new LineEndingsJob(LineEndingStyle.Lf, output),
            ActionIds.CsvToXlsx => new CsvToXlsxJob(output),
            ActionIds.JsonFormat => new JsonJob(indented: true, output),
            ActionIds.JsonMinify => new JsonJob(indented: false, output),
            ActionIds.MarkdownToHtml => new MarkdownHtmlJob(output),
            ActionIds.MarkdownToPdf => new MarkdownPdfJob(output),
            ActionIds.IcoToPng => new IcoToPngJob(output),
            ActionIds.IcoAllPng => new IcoAllPngJob(output),
            _ => null,
        };
        if (plain is not null || tools is null)
            return plain;
        return actionId switch
        {
            ActionIds.SvgToPng => new SvgToPngJob(tools, output),
            ActionIds.SvgPngSizes => new SvgPngSizesJob(tools, output),
            ActionIds.SvgToIco => new ToIcoJob(tools, output),
            _ when actionId == ActionIds.ImageToIco => new ToIcoJob(tools, output),
            _ => null,
        };
    }

    /// <summary>Расширения, к которым относится действие; null — к любому текстовому файлу.</summary>
    public static bool IsCsv(string path) => Path.GetExtension(path).ToLowerInvariant() is ".csv" or ".tsv";

    public static bool IsJson(string path) => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);

    public static bool IsMarkdown(string path) => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".markdown";
}

/// <summary>Общее для текстовых задач: чтение с проверкой размера, запись во временный файл и перенос под свободное имя.</summary>
internal static class TextOutput
{
    /// <summary>Текст целиком в памяти: больше — это уже не «файл для блокнота».</summary>
    public const long MaxBytes = 256L * 1024 * 1024;

    public static byte[] ReadSource(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxBytes)
            throw new CompressionException(Lang.T.TxtTooLarge(Text.SizeFormatter.Format(MaxBytes)));
        return File.ReadAllBytes(path);
    }

    /// <summary>Текст и его кодировка; двоичный файл — понятная ошибка.</summary>
    public static (string Text, TextEncodingKind Encoding, LineEndingStyle Style) Decode(byte[] bytes)
    {
        var analysis = TextAnalysis.Of(bytes);
        if (analysis.Encoding == TextEncodingKind.Binary)
            throw new CompressionException(Lang.T.TxtLooksBinary + ".");
        var bom = analysis.Encoding.BomLength();
        var text = analysis.Encoding.Encoding().GetString(bytes, bom, bytes.Length - bom);
        return (text, analysis.Encoding, analysis.LineEndings.Style);
    }

    public static string Save(string sourcePath, string? outputDirectory, string suffix, string extension, byte[] content)
    {
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, content.Length + 1_000_000);
        var temp = DocumentOutput.TempNear(directory);
        try
        {
            File.WriteAllBytes(temp, content);
            return DocumentOutput.Finish(temp, sourcePath, suffix, extension, directory);
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }

    public static string Extension(string path) => Path.GetExtension(path).ToLowerInvariant();

    public static JobResult Done(IProgress<JobProgress>? progress, string message, string output)
    {
        progress?.Report(new JobProgress(Lang.T.JobStageDone, 1));
        return new JobResult(JobStatus.Done, $"{message} {Lang.T.JobSavedAs(Path.GetFileName(output))}", output);
    }
}

/// <summary>
/// «В UTF-8». BOM пишем только у CSV/TSV: без него Excel откроет таблицу в кодировке ANSI. Остальным BOM
/// мешает (JSON по стандарту без него, скрипты и ini читают его как мусор), а Блокнот и редакторы понимают UTF-8 и так.
/// Переводы строк не трогаем — текст перекодируется как есть.
/// </summary>
public sealed class TextToUtf8Job(string? outputDirectory = null) : IFileJob
{
    public static bool WantsBom(string path) => TextFileJobs.IsCsv(path);

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.TxtStageConverting, null));
        var bytes = TextOutput.ReadSource(sourcePath);
        var (text, encoding, _) = TextOutput.Decode(bytes);
        if (encoding.IsUtf8())
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.TxtAlreadyUtf8(encoding.Label()) + "."));
        ct.ThrowIfCancellationRequested();

        var bom = WantsBom(sourcePath);
        var content = new UTF8Encoding(bom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();
        var output = TextOutput.Save(sourcePath, outputDirectory, Lang.T.TxtUtf8Suffix, TextOutput.Extension(sourcePath), content);
        return Task.FromResult(TextOutput.Done(progress, Lang.T.TxtUtf8Done(encoding.Label(), bom), output));
    }
}

/// <summary>Переводы строк в CRLF или LF. Кодировка и BOM не меняются — замена идёт по кодовым единицам.</summary>
public sealed class LineEndingsJob(LineEndingStyle target, string? outputDirectory = null) : IFileJob
{
    public static string Label(LineEndingStyle style) => style == LineEndingStyle.Crlf ? "CRLF" : "LF";

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.TxtStageConverting, null));
        var bytes = TextOutput.ReadSource(sourcePath);
        var analysis = TextAnalysis.Of(bytes);
        if (analysis.Encoding == TextEncodingKind.Binary)
            throw new CompressionException(Lang.T.TxtLooksBinary + ".");
        var style = analysis.LineEndings.Style;
        if (style == LineEndingStyle.None)
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.TxtSingleLine + "."));
        if (style == target)
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.TxtAlreadyEol(Label(target)) + "."));

        var converted = LineBreaks.Convert(bytes, analysis.Encoding, target);
        var suffix = target == LineEndingStyle.Crlf ? Lang.T.TxtCrlfSuffix : Lang.T.TxtLfSuffix;
        var output = TextOutput.Save(sourcePath, outputDirectory, suffix, TextOutput.Extension(sourcePath), converted);
        return Task.FromResult(TextOutput.Done(progress,
            Lang.T.TxtEolDone(analysis.LineEndings.Total, Label(target)), output));
    }
}

/// <summary>«Форматировать JSON» и «Сжать JSON». Результат — UTF-8 без BOM, как требует RFC 8259.</summary>
public sealed class JsonJob(bool indented, string? outputDirectory = null) : IFileJob
{
    public static string NewLineFor(LineEndingStyle style) => style is LineEndingStyle.Lf ? "\n" : "\r\n";

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (!TextFileJobs.IsJson(sourcePath))
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.TxtOnlyJson + "."));
        progress?.Report(new JobProgress(Lang.T.TxtStageConverting, null));
        var (text, _, style) = TextOutput.Decode(TextOutput.ReadSource(sourcePath));

        string result;
        try
        {
            result = JsonText.Rewrite(text, indented, NewLineFor(style));
        }
        catch (JsonSyntaxException ex)
        {
            throw new CompressionException(Lang.T.TxtJsonError(ex.Line, ex.Column) + ".", ex.Message);
        }
        if (result.TrimEnd() == text.TrimEnd())
            return Task.FromResult(new JobResult(JobStatus.NothingToDo,
                (indented ? Lang.T.TxtJsonAlreadyPretty : Lang.T.TxtJsonAlreadyMinified) + "."));

        var content = new UTF8Encoding(false).GetBytes(result);
        var output = TextOutput.Save(sourcePath, outputDirectory,
            indented ? Lang.T.TxtJsonPrettySuffix : Lang.T.TxtJsonMinSuffix, ".json", content);
        var sizes = $"{Text.SizeFormatter.Format(new FileInfo(sourcePath).Length)} → {Text.SizeFormatter.Format(content.Length)}.";
        return Task.FromResult(TextOutput.Done(progress, sizes, output));
    }
}

/// <summary>CSV/TSV → XLSX. Разделитель угадывается, числа становятся числами, остальное — текстом.</summary>
public sealed class CsvToXlsxJob(string? outputDirectory = null) : IFileJob
{
    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (!TextFileJobs.IsCsv(sourcePath))
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.TxtOnlyCsv + "."));
        progress?.Report(new JobProgress(Lang.T.TxtStageTable, null));
        var (text, _, _) = TextOutput.Decode(TextOutput.ReadSource(sourcePath));
        var delimiter = Csv.DetectDelimiter(text, Path.GetExtension(sourcePath));

        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, text.Length * 2L + 1_000_000);
        var temp = DocumentOutput.TempNear(directory);
        try
        {
            XlsxStats stats;
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                try
                {
                    stats = XlsxWriter.Write(stream, XlsxWriter.SheetName(Path.GetFileNameWithoutExtension(sourcePath)),
                        () => Csv.Parse(text, delimiter), delimiter, ct);
                }
                catch (XlsxLimitException ex)
                {
                    throw new CompressionException(ex.Rows
                        ? Lang.T.TxtTooManyRows(XlsxWriter.MaxRows)
                        : Lang.T.TxtTooManyColumns(XlsxWriter.MaxColumns));
                }
            }
            if (stats.Rows == 0)
                return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.TxtCsvEmpty + "."));

            var output = DocumentOutput.Finish(temp, sourcePath, "", ".xlsx", directory);
            var message = Lang.T.TxtXlsxDone(stats.Rows, stats.Columns, DelimiterName(delimiter));
            if (stats.TruncatedCells > 0)
                message += " " + Lang.T.TxtCellsTruncated(stats.TruncatedCells, XlsxWriter.MaxCellLength);
            return Task.FromResult(TextOutput.Done(progress, message, output));
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }

    public static string DelimiterName(char delimiter) => delimiter switch
    {
        ',' => Lang.T.TxtComma,
        ';' => Lang.T.TxtSemicolon,
        _ => Lang.T.TxtTab,
    };
}

/// <summary>Markdown → самодостаточный HTML со встроенными стилями.</summary>
public sealed class MarkdownHtmlJob(string? outputDirectory = null) : IFileJob
{
    /// <summary>Страница из .md. Если HTML ляжет не рядом с исходником, относительные пути становятся абсолютными.</summary>
    public static string Render(string sourcePath, string text, string targetDirectory)
    {
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var sameFolder = string.Equals(Path.GetFullPath(targetDirectory).TrimEnd('\\', '/'), sourceDirectory.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
        var body = Markdown.ToHtml(text, sameFolder ? null : sourceDirectory);
        var title = Markdown.FirstHeading(text) is { Length: > 0 } heading ? heading : Path.GetFileNameWithoutExtension(sourcePath);
        return Markdown.Page(title, body);
    }

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (!TextFileJobs.IsMarkdown(sourcePath))
            return Task.FromResult(new JobResult(JobStatus.NothingToDo, Lang.T.TxtOnlyMarkdown + "."));
        progress?.Report(new JobProgress(Lang.T.TxtStageConverting, null));
        var (text, _, _) = TextOutput.Decode(TextOutput.ReadSource(sourcePath));
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        var html = Render(sourcePath, text, directory);
        var output = TextOutput.Save(sourcePath, outputDirectory, "", ".html", new UTF8Encoding(false).GetBytes(html));
        return Task.FromResult(TextOutput.Done(progress, Lang.T.TxtHtmlDone, output));
    }
}

/// <summary>Markdown → PDF: та же страница печатается Microsoft Edge в фоновом режиме.</summary>
public sealed class MarkdownPdfJob(string? outputDirectory = null) : IFileJob
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (!TextFileJobs.IsMarkdown(sourcePath))
            return new JobResult(JobStatus.NothingToDo, Lang.T.TxtOnlyMarkdown + ".");
        var edge = EdgeBrowser.Find() ?? throw new CompressionException(Lang.T.TxtNeedsEdge + ".");
        progress?.Report(new JobProgress(Lang.T.TxtStagePrinting, null));
        var (text, _, _) = TextOutput.Decode(TextOutput.ReadSource(sourcePath));

        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, text.Length * 4L + 10_000_000);
        var work = DocumentOutput.WorkDirectory();
        try
        {
            // HTML во временной папке: пути к картинкам — абсолютные.
            var html = Path.Combine(work, "page.html");
            await File.WriteAllTextAsync(html, MarkdownHtmlJob.Render(sourcePath, text, work), new UTF8Encoding(false), ct)
                .ConfigureAwait(false);
            var pdf = Path.Combine(work, "page.pdf");
            await EdgeBrowser.PrintToPdfAsync(edge, html, pdf, Path.Combine(work, "profile"), ct).ConfigureAwait(false);

            var temp = DocumentOutput.TempNear(directory);
            File.Move(pdf, temp);
            try
            {
                var output = DocumentOutput.Finish(temp, sourcePath, "", ".pdf", directory);
                return TextOutput.Done(progress, Lang.T.TxtPdfDone, output);
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

/// <summary>Microsoft Edge (есть в Windows 10/11): печать страницы в PDF без окна.</summary>
public static class EdgeBrowser
{
    /// <summary>Для тестов: подменить путь к Edge («» — Edge нет).</summary>
    internal static string? Override { get; set; }

    public static string? Find()
    {
        if (Override is { } forced) return forced.Length == 0 ? null : forced;
        foreach (var root in new[]
                 {
                     Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                     Environment.GetEnvironmentVariable("ProgramFiles"),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var path = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static async Task PrintToPdfAsync(string edge, string htmlPath, string pdfPath, string profile, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(MarkdownPdfJob.Timeout);
        ProcessResult result;
        try
        {
            // Отдельный профиль: не мешаем открытому Edge и не трогаем его настройки.
            result = await ProcessRunner.RunAsync(edge,
            [
                "--headless", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                "--no-pdf-header-footer", "--user-data-dir=" + profile, "--print-to-pdf=" + pdfPath,
                new Uri(htmlPath).AbsoluteUri,
            ], null, captureOutput: false, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CompressionException(Lang.T.TxtEdgeTimeout + ".");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new CompressionException(Lang.T.TxtEdgeFailed + ".", ex.Message);
        }

        if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length < 100 || !StartsWithPdf(pdfPath))
            throw new CompressionException(Lang.T.TxtEdgeFailed + ".", result.ErrorTail);
    }

    private static bool StartsWithPdf(string path)
    {
        Span<byte> head = stackalloc byte[5];
        using var stream = File.OpenRead(path);
        return stream.Read(head) == 5 && head.SequenceEqual("%PDF-"u8);
    }
}
