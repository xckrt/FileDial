using System.Runtime.InteropServices;
using System.Text;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Documents;
using FileDial.Core.Jobs;
using FileDial.Core.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using WinPdf = Windows.Data.Pdf;

namespace FileDial.App.Documents;

/// <summary>
/// PDF средствами самой Windows (тот же движок, что у просмотра PDF в Edge): отрисовка страниц и распознавание текста.
/// Сторонних библиотек не нужно, работает без интернета.
/// </summary>
internal static class WindowsPdf
{
    // ERROR_WRONG_PASSWORD: документ под паролем, а пароль не дан или неверен.
    private const int WrongPassword = unchecked((int)0x8007052B);

    public static async Task<WinPdf.PdfDocument> LoadAsync(string path, string? password)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            return password is null
                ? await WinPdf.PdfDocument.LoadFromFileAsync(file)
                : await WinPdf.PdfDocument.LoadFromFileAsync(file, password);
        }
        catch (Exception ex) when (ex.HResult == WrongPassword)
        {
            throw new PasswordRequiredException(password is not null, PdfFiles.Locked);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or UnauthorizedAccessException)
        {
            throw new CompressionException($"Windows не смогла открыть {Path.GetFileName(path)}: файл повреждён или это не PDF.",
                ex.Message);
        }
    }

    /// <summary>Страница в PNG. Масштаб — точек на дюйм; длинная сторона не больше <paramref name="maxSide"/>.</summary>
    public static async Task<InMemoryRandomAccessStream> RenderAsync(WinPdf.PdfPage page, double dpi, uint maxSide)
    {
        // Размер страницы — в единицах 1/96 дюйма.
        var scale = dpi / 96;
        var longSide = Math.Max(page.Size.Width, page.Size.Height) * scale;
        if (longSide > maxSide)
            scale *= maxSide / longSide;

        var options = new WinPdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * scale)),
            DestinationHeight = (uint)Math.Max(1, Math.Round(page.Size.Height * scale)),
            // Белый фон: у многих PDF страница прозрачная, а картинка с прозрачностью выглядит пустой в просмотрщиках.
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
        };
        var stream = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, options);
        stream.Seek(0);
        return stream;
    }
}

/// <summary>«Страницы в картинки»: каждая страница — PNG 150 dpi в папке «имя (картинки)».</summary>
internal sealed class PdfImagesJob(string? outputDirectory = null) : IFileJob, IPasswordAware
{
    private const double Dpi = 150;
    private const uint MaxSide = 5000;

    public string? Password { get; init; }

    public IFileJob WithPassword(string password) => new PdfImagesJob(outputDirectory) { Password = password };

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress("Чтение PDF", null));
        var doc = await WindowsPdf.LoadAsync(sourcePath, Password);
        var count = (int)doc.PageCount;
        var parent = outputDirectory ?? Path.GetDirectoryName(sourcePath)!;
        // Грубая оценка: страница A4 в 150 dpi — около мегабайта PNG.
        if (OutputFolderCheck.WhyNot(parent, count * 1_500_000L) is { } problem)
            throw new CompressionException(problem);

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var digits = Math.Max(2, count.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        var staging = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            for (var i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new JobProgress($"Страница {i + 1} из {count}", (double)i / count));
                using var page = doc.GetPage((uint)i);
                using var png = await WindowsPdf.RenderAsync(page, Dpi, MaxSide);
                await using var file = File.Create(Path.Combine(staging, $"{stem} — стр. {(i + 1).ToString().PadLeft(digits, '0')}.png"));
                await png.AsStreamForRead().CopyToAsync(file, ct);
            }

            var result = FreeNames.Child(parent, $"{stem} (картинки)", isDirectory: true);
            Directory.Move(staging, result);
            progress?.Report(new JobProgress("Готово", 1));
            return new JobResult(JobStatus.Done,
                $"{Plural.Count(count, "картинка", "картинки", "картинок")} → папка {Path.GetFileName(result)}.", result);
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(staging);
        }
    }
}

/// <summary>
/// «Распознать текст»: страницы отрисовываются и распознаются встроенным в Windows OCR на языках из параметров Windows.
/// Подходит и для сканов, и для обычных PDF; результат — «имя.txt».
/// </summary>
internal sealed class PdfOcrJob(string? outputDirectory = null) : IFileJob, IPasswordAware
{
    // 300 dpi — как у сканера; мелкий шрифт иначе распознаётся хуже.
    private const double Dpi = 300;

    public string? Password { get; init; }

    public IFileJob WithPassword(string password) => new PdfOcrJob(outputDirectory) { Password = password };

    public static string? WhyNot() =>
        OcrEngine.AvailableRecognizerLanguages.Count == 0
            ? "В Windows нет языка для распознавания текста: Параметры → Время и язык → Язык → ваш язык → Распознавание текста."
            : null;

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? (OcrEngine.AvailableRecognizerLanguages.FirstOrDefault() is { } language
                         ? OcrEngine.TryCreateFromLanguage(language)
                         : null)
                     ?? throw new CompressionException(WhyNot() ?? "Распознавание текста недоступно.");

        progress?.Report(new JobProgress("Чтение PDF", null));
        var doc = await WindowsPdf.LoadAsync(sourcePath, Password);
        var count = (int)doc.PageCount;
        var directory = outputDirectory ?? Path.GetDirectoryName(sourcePath)!;
        if (OutputFolderCheck.WhyNot(directory, count * 20_000L + 1_000_000) is { } problem)
            throw new CompressionException(problem);

        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress($"Распознавание · страница {i + 1} из {count}", (double)i / count));
            using var page = doc.GetPage((uint)i);
            using var png = await WindowsPdf.RenderAsync(page, Dpi, OcrEngine.MaxImageDimension);
            var decoder = await BitmapDecoder.CreateAsync(png);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(bitmap);

            if (count > 1)
            {
                if (i > 0)
                    text.AppendLine();
                text.AppendLine($"— Страница {i + 1} —");
            }
            foreach (var line in result.Lines)
                text.AppendLine(line.Text);
        }

        var words = text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(w => !w.StartsWith('—') && !w.All(char.IsDigit));
        if (words == 0)
            return new JobResult(JobStatus.NothingToDo, "Текст на страницах не найден.");

        var temp = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temp, text.ToString(), new UTF8Encoding(true), ct);
            var output = OutputFiles.MoveToFreeName(temp, () =>
                OutputNameGenerator.Generate(sourcePath, "", ".txt", OutputNameGenerator.PathTaken, directory));
            progress?.Report(new JobProgress("Готово", 1));
            return new JobResult(JobStatus.Done,
                $"{PdfFiles.PagesLabel(count)}, язык: {engine.RecognizerLanguage.DisplayName}. Сохранено как {Path.GetFileName(output)}.",
                output);
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }
}
