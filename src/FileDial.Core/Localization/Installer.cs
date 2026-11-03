namespace FileDial.Core.Localization;

// Окно установки и удаления.

public abstract partial class Texts
{
    // Окно
    public abstract string SetupWindowTitle { get; }
    public abstract string SetupUninstallWindowTitle { get; }
    public abstract string SetupVersion(string version);
    public abstract string SetupNoAdmin { get; }

    // Кнопки
    public abstract string SetupInstall { get; }
    public abstract string SetupUpdate { get; }
    public abstract string SetupReinstall { get; }
    public abstract string SetupRemove { get; }
    public abstract string SetupDone { get; }
    public abstract string SetupBack { get; }
    public abstract string SetupClose { get; }
    public abstract string SetupCancel { get; }

    // Параметры установки
    public abstract string SetupTagline { get; }
    public abstract string SetupSameVersionNote { get; }
    public abstract string SetupUpdateNote(string installedVersion);
    public abstract string SetupFolderTitle { get; }
    public abstract string SetupChangeFolder { get; }
    public abstract string SetupChooseFolder { get; }
    /// <summary>«Нужно 60 МБ · на диске C: свободно 120 ГБ».</summary>
    public abstract string SetupFolderSpace(string needed, string drive, string free);
    public abstract string SetupLookTitle { get; }
    public abstract string SetupThemeLabel { get; }
    public abstract string SetupMoreTitle { get; }
    public abstract string SetupAutostart { get; }
    public abstract string SetupStartMenuShortcut { get; }
    public abstract string SetupDesktopShortcut { get; }

    // Ход и итог установки
    public abstract string SetupInstalling { get; }
    public abstract string SetupUpdating { get; }
    public abstract string SetupPreparing { get; }
    public abstract string SetupClosingRunning { get; }
    public abstract string SetupCancelled { get; }
    public abstract string SetupInstalledTo(string version, string directory);
    public abstract string SetupInstallFailed { get; }
    public abstract string SetupStepSelect { get; }
    public abstract string SetupStepPress(string hotkey);
    public abstract string SetupStepPick { get; }
    public abstract string SetupTrayHint { get; }
    public abstract string SetupLaunch { get; }

    // Удаление
    public abstract string SetupUninstallTitle { get; }
    public abstract string SetupUninstallText(string directory);
    public abstract string SetupRemoveData { get; }
    public abstract string SetupRemoveDataHint(string size);
    public abstract string SetupUninstalling { get; }
    public abstract string SetupRemoved { get; }
    public abstract string SetupRemovedAll { get; }
    public abstract string SetupRemovedKeptData { get; }
    public abstract string SetupUninstallFailed { get; }

    // Ошибки
    public abstract string SetupNoAccess { get; }
    public abstract string SetupFileBusy(string details);
    public abstract string SetupSeeErrorLog { get; }
}

public sealed partial class RuTexts
{
    public override string SetupWindowTitle => "Установка FileDial";
    public override string SetupUninstallWindowTitle => "Удаление FileDial";
    public override string SetupVersion(string version) => $"Версия {version}";
    public override string SetupNoAdmin => "Без прав администратора";

    public override string SetupInstall => "Установить";
    public override string SetupUpdate => "Обновить";
    public override string SetupReinstall => "Переустановить";
    public override string SetupRemove => "Удалить";
    public override string SetupDone => "Готово";
    public override string SetupBack => "Назад";
    public override string SetupClose => "Закрыть";
    public override string SetupCancel => "Отмена";

    public override string SetupTagline =>
        "Выделите файл в Проводнике, нажмите Ctrl+Alt+K — и у курсора появится круг: сжать, конвертировать, обрезать и ещё десятки действий.";
    public override string SetupSameVersionNote => "Эта версия уже установлена — можно переустановить или поменять параметры ниже.";
    public override string SetupUpdateNote(string installedVersion) =>
        $"Установлена версия {installedVersion} — она обновится, настройки и история сохранятся.";
    public override string SetupFolderTitle => "Папка";
    public override string SetupChangeFolder => "Изменить…";
    public override string SetupChooseFolder => "Куда установить FileDial";
    public override string SetupFolderSpace(string needed, string drive, string free) => $"Нужно {needed} · на диске {drive} свободно {free}";
    public override string SetupLookTitle => "Оформление";
    public override string SetupThemeLabel => "Тема";
    public override string SetupMoreTitle => "Дополнительно";
    public override string SetupAutostart => "Запускать вместе с Windows";
    public override string SetupStartMenuShortcut => "Ярлык в меню «Пуск»";
    public override string SetupDesktopShortcut => "Ярлык на рабочем столе";

    public override string SetupInstalling => "Установка";
    public override string SetupUpdating => "Обновление";
    public override string SetupPreparing => "Подготовка";
    public override string SetupClosingRunning => "Закрываю запущенный FileDial…";
    public override string SetupCancelled => "Установка отменена — ничего не изменилось.";
    public override string SetupInstalledTo(string version, string directory) => $"FileDial {version} установлен в {directory}.";
    public override string SetupInstallFailed => "Не получилось установить";
    public override string SetupStepSelect => "Выделите файл в Проводнике или на рабочем столе";
    public override string SetupStepPress(string hotkey) => $"Нажмите {hotkey} — круг появится у курсора";
    public override string SetupStepPick => "Выберите действие мышью или цифрой 1–8";
    public override string SetupTrayHint => "Значок FileDial живёт в области уведомлений: там настройки и история.";
    public override string SetupLaunch => "Запустить FileDial";

