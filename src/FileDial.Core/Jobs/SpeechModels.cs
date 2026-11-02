using System.Security.Cryptography;

namespace FileDial.Core.Jobs;

/// <summary>Модель whisper.cpp, которую программа умеет скачать и проверить сама.</summary>
public sealed record SpeechModelInfo(string Id, string Label, string FileName, long SizeBytes, string Sha256)
{
    public string Url => $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}";
}

public static class SpeechModels
{
    // Размеры и SHA-256 — из официального репозитория whisper.cpp (Hugging Face, заголовки X-Linked-Size/X-Linked-ETag).
    public static readonly SpeechModelInfo Base = new("base", "Базовая", "ggml-base.bin", 147_951_465,
        "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe");

    public static readonly SpeechModelInfo Small = new("small", "Точная", "ggml-small.bin", 487_601_967,
        "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b");

    public static readonly IReadOnlyList<SpeechModelInfo> Downloadable = [Base, Small];

    /// <summary>Куда программа скачивает модели: %LOCALAPPDATA%\FileDial\models.</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileDial", "models");

    /// <summary>Папка models рядом с программой — для дистрибутива с моделью «в комплекте».</summary>
    public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "models");

    /// <summary>
    /// Модель для работы: указанная в настройках (даже если файла уже нет — тогда это сообщит проверка),
    /// иначе найденная в папке рядом с программой или в папке загрузок (точная предпочтительнее базовой).
    /// null — модели нет.
    /// </summary>
    public static string? Find(string? configured, params string[] folders)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        foreach (var folder in folders)
        {
            try
            {
                if (!Directory.Exists(folder))
                    continue;
                var candidates = Directory.GetFiles(folder, "ggml-*.bin");
                var best = candidates
                    .OrderByDescending(p => Path.GetFileName(p).Equals(Small.FileName, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(p => Path.GetFileName(p).Equals(Base.FileName, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (best is not null)
                    return best;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // папка недоступна — смотрим следующую
            }
        }
        return null;
    }

    public static string? FindDefault(string? configured) => Find(configured, BundledFolder, DataFolder);
}

/// <summary>Скачивание модели: во временный файл, с проверкой размера и SHA-256, затем перенос на место.</summary>
public static class SpeechModelDownloader
{
    public static async Task<string> DownloadAsync(HttpClient http, SpeechModelInfo model, string folder,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, model.FileName);
        if (File.Exists(target) && new FileInfo(target).Length == model.SizeBytes)
            return target; // уже скачана

        var free = TryFreeBytes(folder);
        if (free is { } f && f < model.SizeBytes + 50_000_000)
            throw new IOException($"Мало места на диске: нужно около {model.SizeBytes / 1_000_000 + 50} МБ.");

        var part = target + ".part";
        try
        {
            using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    progress?.Report((done, model.SizeBytes));
                }
            }

            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (new FileInfo(part).Length != model.SizeBytes || actual != model.Sha256)
                throw new InvalidDataException("Скачанный файл повреждён (не совпала контрольная сумма). Попробуйте ещё раз.");

            File.Move(part, target, overwrite: true);
            return target;
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static long? TryFreeBytes(string folder)
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
