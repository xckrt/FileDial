using System.Net;
using System.Security.Cryptography;
using FileDial.Core.Localization;
using FileDial.Core.Updates;

namespace FileDial.Core.Tests;

/// <summary>Проверка обновлений на готовых ответах GitHub — без сети.</summary>
public class UpdateCheckerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly Version Current = new(0, 2, 0);

    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789ABCDEF";

    /// <summary>Сокращённый настоящий ответ releases/latest.</summary>
    private static string Release(string tag = "v0.3.0", string? asset = null, string? digest = Digest,
        bool prerelease = false, string url = "https://github.com/owner/FileDial/releases/download/v0.3.0/FileDial-Setup-0.3.0.exe") =>
        $$"""
        {
          "url": "https://api.github.com/repos/owner/FileDial/releases/1",
          "html_url": "https://github.com/owner/FileDial/releases/tag/{{tag}}",
          "tag_name": "{{tag}}",
          "name": "FileDial {{tag}}",
          "draft": false,
          "prerelease": {{(prerelease ? "true" : "false")}},
          "assets": [
            {
              "name": "FileDial-0.3.0-win-x64.zip",
              "size": 76000000,
              "browser_download_url": "https://github.com/owner/FileDial/releases/download/v0.3.0/FileDial-0.3.0-win-x64.zip"
            },
            {
              "name": "{{asset ?? "FileDial-Setup-0.3.0.exe"}}",
              "content_type": "application/x-msdownload",
              "size": 82944000,
              {{(digest is null ? "" : $"\"digest\": \"{digest}\",")}}
              "browser_download_url": "{{url}}"
            }
          ]
        }
        """;

    [Theory]
    [InlineData("v0.3.0", "0.3.0")]
    [InlineData("0.3.0", "0.3.0")]
    [InlineData("V1.10", "1.10.0")]
    [InlineData("v2.0.1-beta.1", "2.0.1")]
    [InlineData("v2.0.1+build5", "2.0.1")]
    public void ParseVersion_AcceptsTagForms(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateChecker.ParseVersion(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v3")]
    public void ParseVersion_RejectsGarbage(string? tag) => Assert.Null(UpdateChecker.ParseVersion(tag));

    [Fact]
    public void NewerRelease_GivesSetupAsset()
    {
        var info = UpdateChecker.Parse(Release(), Current)!;
        Assert.Equal(new Version(0, 3, 0), info.Version);
        Assert.Equal("0.3.0", info.VersionText);
        Assert.Equal("https://github.com/owner/FileDial/releases/tag/v0.3.0", info.ReleasePage);
        Assert.EndsWith("/FileDial-Setup-0.3.0.exe", info.SetupUrl);
        Assert.Equal(82944000, info.Size);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", info.Sha256);
    }

    [Fact]
    public void WithoutDigest_SizeOnly() => Assert.Null(UpdateChecker.Parse(Release(digest: null), Current)!.Sha256);

    [Theory]
    [InlineData("v0.2.0")] // та же
    [InlineData("v0.1.9")] // старее
    public void SameOrOlder_NoUpdate(string tag) => Assert.Null(UpdateChecker.Parse(Release(tag), Current));

    [Fact]
    public void NoSetupAsset_NoUpdate() => Assert.Null(UpdateChecker.Parse(Release(asset: "FileDial-Setup-0.2.9.exe"), Current));

    [Fact]
    public void Prerelease_NoUpdate() => Assert.Null(UpdateChecker.Parse(Release(prerelease: true), Current));

    [Theory]
    [InlineData("http://github.com/owner/FileDial/releases/download/v0.3.0/FileDial-Setup-0.3.0.exe")]
    [InlineData("https://evil.example/FileDial-Setup-0.3.0.exe")]
    [InlineData("https://github.com.evil.example/FileDial-Setup-0.3.0.exe")]
    public void DownloadOnlyFromGitHubHttps(string url) => Assert.Null(UpdateChecker.Parse(Release(url: url), Current));

    [Theory]
    [InlineData("sha256:abc", null)]
    [InlineData("sha512:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", null)]
    [InlineData(null, null)]
    public void Digest_OnlyFullSha256(string? digest, string? expected) => Assert.Equal(expected, UpdateChecker.Sha256FromDigest(digest));

    [Theory]
    [InlineData("owner/FileDial", true)]
    [InlineData(" owner/file-dial.app ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("owner", false)]
    [InlineData("owner/repo/extra", false)]
    [InlineData("https://github.com/owner/repo", false)]
    public void Repository_Validation(string? repo, bool valid) => Assert.Equal(valid, UpdateChecker.IsValidRepository(repo));

    [Fact]
    public void UserAgent_HasVersion() =>
        Assert.Equal("FileDial/0.2.0 (Windows; file tool)", UpdateChecker.UserAgent(new Version(0, 2, 0, 0)));

    private sealed class Canned(HttpStatusCode status, Func<HttpContent> content) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content() });
        }
    }

    [Fact]
    public async Task CheckAsync_CallsLatestRelease_WithUserAgent()
    {
        var handler = new Canned(HttpStatusCode.OK, () => new StringContent(Release()));
        using var http = new HttpClient(handler);
        var info = await UpdateChecker.CheckAsync(http, "owner/FileDial", Current, CancellationToken.None);

        Assert.NotNull(info);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.github.com/repos/owner/FileDial/releases/latest", request.RequestUri!.ToString());
        Assert.Equal("FileDial/0.2.0 (Windows; file tool)", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task CheckAsync_NoReleases_IsNotAnError()
    {
        using var http = new HttpClient(new Canned(HttpStatusCode.NotFound, () => new StringContent("{}")));
        Assert.Null(await UpdateChecker.CheckAsync(http, "owner/FileDial", Current, CancellationToken.None));
    }

    [Fact]
    public async Task CheckAsync_RateLimit_Throws()
    {
        using var http = new HttpClient(new Canned(HttpStatusCode.Forbidden, () => new StringContent("{}")));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            UpdateChecker.CheckAsync(http, "owner/FileDial", Current, CancellationToken.None));
    }

    [Fact]
    public async Task Download_VerifiesDigest_IntoOwnFolder()
    {
        var payload = new byte[4096];
        Random.Shared.NextBytes(payload);
        var sha = Convert.ToHexStringLower(SHA256.HashData(payload));
        var info = new UpdateInfo(new Version(0, 3, 0), "https://github.com/x", "https://github.com/x/setup.exe", payload.Length, sha);
        using var http = new HttpClient(new Canned(HttpStatusCode.OK, () => new ByteArrayContent(payload)));

        var path = await UpdateChecker.DownloadAsync(http, info, _root, null, CancellationToken.None);

        Assert.Equal("FileDial-Setup-0.3.0.exe", Path.GetFileName(path));
        Assert.StartsWith(UpdateChecker.DownloadFolderPrefix, Path.GetFileName(Path.GetDirectoryName(path)));
        Assert.Equal(payload, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Download_WrongDigest_LeavesNothing()
    {
        var payload = new byte[1024];
        var info = new UpdateInfo(new Version(0, 3, 0), "https://github.com/x", "https://github.com/x/setup.exe",
            payload.Length, new string('0', 64));
        using var http = new HttpClient(new Canned(HttpStatusCode.OK, () => new ByteArrayContent(payload)));

        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateChecker.DownloadAsync(http, info, _root, null, CancellationToken.None));
        Assert.Empty(Directory.Exists(_root) ? Directory.GetFileSystemEntries(_root) : []);
    }

    [Fact]
    public async Task Download_WithoutDigest_ChecksSize()
    {
        var payload = new byte[1024];
        var info = new UpdateInfo(new Version(0, 3, 0), "https://github.com/x", "https://github.com/x/setup.exe", 2048, null);
        using var http = new HttpClient(new Canned(HttpStatusCode.OK, () => new ByteArrayContent(payload)));
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateChecker.DownloadAsync(http, info, _root, null, CancellationToken.None));

        var ok = info with { Size = payload.Length };
        Assert.True(File.Exists(await UpdateChecker.DownloadAsync(http, ok, _root, null, CancellationToken.None)));
    }

    [Fact]
    public void CleanOldDownloads_KeepsFreshOnes()
    {
        var old = Directory.CreateDirectory(Path.Combine(_root, UpdateChecker.DownloadFolderPrefix + "old")).FullName;
        var fresh = Directory.CreateDirectory(Path.Combine(_root, UpdateChecker.DownloadFolderPrefix + "fresh")).FullName;
        var other = Directory.CreateDirectory(Path.Combine(_root, "чужая")).FullName;
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-3));

        UpdateChecker.CleanOldDownloads(_root, DateTime.UtcNow);

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(fresh));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void Schedule_WeeklyAndSurvivesClockChanges()
    {
        var now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(UpdateSchedule.IsDue(null, now));
        Assert.False(UpdateSchedule.IsDue(now.AddDays(-6), now));
        Assert.True(UpdateSchedule.IsDue(now.AddDays(-7), now));
        Assert.True(UpdateSchedule.IsDue(now.AddDays(1), now)); // часы перевели назад

        var file = Path.Combine(_root, "update-check.txt");
        Assert.Null(UpdateSchedule.LoadLast(file));
        UpdateSchedule.SaveLast(file, now);
        Assert.Equal(now, UpdateSchedule.LoadLast(file));
        File.WriteAllText(file, "мусор");
        Assert.Null(UpdateSchedule.LoadLast(file));
    }

    [Fact]
    public void Settings_CheckForUpdates_DefaultsOn_ForOldFiles()
    {
        var file = Path.Combine(_root, "settings.json");
        Directory.CreateDirectory(_root);
        File.WriteAllText(file, """{ "Animations": false }""");
        Assert.True(Settings.SettingsStore.Load(file).CheckForUpdates);

        Settings.SettingsStore.Save(file, new Settings.AppSettings { CheckForUpdates = false });
        Assert.False(Settings.SettingsStore.Load(file).CheckForUpdates);
    }
}

