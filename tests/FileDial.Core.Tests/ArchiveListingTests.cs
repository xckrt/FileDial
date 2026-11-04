using System.Globalization;
using System.Text;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

public class ArchiveListingParseTests
{
    private static readonly Encoding Cp1251;

    static ArchiveListingParseTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    /// <summary>Строка вывода tar как её видит программа: байты cp1251, прочитанные как Latin-1.</summary>
    private static string Latin1(string text) => Encoding.Latin1.GetString(Cp1251.GetBytes(text));

    [Fact]
    public void File_WithCyrillic_AndOctalEscape()
    {
        // «№» (0xB9 в cp1251) tar печатает как \271.
        var line = Latin1("-rw-rw-rw-  0 0      0        5000 ноя 03 13:24 Папка ёжик/файл ") + "\\2711.txt";
        var entry = ArchiveListing.ParseTarLine(line, Cp1251)!;
        Assert.Equal("Папка ёжик/файл №1.txt", entry.Path);
        Assert.Equal(5000, entry.Size);
        Assert.False(entry.IsDirectory);
    }

    [Fact]
    public void Directory_AndEscapedBackslash()
    {
        var dir = ArchiveListing.ParseTarLine(Latin1("drwxrwxrwx  0 0      0           0 ноя 03 13:24 Папка/"), Cp1251)!;
        Assert.True(dir.IsDirectory);
        Assert.Equal(0, dir.Size);

        var slash = ArchiveListing.ParseTarLine("-rw-r--r--  0 0      0          10 Nov  3 2025 a\\\\b.txt", Cp1251)!;
        Assert.Equal(@"a\b.txt", slash.Path);
    }

    [Fact]
    public void Symlink_ShowsOnlyName_GarbageIsIgnored()
    {
        var link = ArchiveListing.ParseTarLine("lrwxrwxrwx  0 0      0           0 Nov  3 13:24 link -> target", Cp1251)!;
        Assert.Equal("link", link.Path);
        Assert.Null(ArchiveListing.ParseTarLine("tar.exe: Error opening archive", Cp1251));
        Assert.Null(ArchiveListing.ParseTarLine("", Cp1251));
    }

    [Fact]
    public void Contents_SingleOnly_InBatch()
    {
        Assert.True(ActionCatalog.ForFile(@"C:\a.zip", 1).Single(a => a.Id == ActionIds.ArchiveContents).IsEnabled);
        var batch = ActionCatalog.ForBatch(FileKind.Archive, [@"C:\a.zip", @"C:\b.zip"], [1, 1]);
        Assert.Equal("Содержимое — по одному архиву", batch.Single(a => a.Id == ActionIds.ArchiveContents).DisabledReason);
        Assert.True(FileJobs.IsImplemented(ActionIds.ExtractHere));
    }

    [Fact]
    public void AnsiSafety_OnRussianWindows()
    {
        if (CultureInfo.CurrentCulture.TextInfo.ANSICodePage != 1251) return;
        Assert.True(ArchiveSupport.IsAnsiSafe(@"C:\Мои документы\Ёжик №1.7z"));
        Assert.False(ArchiveSupport.IsAnsiSafe(@"C:\日本\a.7z"));
        Assert.False(ArchiveSupport.IsAnsiSafe("Ünïcödé")); // без «похожей замены» Ü→U
    }
}

/// <summary>tar.exe и пути с символами вне ANSI-кодировки системы.</summary>
public class ArchiveUnicodePathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public ArchiveUnicodePathTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<string> Make7zAsync(string folder, string archiveName)
    {
        var src = Path.Combine(_root, folder);
        Directory.CreateDirectory(Path.Combine(src, "Альбом", "Ünïcödé"));
        File.WriteAllText(Path.Combine(src, "Альбом", "фото 1.txt"), "раз");
        File.WriteAllText(Path.Combine(src, "Альбом", "Ünïcödé", "über.txt"), "zwei");
        var created = await new ArchiveCreateJob(ArchiveFormat.SevenZip).RunAsync(Path.Combine(src, "Альбом"), null, CancellationToken.None);
        var target = Path.Combine(_root, archiveName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(created.OutputPath!, target);
        return target;
    }

    [CyrillicWindowsFact]
    public async Task Listing_ShowsCyrillicNamesAndSizes()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var archive = await Make7zAsync("src", "альбом.7z");

        var contents = await ArchiveListing.ReadAsync(archive, CancellationToken.None);

        Assert.Contains(contents.Entries, e => e.Path == "Альбом/фото 1.txt" && !e.IsDirectory);
        Assert.Equal(2, contents.Files);
        Assert.True(contents.UnpackedBytes > 0);
    }

    [CyrillicWindowsFact]
    public async Task NestedNonAnsiNames_SurviveRoundTrip()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var archive = await Make7zAsync("src", "альбом.7z");
        var target = Path.Combine(_root, "out");
        Directory.CreateDirectory(target);

        await new ArchiveExtractJob(ExtractMode.ToChosen, target).RunAsync(archive, null, CancellationToken.None);

        Assert.Equal("zwei", File.ReadAllText(Path.Combine(target, "Альбом", "Ünïcödé", "über.txt")));
    }

    [Fact]
    public async Task ArchiveInNonAnsiFolder_IsExtractedTestedAndListed()
    {
        if (!ArchiveSupport.TarAvailable || CultureInfo.CurrentCulture.TextInfo.ANSICodePage != 1251) return;
        var archive = await Make7zAsync("src", Path.Combine("日本 папка", "альбом.7z"));
        // Без коротких имён 8.3 на диске обойти ограничение tar.exe нельзя — тогда ждём понятную ошибку.
        string tarPath;
        try { tarPath = ArchiveSupport.TarPath(archive); }
        catch (CompressionException ex)
        {
            Assert.Contains("tar.exe", ex.Message);
            return;
        }
        Assert.True(ArchiveSupport.IsAnsiSafe(tarPath));

        var result = await new ArchiveExtractJob(ExtractMode.Here).RunAsync(archive, null, CancellationToken.None);
        Assert.Equal(Path.Combine(_root, "日本 папка", "Альбом"), result.OutputPath);
        Assert.True(File.Exists(Path.Combine(_root, "日本 папка", "Альбом", "фото 1.txt")));

        var test = await new ArchiveTestJob().RunAsync(archive, null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, test.Status);
        Assert.Equal(2, (await ArchiveListing.ReadAsync(archive, CancellationToken.None)).Files);
    }

    [Fact]
    public async Task SevenZip_TopLevelNonAnsiName_SuggestsZip()
    {
        if (!ArchiveSupport.TarAvailable || CultureInfo.CurrentCulture.TextInfo.ANSICodePage != 1251) return;
        var dir = Path.Combine(_root, "日本");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), "a");

        var ex = await Assert.ThrowsAsync<CompressionException>(() =>
            new ArchiveCreateJob(ArchiveFormat.SevenZip).RunAsync(dir, null, CancellationToken.None));
        Assert.Contains("Сожмите в ZIP", ex.Message);

        // А в ZIP — без проблем.
        var zip = await new ArchiveCreateJob(ArchiveFormat.Zip).RunAsync(dir, null, CancellationToken.None);
        Assert.Equal(Path.Combine(_root, "日本.zip"), zip.OutputPath);
        Assert.Empty(Directory.GetFiles(_root, ".filedial-*"));
    }
}
