using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text;
using FileDial.Core.Archives;
using FileDial.Core.Documents;
using FileDial.Core.Setup;
using Microsoft.Win32;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FileDial.Core.Tests;

/// <summary>Архивы, PDF, документы и установщик по-английски: тексты и имена результатов.</summary>
[Collection(LanguageCollection.Name)]
public sealed class LocalizationDocsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileDialLoc_" + Guid.NewGuid().ToString("N"), "Reports");

    public LocalizationDocsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);

    private string MakePdf(string name, int pages, string? password = null)
    {
        var path = Path.Combine(_dir, name);
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
            doc.AddPage();
        if (password is not null)
        {
            doc.SecuritySettings.UserPassword = password;
            doc.SecuritySettings.OwnerPassword = password;
        }
        doc.Save(path);
        return path;
    }

    [Fact]
    public async Task PdfSplit_EnglishFolderAndPageNames()
    {
        using var _ = new EnglishScope();
        var result = await new PdfSplitJob().RunAsync(MakePdf("report.pdf", 2), null, CancellationToken.None);

        Assert.Equal(Path.Combine(_dir, "report (pages)"), result.OutputPath);
        Assert.Equal(["report — page 01.pdf", "report — page 02.pdf"],
            Directory.GetFiles(result.OutputPath!).Order().Select(Path.GetFileName));
        Assert.Equal("2 pages → folder report (pages).", result.Message);
    }

    [Fact]
    public async Task PdfRotateExtractMergeProtect_EnglishSuffixes()
    {
        using var _ = new EnglishScope();
        var pdf = MakePdf("a.pdf", 3);

        var rotated = await new PdfRotateJob(90).RunAsync(pdf, null, CancellationToken.None);
        Assert.Equal("a_rotated.pdf", Path.GetFileName(rotated.OutputPath));
        Assert.Equal("3 pages rotated right. Saved as a_rotated.pdf.", rotated.Message);

        var extracted = await new PdfExtractJob([3, 1]).RunAsync(pdf, null, CancellationToken.None);
        Assert.Equal("a_pages_3,1.pdf", Path.GetFileName(extracted.OutputPath));
        Assert.StartsWith("2 pages of 3.", extracted.Message);

        var merged = await new PdfMergeJob().RunAsync([pdf, MakePdf("b.pdf", 1)], null, CancellationToken.None);
        Assert.Equal("Reports_merged.pdf", Path.GetFileName(merged.OutputPath));
        Assert.StartsWith("2 files, 4 pages.", merged.Message);

        var protectedPdf = await new PdfProtectJob("secret1").RunAsync(pdf, null, CancellationToken.None);
        Assert.Equal("a_protected.pdf", Path.GetFileName(protectedPdf.OutputPath));
        var locked = Assert.Throws<PasswordRequiredException>(() =>
            PdfFiles.Open(protectedPdf.OutputPath!, null, PdfDocumentOpenMode.Import));
        Assert.Equal("The PDF is password-protected.", locked.Message);
    }

    [Fact]
    public async Task PdfMerge_LockedFile_EnglishError()
    {
        using var _ = new EnglishScope();
        var ex = await Assert.ThrowsAnyAsync<Compression.CompressionException>(() =>
            new PdfMergeJob().RunAsync([MakePdf("open.pdf", 1), MakePdf("closed.pdf", 1, "secret1")], null, CancellationToken.None));
        Assert.Equal("closed.pdf is password-protected. Remove the password and try again.", ex.Message);
    }

    [Theory]
    [InlineData("", "Enter pages, for example 1-3, 5")]
    [InlineData("abc", "Not understood: “abc”")]
    [InlineData("0", "Pages start at 1")]
    [InlineData("12", "The document has 10 pages")]
    [InlineData("5-2", "“5-2”: the start is after the end")]
    public void PageRange_EnglishErrors(string text, string expected)
    {
        using var _ = new EnglishScope();
        Assert.Null(PageRange.TryParse(text, 10, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void PasswordRequired_EnglishMessages()
    {
        using var _ = new EnglishScope();
        Assert.Equal("The archive is password-protected.", new PasswordRequiredException(false).Message);
        Assert.Equal("Wrong password.", new PasswordRequiredException(true).Message);
        Assert.Equal("The PDF is password-protected.", new PasswordRequiredException(false, PdfFiles.Locked).Message);
    }

    private const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private string MakeDocx(string name)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "word/document.xml",
            $"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="{WNs}"><w:body><w:p><w:r><w:t>Hello</w:t></w:r></w:p></w:body></w:document>""");
        Add(zip, "word/comments.xml",
            $"""<?xml version="1.0" encoding="UTF-8"?><w:comments xmlns:w="{WNs}"><w:comment w:id="0" w:author="John Smith" w:initials="JS"><w:p><w:r><w:t>Note</w:t></w:r></w:p></w:comment></w:comments>""");
        var image = zip.CreateEntry("word/media/image1.png");
        using (var s = image.Open())
            s.Write([0x89, 0x50, 0x4E, 0x47]);
        return path;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Part(string package, string name)
    {
        using var zip = ZipFile.OpenRead(package);
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task DocumentStrip_WritesEnglishAuthor()
    {
        using var _ = new EnglishScope();
        var result = await new DocumentStripJob().RunAsync(MakeDocx("letter.docx"), null, CancellationToken.None);

        Assert.Equal("letter_clean.docx", Path.GetFileName(result.OutputPath));
        var comments = Part(result.OutputPath!, "word/comments.xml");
        Assert.Contains("w:author=\"Author\"", comments);
        Assert.Contains("w:initials=\"A\"", comments);
        Assert.DoesNotContain("John", comments);
        Assert.Equal("Removed: names on comments. Saved as letter_clean.docx.", result.Message);
    }

    [Fact]
    public async Task DocumentImages_EnglishFolder()
    {
        using var _ = new EnglishScope();
        var result = await new DocumentImagesJob().RunAsync(MakeDocx("letter.docx"), null, CancellationToken.None);
        Assert.Equal(Path.Combine(_dir, "letter (images)"), result.OutputPath);
        Assert.Equal("1 image → folder letter (images).", result.Message);
    }

    [Fact]
    public void Slides_EnglishMarker()
    {
        using var _ = new EnglishScope();
        var path = Path.Combine(_dir, "deck.pptx");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            Add(zip, "ppt/slides/slide1.xml",
                """<p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"><a:p><a:r><a:t>Title</a:t></a:r></a:p></p:sld>""");
        Assert.StartsWith("— Slide 1 —", OfficeDocs.ExtractText(path));
    }

    [Fact]
    public void DocumentCatalog_EnglishReasons()
    {
        using var _ = new EnglishScope();
        Assert.Equal("Only for DOCX, XLSX, PPTX and OpenDocument", OfficeDocs.PackageOnly);
        Assert.Equal("Needs Microsoft Office or LibreOffice", DocumentConverters.NoConverter);
    }

    private string MakeZip(string name)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "a.txt", "one");
        Add(zip, "b.txt", "two");
        return path;
    }

    [Fact]
    public async Task Archives_EnglishMessagesAndSuffix()
    {
        using var _ = new EnglishScope();
        var zip = MakeZip("docs.zip");

        var extracted = await new ArchiveExtractJob(ExtractMode.ToFolder).RunAsync(zip, null, CancellationToken.None);
        Assert.Equal("Extracted 2 files → docs.", extracted.Message);

        var protectedZip = await new ArchiveProtectJob("secret1").RunAsync(zip, null, CancellationToken.None);
        Assert.Equal("docs_protected.zip", Path.GetFileName(protectedZip.OutputPath));
        Assert.StartsWith("Created a password-protected archive: docs_protected.zip (2 files, AES-256).", protectedZip.Message);

        Assert.Equal("10 MB", FileParts.Presets[0].Label);
        Assert.Equal("4 GB (FAT32)", FileParts.Presets[^1].Label);
        Assert.Equal("The first part (.001) was not found.", FileParts.Problem([]));
    }

    [Fact]
    public async Task SplitJoin_EnglishMessages()
    {
        using var _ = new EnglishScope();
        var source = Path.Combine(_dir, "big.bin");
        File.WriteAllBytes(source, new byte[2500]);

        var split = await new FileSplitJob(1000).RunAsync(source, null, CancellationToken.None);
        Assert.StartsWith("3 parts of ", split.Message);
        File.Delete(source);

        var joined = await new FileJoinJob().RunAsync(split.OutputPath!, null, CancellationToken.None);
        Assert.StartsWith("Joined from 3 parts: big.bin", joined.Message);
    }
}

