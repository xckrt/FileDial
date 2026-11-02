using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using FileDial.Core.Compression;

namespace FileDial.Core.Tests;

public class FfmpegInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "Установка ffmpeg");

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(_root)!;
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }

    private static string Asset(string name, long size, string? digest) => $$"""
        { "name": "{{name}}", "size": {{size}},
          "browser_download_url": "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/{{name}}"
          {{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }
        """;

    private static string Release(params string[] assets) => $$"""{ "name": "Latest", "assets": [ {{string.Join(",", assets)}} ] }""";

    private const string Sha = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void SelectPackage_PrefersNewestStableBranch()
    {
        var json = Release(
            Asset("ffmpeg-master-latest-win64-gpl.zip", 300, Sha),
            Asset("ffmpeg-n8.1-latest-win64-gpl-8.1.zip", 200, Sha),
            Asset("ffmpeg-n9.0-latest-win64-gpl-9.0.zip", 195, Sha),
            Asset("ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip", 100, Sha),
            Asset("ffmpeg-n10.0-latest-linux64-gpl-10.0.tar.xz", 100, Sha),
            Asset("checksums.sha256", 5, Sha));

        var p = FfmpegInstaller.SelectPackage(json)!;
        Assert.Equal("ffmpeg-n9.0-latest-win64-gpl-9.0.zip", p.Name);
        Assert.Equal("9.0", p.Version);
        Assert.Equal(195, p.SizeBytes);
        Assert.Equal(new string('a', 64), p.Sha256);
    }

    [Fact]
    public void SelectPackage_VersionsCompareNumerically()
    {
        var json = Release(Asset("ffmpeg-n9.0-latest-win64-gpl-9.0.zip", 1, Sha), Asset("ffmpeg-n10.1-latest-win64-gpl-10.1.zip", 2, Sha));
        Assert.Equal("10.1", FfmpegInstaller.SelectPackage(json)!.Version);
    }

    [Fact]
    public void SelectPackage_FallsBackToMaster_AndSkipsAssetsWithoutChecksum()
    {
        var json = Release(
            Asset("ffmpeg-n9.0-latest-win64-gpl-9.0.zip", 195, null), // без суммы — нечем проверить
            Asset("ffmpeg-master-latest-win64-gpl.zip", 300, Sha));
        Assert.Equal("master", FfmpegInstaller.SelectPackage(json)!.Version);
        Assert.Null(FfmpegInstaller.SelectPackage(Release(Asset("ffmpeg-n9.0-latest-win64-gpl-9.0.zip", 1, null))));
        Assert.Null(FfmpegInstaller.SelectPackage("{}"));
    }

    /// <summary>Архив как у BtbN: папка сборки, bin с двумя exe, плюс лишнее — и попытка выйти за пределы папки.</summary>
    private byte[] MakeZip(bool withProbe = true)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open());
                w.Write(content);
            }
            Add("ffmpeg-n9.0-latest-win64-gpl-9.0/bin/ffmpeg.exe", "FFMPEG");
            if (withProbe) Add("ffmpeg-n9.0-latest-win64-gpl-9.0/bin/ffprobe.exe", "FFPROBE");
            Add("ffmpeg-n9.0-latest-win64-gpl-9.0/bin/ffplay.exe", "FFPLAY");
            Add("ffmpeg-n9.0-latest-win64-gpl-9.0/doc/readme.txt", "doc");
            Add("../../evil/bin/ffmpeg.exe.txt", "evil");
        }
        return ms.ToArray();
    }

    [Fact]
    public void ExtractTools_TakesOnlyTwoExecutables()
    {
        Directory.CreateDirectory(_root);
        var zip = Path.Combine(_root, "a.zip");
        File.WriteAllBytes(zip, MakeZip());
        var target = Path.Combine(_root, "out");
        Directory.CreateDirectory(target);

        FfmpegInstaller.ExtractTools(zip, target, CancellationToken.None);

        Assert.Equal(["ffmpeg.exe", "ffprobe.exe"], Directory.GetFiles(target).Select(Path.GetFileName).Order());
        Assert.Equal("FFPROBE", File.ReadAllText(Path.Combine(target, "ffprobe.exe")));
        Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "evil")));
    }

    [Fact]
    public void ExtractTools_MissingProbe_IsAnError()
    {
        Directory.CreateDirectory(_root);
        var zip = Path.Combine(_root, "b.zip");
        File.WriteAllBytes(zip, MakeZip(withProbe: false));
        var ex = Assert.Throws<InvalidDataException>(() => FfmpegInstaller.ExtractTools(zip, _root, CancellationToken.None));
        Assert.Equal("В архиве нет ffprobe.exe.", ex.Message);
    }

    private sealed class FakeHttp(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public async Task Install_DownloadsVerifiesAndPlacesTools_LeavingNoTemp()
    {
        var zip = MakeZip();
        var package = new FfmpegPackage("ffmpeg-n9.0-latest-win64-gpl-9.0.zip", "https://example/zip", zip.Length,
            Convert.ToHexStringLower(SHA256.HashData(zip)), "9.0");
        var stages = new List<string>();

        var tools = await FfmpegInstaller.InstallAsync(new HttpClient(new FakeHttp(zip)), package, _root, null,
            new Sync<string>(stages.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "ffmpeg.exe"), tools.Ffmpeg);
        Assert.Equal("FFMPEG", File.ReadAllText(tools.Ffmpeg));
        Assert.Equal(["ffmpeg.exe", "ffprobe.exe"], Directory.GetFileSystemEntries(_root).Select(Path.GetFileName).Order());
        Assert.Equal(["Скачивание ffmpeg 9.0", "Распаковка"], stages);
        Assert.Equal(tools, FfmpegLocator.Locate(_root, null));
    }

    [Fact]
    public async Task Install_WithWrongChecksum_KeepsOldToolsUntouched()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "ffmpeg.exe"), "OLD");
        var zip = MakeZip();
        var package = new FfmpegPackage("x.zip", "https://example/zip", zip.Length, new string('0', 64), "9.0");

        await Assert.ThrowsAsync<InvalidDataException>(() => FfmpegInstaller.InstallAsync(
            new HttpClient(new FakeHttp(zip)), package, _root, null, null, CancellationToken.None));

        Assert.Equal("OLD", File.ReadAllText(Path.Combine(_root, "ffmpeg.exe")));
        Assert.Equal(["ffmpeg.exe"], Directory.GetFileSystemEntries(_root).Select(Path.GetFileName));
    }

    private sealed class Sync<T>(Action<T> a) : IProgress<T>
    {
        public void Report(T value) => a(value);
    }
}
