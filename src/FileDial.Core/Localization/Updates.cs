namespace FileDial.Core.Localization;

// Обновления: уведомление о новой версии, загрузка, ручная проверка из трея, галочка в настройках.

public abstract partial class Texts
{
    /// <summary>Галочка в настройках («Общие»).</summary>
    public abstract string UpdCheckSetting { get; }
    /// <summary>Пункт меню трея.</summary>
    public abstract string UpdTrayCheckNow { get; }

    public abstract string UpdAvailableTitle(string version);
    public abstract string UpdAvailableText { get; }
    public abstract string UpdDownloadingTitle { get; }
    public abstract string UpdDownloadingText { get; }
    public abstract string UpdFailedTitle { get; }
    public abstract string UpdFailedText { get; }
    public abstract string UpdUpToDate(string version);
    public abstract string UpdCheckFailed { get; }
    /// <summary>Переносная копия или выпуск без контрольной суммы: молча не ставим — открываем страницу выпуска.</summary>
    public abstract string UpdManualText { get; }
}

public sealed partial class RuTexts
{
    public override string UpdCheckSetting => "Проверять обновления раз в неделю";
    public override string UpdTrayCheckNow => "Проверить обновления";

    public override string UpdAvailableTitle(string version) => $"Вышла FileDial {version}";
    public override string UpdAvailableText => "Нажмите, чтобы скачать и установить. Программа перезапустится сама.";
    public override string UpdDownloadingTitle => "Загружаем обновление";
    public override string UpdDownloadingText => "Когда оно установится, FileDial перезапустится.";
    public override string UpdFailedTitle => "Не удалось обновить";
    public override string UpdFailedText => "Нажмите, чтобы открыть страницу выпуска и скачать установщик вручную.";
    public override string UpdUpToDate(string version) => $"У вас последняя версия — {version}.";
    public override string UpdCheckFailed => "Не удалось проверить обновления. Проверьте подключение к интернету.";
    public override string UpdManualText => "Нажмите, чтобы открыть страницу выпуска и скачать новую версию вручную.";
}

public sealed partial class EnTexts
{
    public override string UpdCheckSetting => "Check for updates weekly";
    public override string UpdTrayCheckNow => "Check for updates";

    public override string UpdAvailableTitle(string version) => $"FileDial {version} is available";
    public override string UpdAvailableText => "Click to download and install it. FileDial will restart on its own.";
    public override string UpdDownloadingTitle => "Downloading the update";
    public override string UpdDownloadingText => "FileDial will restart once it is installed.";
    public override string UpdFailedTitle => "Couldn't update";
    public override string UpdFailedText => "Click to open the release page and download the installer manually.";
    public override string UpdUpToDate(string version) => $"You have the latest version, {version}.";
    public override string UpdCheckFailed => "Couldn't check for updates. Check your internet connection.";
    public override string UpdManualText => "Click to open the release page and download the new version manually.";
}