[SupportedOSPlatform("windows")]
[Collection(LanguageCollection.Name)]
public sealed class LocalizationInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialSetupLoc_" + Guid.NewGuid().ToString("N"));
    private readonly string _registry = @"Software\FileDialTests\" + Guid.NewGuid().ToString("N");

    public LocalizationInstallerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_registry, throwOnMissingSubKey: false);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void WhyNot_InEnglish()
    {
        using var _ = new EnglishScope();
        Assert.Equal("Network folders won't work. Choose a folder on this computer.", Installer.WhyNot(@"\\server\share\FileDial", 1000));
        Assert.Equal("Enter the full path to the folder.", Installer.WhyNot("FileDial", 1000));
        Assert.StartsWith("Not enough space:", Installer.WhyNot(Path.Combine(_root, "FileDial"), long.MaxValue / 2));
    }

    [Fact]
    public async Task Install_EnglishStagesAndComments()
    {
        using var _ = new EnglishScope();
        var env = new InstallEnvironment
        {
            UninstallKey = _registry + @"\Uninstall\FileDial",
            ClassesKey = _registry + @"\Classes",
            RunKey = _registry + @"\Run",
            StartMenuFolder = Path.Combine(_root, "Start"),
            DesktopFolder = Path.Combine(_root, "Desktop"),
            SettingsPath = Path.Combine(_root, "Roaming", "FileDial", "settings.json"),
            DataFolders = [Path.Combine(_root, "Roaming", "FileDial")],
        };
        var source = Path.Combine(_root, "FileDial-Setup.exe");
        File.WriteAllBytes(source, new byte[1000]);
        var stages = new List<string>();

        await new Installer(env).InstallAsync(source, "1.0",
            new InstallOptions(Path.Combine(_root, "App", "FileDial"), Autostart: false, StartMenuShortcut: false),
            new SyncProgress(p => stages.Add(p.Stage)), CancellationToken.None);

        using var key = Registry.CurrentUser.OpenSubKey(env.UninstallKey)!;
        Assert.Equal("Radial action menu for files", key.GetValue("Comments"));
        Assert.Equal("FileDial", key.GetValue("DisplayName"));
        using var verb = Registry.CurrentUser.OpenSubKey(env.ClassesKey + @"\*\shell\FileDial")!;
        Assert.Equal("Open in FileDial…", verb.GetValue(""));
        Assert.Contains("Copying the program", stages);
        Assert.Equal("Done", stages[^1]);
    }

    /// <summary>Progress&lt;T&gt; сообщает асинхронно — здесь нужен синхронный.</summary>
    private sealed class SyncProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }
}
