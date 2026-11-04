using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using FileDial.App.Tray;
using FileDial.Core.Localization;
using FileDial.Core.Settings;
using FileDial.Core.Updates;

namespace FileDial.App.Updates;

/// <summary>
/// Обновление из трея: в фоне после запуска (раз в неделю) или по пункту меню. Нашлась новая версия — уведомление;
/// клик — скачать установщик и запустить его тихо (<c>--install --quiet --start</c>): он закроет эту копию,
/// заменит exe и запустит новую. Ошибки сети при фоновой проверке — только в журнал.
/// </summary>
internal sealed class AppUpdater(TrayIcon tray, Func<AppSettings> settings)
{
    /// <summary>Не мешаем запуску: проверяем через полминуты.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    // Без общего тайм-аута: зависшую загрузку ловит VerifiedDownload, проверку — CheckTimeout.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Репозиторий из сборки (свойство UpdateRepository в FileDial.App.csproj); null — функция выключена.</summary>
    public static string? Repository { get; } = typeof(AppUpdater).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepository")?.Value is { } repo && UpdateChecker.IsValidRepository(repo)
        ? repo.Trim()
        : null;

    public static bool Enabled => Repository is not null;

    private static Version Current => typeof(AppUpdater).Assembly.GetName().Version ?? new Version(0, 0, 0);

    private bool _checking;
    private bool _installing;

    /// <summary>Фоновая проверка после запуска — если включена в настройках и прошла неделя.</summary>
    public async Task CheckOnStartupAsync()
    {
        if (!Enabled)
            return;
        UpdateChecker.CleanOldDownloads(Path.GetTempPath(), DateTime.UtcNow);
        await Task.Delay(StartupDelay);
        if (settings().CheckForUpdates && UpdateSchedule.IsDue(UpdateSchedule.LoadLast(UpdateSchedule.DefaultPath), DateTime.UtcNow))
            await CheckAsync(manual: false);
    }

    /// <summary>Пункт меню трея: результат сообщаем в любом случае.</summary>
    public void CheckNow() => _ = CheckAsync(manual: true);

    private async Task CheckAsync(bool manual)
    {
        if (_checking || _installing || Repository is not { } repository)
            return;
        _checking = true;
        UpdateInfo? update;
        try
        {
            using var cts = new CancellationTokenSource(CheckTimeout);
            update = await UpdateChecker.CheckAsync(Http, repository, Current, cts.Token);
            TrySaveCheckTime();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Проверка обновлений", ex);
            if (manual)
                tray.Notify("FileDial", Lang.T.UpdCheckFailed, () => { });
            return;
        }
        finally
        {
            _checking = false;
        }

        if (update is not null)
            tray.Notify(Lang.T.UpdAvailableTitle(update.VersionText), Lang.T.UpdAvailableText, () => _ = InstallAsync(update));
        else if (manual)
            tray.Notify("FileDial", Lang.T.UpdUpToDate(UpdateChecker.Format(Current)), () => { });
    }

    private static void TrySaveCheckTime()
    {
        try
        {
            UpdateSchedule.SaveLast(UpdateSchedule.DefaultPath, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Проверка обновлений", ex);
        }
    }

    private async Task InstallAsync(UpdateInfo update)
    {
        if (_installing)
            return;
        _installing = true;
        tray.Notify(Lang.T.UpdDownloadingTitle, Lang.T.UpdDownloadingText, () => { });
        try
        {
            var setup = await UpdateChecker.DownloadAsync(Http, update, Path.GetTempPath(), null, CancellationToken.None);
            // Установщик попросит эту копию закрыться (RunningCopies) и после замены запустит новую (--start).
            var psi = new ProcessStartInfo(setup)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(setup)!,
            };
            foreach (var arg in new[] { "--install", "--quiet", "--start" })
                psi.ArgumentList.Add(arg);
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Загрузка обновления", ex);
            _installing = false;
            tray.Notify(Lang.T.UpdFailedTitle, Lang.T.UpdFailedText, () => OpenPage(update.ReleasePage));
        }
    }

    private static void OpenPage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Страница выпуска", ex);
        }
    }
}
