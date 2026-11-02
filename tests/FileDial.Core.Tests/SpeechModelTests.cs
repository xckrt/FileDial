using System.Net;
using System.Security.Cryptography;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

public class SpeechModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Folder(string name, params string[] files)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "x");
        return dir;
    }

    [Fact]
    public void Find_PrefersConfigured_EvenIfMissing()
    {
        var bundled = Folder("рядом с программой", "ggml-base.bin");
        Assert.Equal(@"D:\нет.bin", SpeechModels.Find(@"D:\нет.bin", bundled));
    }

    [Fact]
    public void Find_PrefersSmallOverBase_AndBundledOverDownloaded()
    {
        var bundled = Folder("bundled", "ggml-base.bin", "ggml-small.bin");
        var data = Folder("data", "ggml-small.bin");
        Assert.Equal(Path.Combine(bundled, "ggml-small.bin"), SpeechModels.Find(null, bundled, data));
    }

    [Fact]
    public void Find_TakesAnyGgmlModel_OrNothing()
    {
        var other = Folder("other", "ggml-medium.bin", "readme.txt");
        Assert.Equal(Path.Combine(other, "ggml-medium.bin"), SpeechModels.Find(null, Folder("empty"), other));
        Assert.Null(SpeechModels.Find(null, Folder("empty2"), Path.Combine(_root, "нет такой папки")));
    }

    [Fact]
    public void KnownModels_HaveUrlsAndHashes()
    {
        Assert.Equal("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin", SpeechModels.Base.Url);
        Assert.All(SpeechModels.Downloadable, m => Assert.Equal(64, m.Sha256.Length));
    }

    [Fact]
    public void Catalog_WithoutModel_OffersSetup_WithModelButNoFilter_DoesNot()
    {
        var noModel = ActionCatalog.ForFile(@"C:\v.mp4", 1, null, JobSettings.Default).Single(a => a.Id == ActionIds.Subtitles);
        Assert.False(noModel.IsEnabled);
        Assert.Equal(Speech.NoModel, noModel.DisabledReason);
        Assert.Equal(ActionIds.SetupSpeech, noModel.FixActionId);

        var model = Path.Combine(Folder("m", "ggml-base.bin"), "ggml-base.bin");
        var noFilter = ActionCatalog.ForFile(@"C:\a.mp3", 1, null, new JobSettings { WhisperModel = model })
            .Single(a => a.Id == ActionIds.SpeechToText);
        Assert.Equal("Ваш ffmpeg собран без whisper", noFilter.DisabledReason);
        Assert.Null(noFilter.FixActionId); // это моделью не исправить
    }

    // --- Загрузка (через подставной HTTP, без сети) ---

    private sealed class FakeHttp(byte[] body, Action? onRequest = null) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            onRequest?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    private static (SpeechModelInfo Model, byte[] Body) FakeModel(int size = 300_000)
    {
        var body = new byte[size];
        new Random(42).NextBytes(body);
        var sha = Convert.ToHexStringLower(SHA256.HashData(body));
        return (new SpeechModelInfo("test", "Тест", "ggml-test.bin", body.Length, sha), body);
    }

    [Fact]
    public async Task Download_Verifies_AndPutsFileInPlace()
    {
        var (model, body) = FakeModel();
        var folder = Path.Combine(_root, "модели");
        var reports = new List<(long, long)>();

        var path = await SpeechModelDownloader.DownloadAsync(new HttpClient(new FakeHttp(body)), model, folder,
            new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(folder, "ggml-test.bin"), path);
        Assert.Equal(body, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(folder, "*.part"));
        Assert.Equal((body.Length, body.Length), reports[^1]);
    }

    [Fact]
    public async Task Download_WithWrongHash_IsRejected_AndCleanedUp()
    {
        var (model, body) = FakeModel();
        var broken = (byte[])body.Clone();
        broken[100] ^= 0xFF; // «битый» ответ сервера
        var folder = Path.Combine(_root, "битая");

        await Assert.ThrowsAsync<InvalidDataException>(() => SpeechModelDownloader.DownloadAsync(
            new HttpClient(new FakeHttp(broken)), model, folder, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task Download_Cancelled_LeavesNothing()
    {
        var (model, body) = FakeModel();
        var folder = Path.Combine(_root, "отмена");
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SpeechModelDownloader.DownloadAsync(
            new HttpClient(new FakeHttp(body, cts.Cancel)), model, folder, null, cts.Token));
        Assert.Empty(Directory.Exists(folder) ? Directory.GetFiles(folder) : []);
    }

    [Fact]
    public async Task Download_SkipsWhenAlreadyPresent()
    {
        var (model, body) = FakeModel();
        var folder = Path.Combine(_root, "уже есть");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, model.FileName), body);
        var http = new FakeHttp(body);

        await SpeechModelDownloader.DownloadAsync(new HttpClient(http), model, folder, null, CancellationToken.None);
        Assert.Equal(0, http.Requests);
    }

    private sealed class SyncProgress(Action<(long, long)> a) : IProgress<(long, long)>
    {
        public void Report((long, long) value) => a(value);
    }
}
