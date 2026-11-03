using System.Security.Cryptography;
using FileDial.Core.Localization;

namespace FileDial.Core.Jobs;

/// <summary>
/// Загрузка файла с проверкой размера и SHA-256. Пишет во временный файл; при любой ошибке,
/// отмене или несовпадении суммы временный файл удаляется — «наполовину скачанного» не остаётся.
/// </summary>
public static class VerifiedDownload
{
    /// <summary>Некоторые серверы (gyan.dev) отвечают 503 или молчат на запросы без User-Agent.</summary>
    public const string UserAgent = "FileDial/1.0 (Windows; file tool)";

    /// <summary>Сколько ждать очередную порцию данных, прежде чем считать загрузку зависшей.</summary>
    public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

    public static HttpRequestMessage Get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    /// <summary>Небольшой текстовый ответ (номер версии, контрольная сумма) — с User-Agent.</summary>
    public static async Task<string> GetStringAsync(HttpClient http, string url, CancellationToken ct)
    {
        using var request = Get(url);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <param name="expectedSize">Размер, если известен заранее; иначе берётся из ответа сервера (только для прогресса).</param>
    /// <param name="stallTimeout">Сколько ждать данных без единого байта; по умолчанию 60 с.</param>
    public static async Task DownloadAsync(HttpClient http, string url, long? expectedSize, string expectedSha256,
        string partPath, IProgress<(long Done, long Total)>? progress, CancellationToken ct, TimeSpan? stallTimeout = null)
    {
        var stall = stallTimeout ?? DefaultStallTimeout;
        var folder = Path.GetDirectoryName(partPath)!;
        Directory.CreateDirectory(folder);

        var ok = false;
        try
        {
            using var request = Get(url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = expectedSize ?? response.Content.Headers.ContentLength ?? 0;
            // Архиву нужно место и на себя, и на распакованное.
            if (total > 0 && FreeBytes(folder) is { } free && free < total * 3 + 50_000_000)
                throw new IOException(Lang.T.JobDownloadNoSpace(total * 3 / 1_000_000 + 50));

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await ReadWithStallTimeoutAsync(input, buffer, stall, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    progress?.Report((done, total));
                }
            }

            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if ((expectedSize is { } size && new FileInfo(partPath).Length != size) ||
                !actual.Equals(expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Lang.T.JobDownloadCorrupted);
            ok = true;
        }
        finally
        {
            if (!ok)
                TryDelete(partPath);
        }
    }

    /// <summary>Чтение очередной порции: если данных нет дольше <paramref name="stall"/> — понятная ошибка, а не вечное ожидание.</summary>
    private static async Task<int> ReadWithStallTimeoutAsync(Stream input, byte[] buffer, TimeSpan stall, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(stall);
        try
        {
            return await input.ReadAsync(buffer, timer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException(Lang.T.JobDownloadStalled);
        }
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static long? FreeBytes(string folder)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(folder));
            return string.IsNullOrEmpty(root) || root.StartsWith(@"\\") ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
