using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FileDial.Core.Archives;
using FileDial.Core.Documents;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileDial.Core.Tests;

public class PageRangeTests
{
    [Theory]
    [InlineData("1-3, 5", 10, new[] { 1, 2, 3, 5 })]
    [InlineData("8-", 10, new[] { 8, 9, 10 })]
    [InlineData("-2", 10, new[] { 1, 2 })]
    [InlineData("3; 1", 10, new[] { 3, 1 })] // порядок записи сохраняется
    [InlineData("2–4", 10, new[] { 2, 3, 4 })] // тире из Word
    [InlineData("1-3, 2-4", 10, new[] { 1, 2, 3, 4 })] // без повторов
    public void Parses(string text, int count, int[] expected)
    {
        Assert.Equal(expected, PageRange.TryParse(text, count, out var error));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("", "Укажите")]
    [InlineData("abc", "Непонятно")]
    [InlineData("0", "с 1")]
    [InlineData("12", "10 страниц")]
    [InlineData("5-2", "начало больше конца")]
    public void Rejects(string text, string reason)
    {
        Assert.Null(PageRange.TryParse(text, 10, out var error));
        Assert.Contains(reason, error);
    }

    [Fact]
    public void Label_CollapsesRuns()
    {
        Assert.Equal("1-3,5,7-8", PageRange.Label([1, 2, 3, 5, 7, 8]));
        Assert.Equal("3,1", PageRange.Label([3, 1]));
    }
}

