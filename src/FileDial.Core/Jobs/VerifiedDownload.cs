using System.Security.Cryptography;

namespace FileDial.Core.Jobs;

/// <summary>
/// Загрузка файла с проверкой размера и SHA-256. Пишет во временный файл; при любой ошибке,
/// отмене или несовпадении суммы временный файл удаляется — «наполовину скачанного» не остаётся.
/// </summary>
public static class VerifiedDownload
{
    /// <param name="expectedSize">Размер, если известен заранее; иначе берётся из ответа сервера (только для прогресса).</param>
    public static async Task DownloadAsync(HttpClient http, string url, long? expectedSize, string expectedSha256,
        string partPath, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        var folder = Path.GetDirectoryName(partPath)!;
        Directory.CreateDirectory(folder);

        var ok = false;
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = expectedSize ?? response.Content.Headers.ContentLength ?? 0;
            // Архиву нужно место и на себя, и на распакованное.
            if (total > 0 && FreeBytes(folder) is { } free && free < total * 3 + 50_000_000)
                throw new IOException($"Мало места на диске: нужно около {total * 3 / 1_000_000 + 50} МБ.");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
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
                throw new InvalidDataException("Скачанный файл повреждён (не совпала контрольная сумма). Попробуйте ещё раз.");
            ok = true;
        }
        finally
        {
            if (!ok)
                TryDelete(partPath);
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
