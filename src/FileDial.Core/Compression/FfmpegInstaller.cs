using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileDial.Core.Jobs;

namespace FileDial.Core.Compression;

public enum ArchiveKind
{
    Zip,
    SevenZip,
}

/// <summary>Архив сборки ffmpeg для установки.</summary>
/// <param name="SizeBytes">Размер, если известен заранее (у gyan.dev — нет, берётся из ответа сервера).</param>
/// <param name="HasWhisper">Есть ли в сборке фильтр whisper (распознавание речи).</param>
public sealed record FfmpegPackage(string Name, string Url, long? SizeBytes, string Sha256, string Version,
    ArchiveKind Kind = ArchiveKind.Zip, bool HasWhisper = false);

/// <summary>
/// Установка ffmpeg в один клик.
/// Основной источник — сборка gyan.dev «full» (в ней есть whisper для субтитров): архив .7z распаковывается
/// встроенным в Windows tar.exe (libarchive), контрольная сумма публикуется рядом с архивом.
/// Запасной — сборка BtbN из релизов GitHub (.zip, SHA-256 из GitHub API, но без whisper).
/// Из архива берутся только ffmpeg.exe и ffprobe.exe.
/// </summary>
public static partial class FfmpegInstaller
{
    public const string GyanBase = "https://www.gyan.dev/ffmpeg/builds/";
    public const string ReleaseApi = "https://api.github.com/repos/BtbN/FFmpeg-Builds/releases/tags/latest";

    /// <summary>Куда ставится ffmpeg: %LOCALAPPDATA%\FileDial\ffmpeg (права администратора не нужны).</summary>
    public static string InstallFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileDial", "ffmpeg");

    /// <summary>tar.exe из Windows (есть в Windows 10 1803+); умеет 7z через libarchive.</summary>
    public static string SystemTar => Path.Combine(Environment.SystemDirectory, "tar.exe");

    // --- gyan.dev ---

    [GeneratedRegex(@"^\d+(\.\d+){1,3}$")]
    private static partial Regex VersionText();

    [GeneratedRegex(@"^[0-9a-fA-F]{64}")]
    private static partial Regex Sha256Text();

    /// <summary>Пакет gyan.dev по ответам «release-version» и файлу .sha256.</summary>
    public static FfmpegPackage? GyanPackage(string versionText, string shaText)
    {
        var version = versionText.Trim();
        var sha = Sha256Text().Match(shaText.Trim());
        if (!VersionText().IsMatch(version) || !sha.Success)
            return null;
        var name = $"ffmpeg-{version}-full_build.7z";
        return new FfmpegPackage(name, GyanBase + "packages/" + name, null, sha.Value.ToLowerInvariant(), version,
            ArchiveKind.SevenZip, HasWhisper: true);
    }

    private static async Task<FfmpegPackage?> FindGyanAsync(HttpClient http, CancellationToken ct)
    {
        var version = await http.GetStringAsync(GyanBase + "release-version", ct).ConfigureAwait(false);
        var name = $"ffmpeg-{version.Trim()}-full_build.7z";
        if (!VersionText().IsMatch(version.Trim()))
            return null;
        var sha = await http.GetStringAsync(GyanBase + "packages/" + name + ".sha256", ct).ConfigureAwait(false);
        return GyanPackage(version, sha);
    }

    // --- BtbN ---

    [GeneratedRegex(@"^ffmpeg-n(?<major>\d+)\.(?<minor>\d+)-latest-win64-gpl-\d+\.\d+\.zip$")]
    private static partial Regex StableAsset();

    /// <summary>Выбирает из ответа GitHub API самую новую стабильную ветку BtbN; без неё — ночную сборку master.</summary>
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