[Collection(LanguageCollection.Name)]
public class UpdateTextsTests
{
    [Fact]
    public void Texts_BothLanguages()
    {
        Assert.Equal("Вышла FileDial 0.3.0", Lang.T.UpdAvailableTitle("0.3.0"));
        using (new EnglishScope())
            Assert.Equal("FileDial 0.3.0 is available", Lang.T.UpdAvailableTitle("0.3.0"));
    }
}

/// <summary>Папка программы при сжатом exe и уборка старых распаковок.</summary>
public class AppPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(@"C:\Программы\FileDial\FileDial.exe", @"C:\Temp\.net\FileDial\abc\", @"C:\Программы\FileDial")]
    [InlineData(@"C:\dl\FileDial-Setup-0.3.0.exe", @"C:\Temp\.net\x\", @"C:\dl")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", @"C:\tests\bin\", @"C:\tests\bin\")]
    [InlineData(@"C:\tests\bin\testhost.exe", @"C:\tests\bin\", @"C:\tests\bin\")]
    [InlineData(null, @"C:\tests\bin\", @"C:\tests\bin\")]
    public void ExeFolder(string? processPath, string baseDirectory, string expected) =>
        Assert.Equal(expected, AppPaths.ResolveExeFolder(processPath, baseDirectory));

    private string Make(params string[] parts)
    {
        var dir = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.dll"), "x");
        return dir;
    }

    [Fact]
    public void CleanOldExtractions_RemovesOldVersionsAndStaleSetups()
    {
        var current = Make("FileDial", "new");
        var previous = Make("FileDial", "old");
        var staleSetup = Make("FileDial-Setup-0.2.0", "s1");
        var freshSetup = Make("FileDial-Setup-0.3.0", "s2");
        var foreign = Make("Other", "x");
        var renamed = Make("FileDial (2)", "y");
        var now = DateTime.UtcNow;
        Directory.SetLastWriteTimeUtc(staleSetup, now.AddHours(-2));

        Assert.Equal(2, AppPaths.CleanOldExtractions(current + Path.DirectorySeparatorChar, now));

        Assert.True(Directory.Exists(current));
        Assert.False(Directory.Exists(previous));
        Assert.False(Directory.Exists(staleSetup));
        Assert.False(Directory.Exists(Path.GetDirectoryName(staleSetup))); // пустая папка установщика тоже
        Assert.True(Directory.Exists(freshSetup));
        Assert.True(Directory.Exists(foreign));
        Assert.True(Directory.Exists(renamed));
    }

    [Fact]
    public void CleanOldExtractions_LeavesFolderOfRunningVersionWhole()
    {
        // Другая версия с тем же именем exe ещё работает: одна её DLL загружена, другая — ещё нет.
        var current = Make("FileDial", "new");
        var running = Make("FileDial", "running");
        var notLoadedYet = Path.Combine(running, "PdfSharp.dll");
        File.WriteAllText(notLoadedYet, "x");

        using (File.Open(Path.Combine(running, "a.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(0, AppPaths.CleanOldExtractions(current, DateTime.UtcNow));

        Assert.True(File.Exists(notLoadedYet)); // ничего не удалено частично
        Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(current)!), d => d == running);
    }
}
