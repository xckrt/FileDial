namespace FileDial.Core.Localization;

// Установщик: проверка папки, этапы установки и удаления, описание программы.

public abstract partial class Texts
{
    public abstract string InstallNeedFullPath { get; }
    public abstract string InstallNoNetwork { get; }
    public abstract string InstallNotWindowsFolder { get; }
    public abstract string InstallDriveNotReady { get; }
    public abstract string InstallNoDrive { get; }
    public abstract string InstallNoWriteAccess { get; }
    public abstract string InstallLowSpace(string needed, string free);

    public abstract string InstallStageCopying { get; }
    public abstract string InstallStageRegistering { get; }
    public abstract string InstallStageShortcuts { get; }
    public abstract string InstallDone { get; }
    /// <summary>Описание в «Приложениях» и в ярлыке.</summary>
    public abstract string InstallDescription { get; }

    public abstract string InstallStageAutostart { get; }
    public abstract string InstallStageAppsEntry { get; }
    public abstract string InstallStageData { get; }
    public abstract string InstallStageFiles { get; }
}

public sealed partial class RuTexts
{
    public override string InstallNeedFullPath => "Укажите полный путь к папке.";
    public override string InstallNoNetwork => "Сетевые папки не подходят — выберите папку на этом компьютере.";
    public override string InstallNotWindowsFolder => "Папка Windows не подходит.";
    public override string InstallDriveNotReady => "Диск недоступен.";
    public override string InstallNoDrive => "Такого диска нет.";
    public override string InstallNoWriteAccess =>
        "Нет прав на запись в эту папку. Program Files требует прав администратора — " +
        "выберите папку в своём профиле или на другом диске.";
    public override string InstallLowSpace(string needed, string free) => $"Мало места: нужно {needed}, свободно {free}.";

    public override string InstallStageCopying => "Копирование программы";
    public override string InstallStageRegistering => "Регистрация в Windows";
    public override string InstallStageShortcuts => "Ярлыки и автозапуск";
    public override string InstallDone => "Готово";
    public override string InstallDescription => "Круговое меню действий для файлов";

    public override string InstallStageAutostart => "Автозапуск и ярлыки";
    public override string InstallStageAppsEntry => "Запись в «Приложениях»";
    public override string InstallStageData => "Настройки и данные";
    public override string InstallStageFiles => "Файлы программы";
}

public sealed partial class EnTexts
{
    public override string InstallNeedFullPath => "Enter the full path to the folder.";
    public override string InstallNoNetwork => "Network folders won't work. Choose a folder on this computer.";
    public override string InstallNotWindowsFolder => "The Windows folder won't work.";
    public override string InstallDriveNotReady => "The drive is not available.";
    public override string InstallNoDrive => "There is no such drive.";
    public override string InstallNoWriteAccess =>
        "No permission to write to this folder. Program Files needs administrator rights, " +
        "so choose a folder in your profile or on another drive.";
    public override string InstallLowSpace(string needed, string free) => $"Not enough space: {needed} needed, {free} free.";

    public override string InstallStageCopying => "Copying the program";
    public override string InstallStageRegistering => "Registering with Windows";
    public override string InstallStageShortcuts => "Shortcuts and autostart";
    public override string InstallDone => "Done";
    public override string InstallDescription => "Radial action menu for files";

    public override string InstallStageAutostart => "Autostart and shortcuts";
    public override string InstallStageAppsEntry => "Entry in Installed apps";
    public override string InstallStageData => "Settings and data";
    public override string InstallStageFiles => "Program files";
}
