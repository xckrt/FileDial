using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileDial.Core.Jobs;

namespace FileDial.Core.Updates;

/// <summary>Новая версия на GitHub: что показать и что скачать.</summary>
/// <param name="Sha256">Из поля digest ассета; у файлов, загруженных до его появления, — null (проверяем только размер).</param>
public sealed record UpdateInfo(Version Version, string ReleasePage, string SetupUrl, long Size, string? Sha256)
{
    public string VersionText => UpdateChecker.Format(Version);
}

/// <summary>
/// Проверка обновлений по последнему выпуску на GitHub (releases/latest — без черновиков и предварительных).
/// Обновление — это ассет FileDial-Setup-&lt;версия&gt;.exe: тот же установщик, что раздаётся вручную.
/// </summary>
public static partial class UpdateChecker
{
    /// <summary>Автоматически — не чаще раза в неделю.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9._-]+$")]
    private static partial Regex RepositoryPattern();

    /// <summary>«владелец/репозиторий»; пусто или мусор — проверка выключена.</summary>
    public static bool IsValidRepository(string? repository) =>
        !string.IsNullOrWhiteSpace(repository) && RepositoryPattern().IsMatch(repository.Trim());

    public static string ApiUrl(string repository) => $"https://api.github.com/repos/{repository.Trim()}/releases/latest";

    public static string UserAgent(Version current) => $"FileDial/{Format(current)} (Windows; file tool)";

    /// <summary>«1.2.3» — три части, как в имени установщика.</summary>
    public static string Format(Version v) => $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";

    public static string SetupAssetName(Version v) => $"FileDial-Setup-{Format(v)}.exe";

    /// <summary>«v0.3.0», «0.3.0», «v0.3» → 0.3.0; суффиксы «-beta», «+сборка» отбрасываются. null — не версия.</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];
        var end = text.IndexOfAny(['-', '+']);
        if (end >= 0)
            text = text[..end];
        if (!Version.TryParse(text, out var v) || text.Split('.').Length < 2)
            return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>Сравнение без четвёртой части (её в версиях FileDial нет).</summary>
    public static bool IsNewer(Version candidate, Version current) =>
        new Version(candidate.Major, candidate.Minor, Math.Max(0, candidate.Build)) >
        new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    /// <summary>
    /// Разбор ответа releases/latest. null — версия не новее, это не выпуск или в нём нет установщика.
    /// </summary>
    public static UpdateInfo? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;
        if (Bool(root, "draft") || Bool(root, "prerelease"))
            return null;
        if (ParseVersion(Str(root, "tag_name")) is not { } version || !IsNewer(version, current))
            return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var wanted = SetupAssetName(version);
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(Str(asset, "name"), wanted, StringComparison.OrdinalIgnoreCase))
                continue;
            var url = Str(asset, "browser_download_url");
            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
            // Качаем только по https с github.com: ответ API не должен увести загрузку куда угодно.
            if (url is null || size <= 0 || !IsGitHubHttps(url))
                return null;
            var page = Str(root, "html_url") is { } html && IsGitHubHttps(html) ? html : url;
            return new UpdateInfo(version, page, url, size, Sha256FromDigest(Str(asset, "digest")));
        }
        return null;
    }

    /// <summary>«sha256:…» → 64 шестнадцатеричных символа; другой алгоритм или мусор — null.</summary>
    public static string? Sha256FromDigest(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var hex = digest[prefix.Length..].Trim();
        return hex.Length == 64 && hex.All(char.IsAsciiHexDigit) ? hex.ToLowerInvariant() : null;
    }

    private static bool IsGitHubHttps(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Запрос к GitHub. Нет выпусков (404) — null; сетевые ошибки — исключение (вызывающий их только пишет в журнал).</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, string repository, Version current, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl(repository));
        request.Headers.UserAgent.ParseAdd(UserAgent(current));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), current);
    }

    /// <summary>Установщик — в новую папку %TEMP%\FileDial-update-&lt;guid&gt;; возвращает путь к exe.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo update, string tempRoot,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        var folder = Path.Combine(tempRoot, DownloadFolderPrefix + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(folder, SetupAssetName(update.Version));
        var part = target + ".part";
        try
        {
            await VerifiedDownload.DownloadAsync(http, update.SetupUrl, update.Size, update.Sha256, part, progress, ct)
                .ConfigureAwait(false);
            File.Move(part, target);
            return target;
        }
        catch
        {
            TryDeleteFolder(folder);
            throw;
        }
    }

    public const string DownloadFolderPrefix = "FileDial-update-";

    /// <summary>Скачанные установщики старше суток (установка давно закончилась) — убрать.</summary>
    public static void CleanOldDownloads(string tempRoot, DateTime utcNow)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(tempRoot, DownloadFolderPrefix + "*"))
                if (utcNow - Directory.GetLastWriteTimeUtc(dir) > TimeSpan.FromDays(1))
                    TryDeleteFolder(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>Когда проверяли в последний раз: маленький файл %APPDATA%\FileDial\update-check.txt (дата в UTC).</summary>
public static class UpdateSchedule
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileDial", "update-check.txt");

    /// <summary>Пора ли проверять. Дата из будущего (переводили часы) — тоже пора.</summary>
    public static bool IsDue(DateTime? lastUtc, DateTime utcNow) =>
        lastUtc is not { } last || last > utcNow || utcNow - last >= UpdateChecker.Interval;

    public static DateTime? LoadLast(string path)
    {
        try
        {
            return File.Exists(path) && DateTime.TryParse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void SaveLast(string path, DateTime utcNow)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, utcNow.ToString("O", CultureInfo.InvariantCulture));
    }
}