    private static async Task<FfmpegPackage?> FindBtbNAsync(HttpClient http, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        request.Headers.UserAgent.ParseAdd("FileDial");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return SelectPackage(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
    }

    /// <summary>Сначала сборка с whisper (gyan.dev, если есть tar.exe), при неудаче — BtbN.</summary>
    public static async Task<FfmpegPackage> FindLatestAsync(HttpClient http, CancellationToken ct)
    {
        if (File.Exists(SystemTar))
        {
            try
            {
                if (await FindGyanAsync(http, ct).ConfigureAwait(false) is { } gyan)
                    return gyan;
            }
            catch (HttpRequestException)
            {
                // gyan.dev недоступен — пробуем GitHub
            }
        }
        return await FindBtbNAsync(http, ct).ConfigureAwait(false)
               ?? throw new InvalidDataException("Не нашёлся подходящий архив ffmpeg для Windows.");
    }

    // --- Установка ---

    /// <summary>Скачивает, проверяет и распаковывает ffmpeg.exe и ffprobe.exe в <paramref name="folder"/>.</summary>
    public static async Task<FfmpegTools> InstallAsync(HttpClient http, FfmpegPackage package, string folder,
        IProgress<(long Done, long Total)>? progress, IProgress<string>? stage, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var archive = Path.Combine(folder, package.Name + ".part");
        var staging = Path.Combine(folder, ".staging");
        try
        {
            stage?.Report($"Скачивание ffmpeg {package.Version}");
            await VerifiedDownload.DownloadAsync(http, package.Url, package.SizeBytes, package.Sha256, archive, progress, ct)
                .ConfigureAwait(false);

            stage?.Report("Распаковка");
            OutputFiles.TryDeleteDirectory(staging);
            Directory.CreateDirectory(staging);
            if (package.Kind == ArchiveKind.SevenZip)
                await ExtractWithTarAsync(archive, staging, ct).ConfigureAwait(false);
            else
                await Task.Run(() => ExtractTools(archive, staging, ct), ct).ConfigureAwait(false);

            // Замена готовыми файлами — только после успешной распаковки обоих.
            foreach (var exe in new[] { "ffmpeg.exe", "ffprobe.exe" })
                File.Move(Path.Combine(staging, exe), Path.Combine(folder, exe), overwrite: true);
            return new FfmpegTools(Path.Combine(folder, "ffmpeg.exe"), Path.Combine(folder, "ffprobe.exe"));
        }
        finally
        {
            VerifiedDownload.TryDelete(archive);
            OutputFiles.TryDeleteDirectory(staging);
        }
    }

    /// <summary>Из zip берём только bin\ffmpeg.exe и bin\ffprobe.exe; остальные файлы не нужны.</summary>
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

    /// <summary>
    /// 7z распаковывает tar.exe из Windows (только две нужные программы, по маске */bin/…). bsdtar сам
    /// отказывается от абсолютных путей и «..». Затем файлы переносятся из подпапки сборки в <paramref name="targetFolder"/>.
    /// </summary>
    public static async Task ExtractWithTarAsync(string archivePath, string targetFolder, CancellationToken ct)
    {
        var raw = Path.Combine(targetFolder, "raw");
        Directory.CreateDirectory(raw);
        var result = await ProcessRunner.RunAsync(SystemTar,
            ["-x", "-f", archivePath, "-C", raw, "*/bin/ffmpeg.exe", "*/bin/ffprobe.exe"],
            null, captureOutput: false, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidDataException($"Не удалось распаковать архив ffmpeg (tar, код {result.ExitCode}).");

        foreach (var exe in new[] { "ffmpeg.exe", "ffprobe.exe" })
        {
            var found = Directory.GetFiles(raw, exe, SearchOption.AllDirectories)
                            .FirstOrDefault(p => Path.GetFileName(Path.GetDirectoryName(p)) == "bin")
                        ?? throw new InvalidDataException($"В архиве нет {exe}.");
            File.Move(found, Path.Combine(targetFolder, exe), overwrite: true);
        }
        OutputFiles.TryDeleteDirectory(raw);
    }
}