public sealed class PdfJobTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileDialPdf_" + Guid.NewGuid().ToString("N"), "Отчёты");

    public PdfJobTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);

    private string MakePdf(string name, int pages, string? password = null)
    {
        var path = Path.Combine(_dir, name);
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
            doc.AddPage().Width = PdfSharp.Drawing.XUnit.FromPoint(200 + i); // ширина отличает страницы
        if (password is not null)
        {
            doc.SecuritySettings.UserPassword = password;
            doc.SecuritySettings.OwnerPassword = password;
        }
        doc.Save(path);
        return path;
    }

    private static double[] Widths(string path, string? password = null)
    {
        using var doc = PdfReader.Open(path, password ?? "", PdfDocumentOpenMode.Import);
        return doc.Pages.Cast<PdfPage>().Select(p => Math.Round(p.Width.Point)).ToArray();
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    [Fact]
    public async Task Split_PutsEachPageInFolder_AndKeepsSource()
    {
        var pdf = MakePdf("отчёт.pdf", 3);
        var before = Hash(pdf);

        var result = await new PdfSplitJob().RunAsync(pdf, null, CancellationToken.None);

        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Equal(Path.Combine(_dir, "отчёт (страницы)"), result.OutputPath);
        var parts = Directory.GetFiles(result.OutputPath!).Order().ToArray();
        Assert.Equal(["отчёт — стр. 01.pdf", "отчёт — стр. 02.pdf", "отчёт — стр. 03.pdf"], parts.Select(Path.GetFileName));
        Assert.Equal([201], Widths(parts[1]));
        Assert.Equal(before, Hash(pdf));
        Assert.Empty(Directory.GetDirectories(_dir, ".filedial-*"));
    }

    [Fact]
    public async Task Split_SinglePage_NothingToDo()
    {
        var result = await new PdfSplitJob().RunAsync(MakePdf("one.pdf", 1), null, CancellationToken.None);
        Assert.Equal(JobStatus.NothingToDo, result.Status);
    }

    [Fact]
    public async Task Extract_KeepsChosenOrder()
    {
        var pdf = MakePdf("a.pdf", 4);
        var result = await new PdfExtractJob([4, 1]).RunAsync(pdf, null, CancellationToken.None);
        Assert.Equal("a_стр_4,1.pdf", Path.GetFileName(result.OutputPath));
        Assert.Equal([203, 200], Widths(result.OutputPath!));
    }

    [Fact]
    public async Task Rotate_AddsDegrees_ToEveryPage()
    {
        var pdf = MakePdf("a.pdf", 2);
        var right = await new PdfRotateJob(90).RunAsync(pdf, null, CancellationToken.None);
        var left = await new PdfRotateJob(-90).RunAsync(right.OutputPath!, null, CancellationToken.None);

        Assert.Equal("a_повёрнут.pdf", Path.GetFileName(right.OutputPath));
        using (var doc = PdfReader.Open(right.OutputPath!, PdfDocumentOpenMode.Import))
            Assert.All(doc.Pages.Cast<PdfPage>(), p => Assert.Equal(90, p.Rotate));
        using (var doc = PdfReader.Open(left.OutputPath!, PdfDocumentOpenMode.Import))
            Assert.All(doc.Pages.Cast<PdfPage>(), p => Assert.Equal(0, p.Rotate));
    }

    [Fact]
    public async Task Merge_InSelectionOrder_NamedAfterFolder()
    {
        var a = MakePdf("b.pdf", 2);
        var b = MakePdf("a.pdf", 1);
        var result = await new PdfMergeJob().RunAsync([a, b], null, CancellationToken.None);

        Assert.Equal("Отчёты_объединено.pdf", Path.GetFileName(result.OutputPath));
        Assert.Equal([200, 201, 200], Widths(result.OutputPath!));
        Assert.Contains("3 страницы", result.Message);
    }

    [Fact]
    public async Task Merge_LockedFile_ExplainsWhich()
    {
        var a = MakePdf("open.pdf", 1);
        var b = MakePdf("закрыт.pdf", 1, "secret1");
        var ex = await Assert.ThrowsAnyAsync<Compression.CompressionException>(() =>
            new PdfMergeJob().RunAsync([a, b], null, CancellationToken.None));
        Assert.Contains("закрыт.pdf", ex.Message);
    }

    [Fact]
    public async Task Protect_ThenOpenOnlyWithPassword()
    {
        var pdf = MakePdf("a.pdf", 2);
        var result = await new PdfProtectJob("пароль-123").RunAsync(pdf, null, CancellationToken.None);

        Assert.Equal("a_с_паролем.pdf", Path.GetFileName(result.OutputPath));
        Assert.Equal([200, 201], Widths(result.OutputPath!, "пароль-123"));
        var locked = Assert.Throws<PasswordRequiredException>(() => PdfFiles.Open(result.OutputPath!, null, PdfDocumentOpenMode.Import));
        Assert.False(locked.WrongPassword);
        Assert.Equal(PdfFiles.Locked, locked.Message);
    }

    [Fact]
    public async Task LockedSource_AsksPassword_ThenWorks()
    {
        var pdf = MakePdf("a.pdf", 2, "secret1");
        var job = new PdfRotateJob(90);

        var first = await Assert.ThrowsAsync<PasswordRequiredException>(() => job.RunAsync(pdf, null, CancellationToken.None));
        Assert.False(first.WrongPassword);
        var wrong = await Assert.ThrowsAsync<PasswordRequiredException>(() =>
            job.WithPassword("nope").RunAsync(pdf, null, CancellationToken.None));
        Assert.True(wrong.WrongPassword);

        var result = await job.WithPassword("secret1").RunAsync(pdf, null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, result.Status);
        // Копия из защищённого файла собрана заново — уже без пароля.
        Assert.Equal([200, 201], Widths(result.OutputPath!));
    }

    [Fact]
    public void PageCount_ForMenu()
    {
        Assert.Equal(3, PdfFiles.TryPageCount(MakePdf("a.pdf", 3), TimeSpan.FromSeconds(10)));
        Assert.Null(PdfFiles.TryPageCount(MakePdf("b.pdf", 3, "secret1"), TimeSpan.FromSeconds(10)));
        var broken = Path.Combine(_dir, "broken.pdf");
        File.WriteAllText(broken, "не pdf");
        Assert.Null(PdfFiles.TryPageCount(broken, TimeSpan.FromSeconds(10)));
    }
}