    public override string SetupUninstallTitle => "Удалить FileDial";
    public override string SetupUninstallText(string directory) =>
        $"Программа будет удалена из «{directory}» вместе с ярлыками, автозапуском и записью в «Приложениях».";
    public override string SetupRemoveData => "Удалить также настройки, историю и скачанные компоненты";
    public override string SetupRemoveDataHint(string size) =>
        $"ffmpeg, модели распознавания речи и прочее — {size}. Без галочки всё останется для повторной установки.";
    public override string SetupUninstalling => "Удаление";
    public override string SetupRemoved => "FileDial удалён";
    public override string SetupRemovedAll => "Программа, настройки и скачанные компоненты удалены.";
    public override string SetupRemovedKeptData => "Настройки и история сохранены — при повторной установке всё вернётся.";
    public override string SetupUninstallFailed => "Не получилось удалить";

    public override string SetupNoAccess => "Нет доступа к папке или реестру. Выберите папку в своём профиле и попробуйте снова.";
    public override string SetupFileBusy(string details) => $"Файл занят или диск недоступен: {details}";
    public override string SetupSeeErrorLog => "Подробности записаны в журнал ошибок (%LOCALAPPDATA%\\FileDial\\errors.log).";
}

public sealed partial class EnTexts
{
    public override string SetupWindowTitle => "FileDial setup";
    public override string SetupUninstallWindowTitle => "Uninstall FileDial";
    public override string SetupVersion(string version) => $"Version {version}";
    public override string SetupNoAdmin => "No admin rights needed";

    public override string SetupInstall => "Install";
    public override string SetupUpdate => "Update";
    public override string SetupReinstall => "Reinstall";
    public override string SetupRemove => "Uninstall";
    public override string SetupDone => "Done";
    public override string SetupBack => "Back";
    public override string SetupClose => "Close";
    public override string SetupCancel => "Cancel";

    public override string SetupTagline =>
        "Select a file in Explorer, press Ctrl+Alt+K, and a dial with actions appears at the cursor: compress, convert, trim and dozens more.";
    public override string SetupSameVersionNote => "This version is already installed. You can reinstall it or change the options below.";
    public override string SetupUpdateNote(string installedVersion) =>
        $"Version {installedVersion} is installed. It will be updated, and your settings and history will be kept.";
    public override string SetupFolderTitle => "Folder";
    public override string SetupChangeFolder => "Change…";
    public override string SetupChooseFolder => "Where to install FileDial";
    public override string SetupFolderSpace(string needed, string drive, string free) => $"Needs {needed} · {free} free on {drive}";
    public override string SetupLookTitle => "Appearance";
    public override string SetupThemeLabel => "Theme";
    public override string SetupMoreTitle => "Options";
    public override string SetupAutostart => "Start with Windows";
    public override string SetupStartMenuShortcut => "Start menu shortcut";
    public override string SetupDesktopShortcut => "Desktop shortcut";

    public override string SetupInstalling => "Installing";
    public override string SetupUpdating => "Updating";
    public override string SetupPreparing => "Preparing";
    public override string SetupClosingRunning => "Closing the running FileDial…";
    public override string SetupCancelled => "Setup was cancelled. Nothing was changed.";
    public override string SetupInstalledTo(string version, string directory) => $"FileDial {version} is installed in {directory}.";
    public override string SetupInstallFailed => "Could not install";
    public override string SetupStepSelect => "Select a file in Explorer or on the desktop";
    public override string SetupStepPress(string hotkey) => $"Press {hotkey} — the dial appears at the cursor";
    public override string SetupStepPick => "Pick an action with the mouse or keys 1–8";
    public override string SetupTrayHint => "The FileDial icon lives in the notification area, with settings and history.";
    public override string SetupLaunch => "Start FileDial";

    public override string SetupUninstallTitle => "Uninstall FileDial";
    public override string SetupUninstallText(string directory) =>
        $"FileDial will be removed from “{directory}” along with its shortcuts, autostart and its entry in Installed apps.";
    public override string SetupRemoveData => "Also remove settings, history and downloaded components";
    public override string SetupRemoveDataHint(string size) =>
        $"ffmpeg, speech recognition models and more — {size}. If unchecked, everything is kept for a future install.";
    public override string SetupUninstalling => "Uninstalling";
    public override string SetupRemoved => "FileDial is uninstalled";
    public override string SetupRemovedAll => "The program, settings and downloaded components were removed.";
    public override string SetupRemovedKeptData => "Settings and history were kept and will be back if you reinstall.";
    public override string SetupUninstallFailed => "Could not uninstall";

    public override string SetupNoAccess => "No access to the folder or registry. Choose a folder in your user profile and try again.";
    public override string SetupFileBusy(string details) => $"A file is in use or the drive is unavailable: {details}";
    public override string SetupSeeErrorLog => "Details are in the error log (%LOCALAPPDATA%\\FileDial\\errors.log).";
}
