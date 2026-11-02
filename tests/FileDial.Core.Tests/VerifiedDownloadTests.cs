using System.Net;
using System.Security.Cryptography;
using FileDial.Core.Jobs;

namespace FileDial.Core.Tests;

public class VerifiedDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Recording(Func<HttpContent> content) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            // Как gyan.dev: без User-Agent — 503.
            var status = request.Headers.UserAgent.Count == 0 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status) { Content = content() });
        }
    }

    /// <summary>Поток, который отдаёт начало и «замолкает» навсегда — как зависшее соединение.</summary>
    private sealed class StallingStream(byte[] head) : Stream
    {
        private int _sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _sent; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_sent < head.Length)
            {
                var n = Math.Min(buffer.Length, head.Length - _sent);
                head.AsMemory(_sent, n).CopyTo(buffer);
                _sent += n;
                return n;
            }
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
    }

    [Fact]
    public async Task SendsUserAgent_SoStrictServersAnswer()
    {
        var body = new byte[1000];
        var http = new Recording(() => new ByteArrayContent(body));
        var part = Path.Combine(_root, "file.part");

        await VerifiedDownload.DownloadAsync(new HttpClient(http), "https://example/f", null,
            Convert.ToHexStringLower(SHA256.HashData(body)), part, null, CancellationToken.None);

        Assert.Contains("FileDial", http.Requests[0].Headers.UserAgent.ToString());
        Assert.True(File.Exists(part));

        var text = await VerifiedDownload.GetStringAsync(new HttpClient(new Recording(() => new StringContent("9.0.2"))),
            "https://example/v", CancellationToken.None);
        Assert.Equal("9.0.2", text);
    }

    [Fact]
    public async Task StalledDownload_FailsWithClearMessage_AndLeavesNothing()
    {
        var http = new Recording(() => new StreamContent(new StallingStream(new byte[5000])));
        var part = Path.Combine(_root, "stall.part");

        var ex = await Assert.ThrowsAsync<IOException>(() => VerifiedDownload.DownloadAsync(new HttpClient(http),
            "https://example/f", null, new string('0', 64), part, null, CancellationToken.None,
            stallTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.StartsWith("Загрузка остановилась", ex.Message);
        Assert.False(File.Exists(part));
    }

    [Fact]
    public async Task UserCancel_IsCancellation_NotStall()
    {
        using var cts = new CancellationTokenSource();
        var http = new Recording(() => new StreamContent(new StallingStream(new byte[10])));
        cts.CancelAfter(100);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => VerifiedDownload.DownloadAsync(new HttpClient(http),
            "https://example/f", null, new string('0', 64), Path.Combine(_root, "c.part"), null, cts.Token,
            stallTimeout: TimeSpan.FromSeconds(30)));
    }
}