public sealed class OfficeDocTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileDialDoc_" + Guid.NewGuid().ToString("N"));

    public OfficeDocTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private string MakeDocx(string name = "письмо.docx")
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"/>""");
        Add(zip, "word/document.xml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <w:document xmlns:w="{WNs}"><w:body>
              <w:p><w:r><w:t>Привет,</w:t></w:r><w:r><w:t xml:space="preserve"> мир</w:t></w:r></w:p>
              <w:p><w:ins w:id="1" w:author="Иван Петров"><w:r><w:t>Вставка</w:t><w:tab/><w:t>после табуляции</w:t></w:r></w:ins></w:p>
              <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Ячейка</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
            </w:body></w:document>
            """);
        Add(zip, "word/comments.xml", $"""<?xml version="1.0" encoding="UTF-8"?><w:comments xmlns:w="{WNs}"><w:comment w:id="0" w:author="Иван Петров" w:initials="ИП"><w:p><w:r><w:t>Замечание</w:t></w:r></w:p></w:comment></w:comments>""");
        Add(zip, "docProps/core.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/">
              <dc:title>Письмо</dc:title><dc:creator>Иван Петров</dc:creator><cp:lastModifiedBy>Мария</cp:lastModifiedBy><cp:lastPrinted>2026-01-01T00:00:00Z</cp:lastPrinted>
            </cp:coreProperties>
            """);
        Add(zip, "docProps/app.xml", """<?xml version="1.0" encoding="UTF-8"?><Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"><Company>ООО Ромашка</Company><Pages>1</Pages></Properties>""");
        Add(zip, "word/_rels/settings.xml.rels", """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/attachedTemplate" Target="file:///C:\Users\ivan\Documents\Бланк.dotx" TargetMode="External"/></Relationships>""");
        var image = zip.CreateEntry("word/media/image1.png");
        using (var s = image.Open())
            s.Write([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]);
        return path;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static string Part(string package, string name)
    {
        using var zip = ZipFile.OpenRead(package);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void Docx_TextByParagraphs()
    {
        var text = OfficeDocs.ExtractText(MakeDocx());
        Assert.Equal(["Привет, мир", "Вставка\tпосле табуляции", "Ячейка"],
            text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task TextJob_WritesUtf8WithBom()
    {
        var result = await new DocumentTextJob().RunAsync(MakeDocx(), null, CancellationToken.None);
        Assert.Equal("письмо.txt", Path.GetFileName(result.OutputPath));
        var bytes = File.ReadAllBytes(result.OutputPath!);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("6 слов", result.Message);
    }

    [Fact]
    public async Task Images_CopiedAsIs()
    {
        var result = await new DocumentImagesJob().RunAsync(MakeDocx(), null, CancellationToken.None);
        Assert.Equal(Path.Combine(_dir, "письмо (картинки)"), result.OutputPath);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 1, 2, 3], File.ReadAllBytes(Path.Combine(result.OutputPath!, "image1.png")));
    }

    [Fact]
    public async Task Strip_RemovesNames_KeepsContent()
    {
        var source = MakeDocx();
        var before = File.ReadAllBytes(source);
        var result = await new DocumentStripJob().RunAsync(source, null, CancellationToken.None);

        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Equal("письмо_без_данных.docx", Path.GetFileName(result.OutputPath));
        Assert.Equal(before, File.ReadAllBytes(source));

        var out_ = result.OutputPath!;
        var core = Part(out_, "docProps/core.xml");
        Assert.DoesNotContain("Иван", core);
        Assert.DoesNotContain("Мария", core);
        Assert.DoesNotContain("lastPrinted", core);
        Assert.Contains("Письмо", core); // заголовок — не личные данные
        Assert.DoesNotContain("Ромашка", Part(out_, "docProps/app.xml"));
        Assert.DoesNotContain("Иван", Part(out_, "word/comments.xml"));
        Assert.DoesNotContain("Иван", Part(out_, "word/document.xml"));
        Assert.DoesNotContain("ivan", Part(out_, "word/_rels/settings.xml.rels"));
        Assert.Contains("Вставка", Part(out_, "word/document.xml"));
        Assert.Contains("имена у комментариев", result.Message);

        // Повторная очистка — уже нечего убирать.
        var again = await new DocumentStripJob().RunAsync(out_, null, CancellationToken.None);
        Assert.Equal(JobStatus.NothingToDo, again.Status);
    }

    [Fact]
    public async Task Odt_TextAndMeta()
    {
        var path = Path.Combine(_dir, "заметка.odt");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            Add(zip, "content.xml", """
                <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <office:body><office:text>
                    <text:h>Заголовок</text:h>
                    <text:p>Раз<text:s text:c="2"/>два<text:tab/>три<office:annotation><dc:creator>Иван</dc:creator><text:p>коммент</text:p></office:annotation></text:p>
                  </office:text></office:body>
                </office:document-content>
                """);
            Add(zip, "meta.xml", """
                <office:document-meta xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:meta="urn:oasis:names:tc:opendocument:xmlns:meta:1.0" xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <office:meta><meta:initial-creator>Иван</meta:initial-creator><dc:creator>Иван</dc:creator><dc:title>Заметка</dc:title></office:meta>
                </office:document-meta>
                """);
        }

        Assert.Equal(["Заголовок", "Раз  два\tтри"],
            OfficeDocs.ExtractText(path).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));

        var result = await new DocumentStripJob().RunAsync(path, null, CancellationToken.None);
        Assert.DoesNotContain("Иван", Part(result.OutputPath!, "meta.xml"));
        Assert.DoesNotContain("Иван", Part(result.OutputPath!, "content.xml"));
        using var zipOut = ZipFile.OpenRead(result.OutputPath!);
        Assert.Equal("mimetype", zipOut.Entries[0].FullName); // OpenDocument требует mimetype первым
    }

    [Fact]
    public async Task EncryptedOffice_ClearError()
    {
        // Документ Office под паролем — не zip.
        var path = Path.Combine(_dir, "секрет.docx");
        File.WriteAllBytes(path, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0]);
        var ex = await Assert.ThrowsAnyAsync<Compression.CompressionException>(() =>
            new DocumentStripJob().RunAsync(path, null, CancellationToken.None));
        Assert.Contains("защищён паролем или повреждён", ex.Message);
    }
}

