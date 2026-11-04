using System.Globalization;
using System.IO.Compression;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

public class ArchivePasswordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "С паролем");

    public ArchivePasswordTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    private string PlainZip()
    {
        var path = Path.Combine(_root, "документы.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string name, string text)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(text);
        }
        Add("договор.txt", "секретные условия");
        Add("Сканы/паспорт.txt", "1234 567890");
        return path;
    }

    private async Task<string> ProtectedZipAsync(string password)
    {
        var result = await new ArchiveProtectJob(password).RunAsync(PlainZip(), null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, result.Status);
        return result.OutputPath!;
    }

    [Fact]
    public async Task Protect_CreatesEncryptedCopy_SourceUntouched()
    {
        var source = PlainZip();
        var before = File.ReadAllBytes(source);

        var result = await new ArchiveProtectJob("Надёжный-1").RunAsync(source, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "документы_пароль.zip"), result.OutputPath);
        Assert.True(ArchiveSupport.IsEncryptedZip(result.OutputPath!));
        Assert.False(ArchiveSupport.IsEncryptedZip(source));
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Contains("AES-256", result.Message);
        Assert.Empty(Directory.GetFiles(_root, ".filedial-*"));
    }

    [CyrillicWindowsFact]
    public async Task Extract_AsksForPassword_RejectsWrong_AcceptsRight()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var zip = await ProtectedZipAsync("Pa55word");
        var target = Path.Combine(_root, "out");
        Directory.CreateDirectory(target);
        IFileJob job = new ArchiveExtractJob(ExtractMode.ToChosen, target);

        var none = await Assert.ThrowsAsync<PasswordRequiredException>(() => job.RunAsync(zip, null, CancellationToken.None));
        Assert.False(none.WrongPassword);

        var wrong = await Assert.ThrowsAsync<PasswordRequiredException>(() =>
            ((IPasswordAware)job).WithPassword("не тот").RunAsync(zip, null, CancellationToken.None));
        Assert.True(wrong.WrongPassword);
        Assert.Empty(Directory.GetFileSystemEntries(target)); // после неудачи ничего не осталось

        var ok = await ((IPasswordAware)job).WithPassword("Pa55word").RunAsync(zip, null, CancellationToken.None);
        Assert.Equal(JobStatus.Done, ok.Status);
        Assert.Equal("1234 567890", File.ReadAllText(Path.Combine(ok.OutputPath!, "Сканы", "паспорт.txt")));
    }

    [CyrillicWindowsFact]
    public async Task Test_And_Repack_WorkWithPassword()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var zip = await ProtectedZipAsync("Pa55word");

        await Assert.ThrowsAsync<PasswordRequiredException>(() => new ArchiveTestJob().RunAsync(zip, null, CancellationToken.None));
        var test = await new ArchiveTestJob { Password = "Pa55word" }.RunAsync(zip, null, CancellationToken.None);
        Assert.Equal("Архив в порядке: 2 файла, ошибок нет.", test.Message);

        // Перепаковка в 7z снимает пароль (это смена формата, а не защита).
        var repacked = await new ArchiveRepackJob(ArchiveFormat.SevenZip) { Password = "Pa55word" }
            .RunAsync(zip, null, CancellationToken.None);
        Assert.Equal(".7z", Path.GetExtension(repacked.OutputPath));
        Assert.Equal(2, ArchiveSupport.CountFiles(repacked.OutputPath!, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Contents_OfEncryptedZip_ShowNamesWithoutPassword()
    {
        var zip = await ProtectedZipAsync("Pa55word");
        var contents = await ArchiveListing.ReadAsync(zip, CancellationToken.None);
        Assert.Contains(contents.Entries, e => e.Path == "Сканы/паспорт.txt");
    }

    [Fact]
    public void Catalog_And_Inputs()
    {
        Assert.True(ActionCatalog.ForFile(@"C:\a.zip", 1).Single(a => a.Id == ActionIds.ArchivePassword).IsEnabled
                    || !ArchiveSupport.TarAvailable);
        Assert.True(FileJobs.NeedsNewPassword(ActionIds.ArchivePassword));
        Assert.False(FileJobs.NeedsFfmpeg(ActionIds.ArchivePassword));
        Assert.True(FileJobs.IsImplemented(ActionIds.ArchivePassword));
    }

    [Fact]
    public void Passphrase_OutsideAnsi_IsExplained()
    {
        if (CultureInfo.CurrentCulture.TextInfo.ANSICodePage != 1251) return;
        Assert.Equal(["--passphrase", "пароль№1"], ArchiveSupport.PassphraseArgs("пароль№1"));
        Assert.Empty(ArchiveSupport.PassphraseArgs(null));
        Assert.Throws<CompressionException>(() => ArchiveSupport.PassphraseArgs("パス"));
    }
}
