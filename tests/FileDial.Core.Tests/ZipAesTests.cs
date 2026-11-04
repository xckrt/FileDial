using System.Diagnostics;
using System.IO.Compression;
using FileDial.Core.Archives;

namespace FileDial.Core.Tests;

/// <summary>
/// ZIP с AES-256 проверяется независимым читателем — libarchive (tar.exe из Windows), который сверяет
/// проверку пароля и код подлинности HMAC. Если формат собран неправильно, распаковка не пройдёт.
/// </summary>
public class ZipAesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "Пароль");

    public ZipAesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    private static (int Exit, string Error) Tar(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo(ArchiveSupport.Tar)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, WorkingDirectory = workingDirectory,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, err);
    }

    private string MakeZip(string password, out Dictionary<string, byte[]> files)
    {
        files = new()
        {
            ["пример.txt"] = "Секретный текст, который хорошо сжимается. "u8.ToArray().Concat(new byte[5000]).ToArray(),
            ["Папка/данные.bin"] = Enumerable.Range(0, 200_000).Select(i => (byte)(i * 31 % 251)).ToArray(),
            ["пусто.txt"] = [],
        };
        var src = Path.Combine(_root, "src");
        foreach (var (name, data) in files)
        {
            var full = Path.Combine(src, name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, data);
        }

        var zip = Path.Combine(_root, "секрет.zip");
        using (var stream = File.Create(zip))
        using (var writer = new ZipAesWriter(stream, password))
        {
            foreach (var name in files.Keys)
                writer.AddFile(Path.Combine(src, name), name, CancellationToken.None);
            writer.AddDirectory("Пустая папка", DateTime.Now);
            writer.Finish();
        }
        return zip;
    }

    [CyrillicWindowsFact]
    public void RightPassword_ExtractsIdenticalFiles()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var zip = MakeZip("Pa55-word", out var files);
        var outDir = Path.Combine(_root, "out");
        Directory.CreateDirectory(outDir);

        var (exit, error) = Tar(outDir, "-x", "--passphrase", "Pa55-word", "-f", zip);

        Assert.True(exit == 0, error);
        foreach (var (name, data) in files)
            Assert.Equal(data, File.ReadAllBytes(Path.Combine(outDir, name)));
        Assert.True(Directory.Exists(Path.Combine(outDir, "Пустая папка")));
    }

    [Fact]
    public void WrongPassword_IsRejected()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var zip = MakeZip("правильный", out _);
        var outDir = Path.Combine(_root, "wrong");
        Directory.CreateDirectory(outDir);

        var (exit, _) = Tar(outDir, "-x", "--passphrase", "wrong-password", "-f", zip);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void TamperedData_FailsAuthentication()
    {
        if (!ArchiveSupport.TarAvailable) return;
        var zip = MakeZip("Pa55-word", out _);
        var bytes = File.ReadAllBytes(zip);
        // Портим байт в середине данных второго файла (большого) — HMAC должен это поймать.
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(zip, bytes);
        var outDir = Path.Combine(_root, "tampered");
        Directory.CreateDirectory(outDir);

        var (exit, _) = Tar(outDir, "-x", "--passphrase", "Pa55-word", "-f", zip);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public void Structure_IsReadableByDotNet_NamesVisible_DataEncrypted()
    {
        var zip = MakeZip("x", out var files);
        using var archive = ZipFile.OpenRead(zip);
        // Имена в ZIP не шифруются — их видно; а вот прочитать данные без пароля нельзя.
        Assert.Equal(files.Keys.Order().Append("Пустая папка/").Order(), archive.Entries.Select(e => e.FullName).Order());
        Assert.ThrowsAny<Exception>(() =>
        {
            using var s = archive.GetEntry("пример.txt")!.Open();
            s.ReadByte();
        });
        // Сам текст в файле не лежит открыто.
        Assert.DoesNotContain("Секретный", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(zip)));
    }

    [Theory]
    [InlineData(2026, 11, 3, 13, 24, 58)]
    [InlineData(1975, 1, 1, 0, 0, 0)]
    public void DosTime_Encoding(int y, int mo, int d, int h, int mi, int s)
    {
        var t = ZipAesWriter.DosTime(new DateTime(y, mo, d, h, mi, s));
        var date = t >> 16;
        Assert.Equal(Math.Max(y, 1980), (int)(date >> 9) + 1980);
        if (y >= 1980)
            Assert.Equal(s / 2, (int)(t & 0x1F));
    }
}