public class DocumentCatalogTests
{
    [Fact]
    public void Pdf_SinglePage_NoSplit_NoMerge()
    {
        var actions = ActionCatalog.ForFile(@"C:\d\a.pdf", 1, pages: 1);
        Assert.Equal(8, actions.Count);
        Assert.False(actions.Single(a => a.Id == ActionIds.PdfSplit).IsEnabled);
        Assert.Equal("Выделите несколько PDF", actions.Single(a => a.Id == ActionIds.PdfMerge).DisabledReason);
        Assert.True(actions.Single(a => a.Id == ActionIds.PdfRotateRight).IsEnabled);
    }

    [Fact]
    public void Pdf_Batch_MergeEnabled_ExtractPerFile()
    {
        var actions = ActionCatalog.ForBatch(FileKind.Pdf, [@"C:\d\a.pdf", @"C:\d\b.pdf"], [1, 1]);
        Assert.True(actions.Single(a => a.Id == ActionIds.PdfMerge).IsEnabled);
        Assert.False(actions.Single(a => a.Id == ActionIds.PdfExtract).IsEnabled);
    }

    [Fact]
    public void LegacyFormats_PackageActionsDisabled()
    {
        var actions = ActionCatalog.ForFile(@"C:\d\старый.doc", 1);
        Assert.Equal(OfficeDocs.PackageOnly, actions.Single(a => a.Id == ActionIds.DocStrip).DisabledReason);
        Assert.Equal(OfficeDocs.PackageOnly, actions.Single(a => a.Id == ActionIds.DocImages).DisabledReason);
        // Свой формат в «Формат» не повторяется только для новых форматов: DOC → DOCX — да.
        var formats = actions.Single(a => a.Id == ActionIds.DocConvert).Children!;
        Assert.Contains(formats, c => c.Id == ActionIds.DocTo(".docx"));
    }

    [Fact]
    public void Sheet_FormatsAndText()
    {
        var actions = ActionCatalog.ForFile(@"C:\d\бюджет.xlsx", 1);
        var formats = actions.Single(a => a.Id == ActionIds.DocConvert).Children!;
        Assert.Equal(["XLSX", "ODS", "CSV"], formats.Select(c => c.Label));
        Assert.Equal("Файл уже в XLSX", formats[0].DisabledReason);
        Assert.False(actions.Single(a => a.Id == ActionIds.DocText).IsEnabled);
        Assert.True(actions.Single(a => a.Id == ActionIds.DocStrip).IsEnabled);
    }

    [Fact]
    public void Kinds()
    {
        Assert.Equal(FileKind.Pdf, FileKinds.Of(@"C:\a.PDF"));
        Assert.Equal(FileKind.Document, FileKinds.Of(@"C:\a.docx"));
        Assert.Equal(FileKind.Document, FileKinds.Of(@"C:\a.odp"));
        Assert.Equal(FileKind.Other, FileKinds.Of(@"C:\a.psd"));
        Assert.Equal("3 документа", SelectionInterpreter.KindCount(FileKind.Document, 3));
    }

    [Fact]
    public void Jobs_NoFfmpegNeeded()
    {
        foreach (var id in new[] { ActionIds.PdfSplit, ActionIds.PdfMerge, ActionIds.DocToPdf, ActionIds.DocStrip, ActionIds.DocTo(".odt") })
        {
            Assert.False(FileJobs.NeedsFfmpeg(id), id);
            Assert.NotNull(FileJobs.Create(id, null));
        }
        Assert.True(FileJobs.NeedsFfmpeg(ActionIds.DocCompress));
        Assert.True(FileJobs.NeedsNewPassword(ActionIds.PdfProtect));
        Assert.True(FileJobs.NeedsPageRange(ActionIds.PdfExtract));
    }
}
