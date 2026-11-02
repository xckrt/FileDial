using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileDial.Core.Jobs;

namespace FileDial.Core.Compression;

/// <summary>Архив сборки ffmpeg для установки.</summary>
public sealed record FfmpegPackage(string Name, string Url, long SizeBytes, string Sha256, string Version);

/// <summary>
/// Установка ffmpeg в один клик: сборка BtbN для Windows x64 (GPL, со встроенным whisper) из GitHub.
/// Берётся последняя стабильная ветка (ffmpeg-nX.Y-latest-win64-gpl-X.Y.zip); архив проверяется по SHA-256,
/// который GitHub отдаёт для каждого файла релиза; из архива извлекаются только ffmpeg.exe и ffprobe.exe.
/// </summary>
public static partial class FfmpegInstaller
{
    public const string ReleaseApi = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/tags/latest";

    /// <summary>Куда ставится ffmpeg: %LOCALAPPDATA%\FileDial\ffmpeg (права администратора не нужны).</summary>
    public static string InstallFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileDial", "ffmpeg");

    [GeneratedRegex(@"^ffmpeg-n(?<major>\d+)\.(?<minor>\d+)-latest-win64-gpl-\d+\.\d+\.zip$")]
    private static partial Regex StableAsset();

    /// <summary>Выбирает из ответа GitHub API самую новую стабильную ветку; без неё — ночную сборку master.</summary>
    public static FfmpegPackage? SelectPackage(string releaseJson)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        FfmpegPackage? best = null, master = null;
        var bestVersion = new Version(0, 0);
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;
            var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
            // Без контрольной суммы не ставим: нечем проверить скачанное.
            if (url is null || size <= 0 || digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal))
                continue;
            var sha = digest["sha256:".Length..];

            var match = StableAsset().Match(name);
            if (match.Success)
            {
                var version = new Version(int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture));
                if (version > bestVersion)
                {
                    bestVersion = version;
                    best = new FfmpegPackage(name, url, size, sha, version.ToString());
                }
            }
            else if (name == "ffmpeg-master-latest-win64-gpl.zip")
            {
                master = new FfmpegPackage(name, url, size, sha, "master");
            }
        }
        return best ?? master;
    }

    public static async Task<FfmpegPackage> FindLatestAsync(HttpClient http, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        request.Headers.UserAgent.ParseAdd("FileDial");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return SelectPackage(json) ?? throw new InvalidDataException("Не нашёлся подходящий архив ffmpeg для Windows.");
    }

    /// <summary>Скачивает, проверяет и распаковывает ffmpeg.exe и ffprobe.exe в <paramref name="folder"/>.</summary>
    public static async Task<FfmpegTools> InstallAsync(HttpClient http, FfmpegPackage package, string folder,
        IProgress<(long Done, long Total)>? progress, IProgress<string>? stage, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, package.Name + ".part");
        var staging = Path.Combine(folder, ".staging");
        try
        {
            stage?.Report($"Скачивание ffmpeg {package.Version}");
            await VerifiedDownload.DownloadAsync(http, package.Url, package.SizeBytes, package.Sha256, zip, progress, ct)
                .ConfigureAwait(false);

            stage?.Report("Распаковка");
            OutputFiles.TryDeleteDirectory(staging);
            Directory.CreateDirectory(staging);
            await Task.Run(() => ExtractTools(zip, staging, ct), ct).ConfigureAwait(false);

            // Замена готовыми файлами — только после успешной распаковки обоих.
            foreach (var exe in new[] { "ffmpeg.exe", "ffprobe.exe" })
                File.Move(Path.Combine(staging, exe), Path.Combine(folder, exe), overwrite: true);
            return new FfmpegTools(Path.Combine(folder, "ffmpeg.exe"), Path.Combine(folder, "ffprobe.exe"));
        }
        finally
        {
            VerifiedDownload.TryDelete(zip);
            OutputFiles.TryDeleteDirectory(staging);
        }
    }

    /// <summary>Из архива берём только bin\ffmpeg.exe и bin\ffprobe.exe; остальные файлы не нужны.</summary>
    public static void ExtractTools(string zipPath, string targetFolder, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var exe in new[] { "ffmpeg.exe", "ffprobe.exe" })
        {
            ct.ThrowIfCancellationRequested();
            var entry = archive.Entries.FirstOrDefault(e =>
                            e.FullName.Replace('\\', '/').EndsWith("/bin/" + exe, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"В архиве нет {exe}.");
            // Пишем под своим именем, путь из архива не используем — никаких «../» за пределы папки.
            entry.ExtractToFile(Path.Combine(targetFolder, exe), overwrite: true);
        }
    }
}
