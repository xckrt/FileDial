namespace FileDial.Core.Localization;

// Оболочка: трей и запуск, чтение выделения в Проводнике, горячая клавиша, автозапуск, PDF средствами Windows.

public abstract partial class Texts
{
    // Запуск и трей
    public abstract string ShellAlreadyRunning { get; }
    public abstract string ShellNeedsFfmpegTitle { get; }
    public abstract string ShellNeedsFfmpegText { get; }
    /// <summary>Сочетание не зарегистрировалось при запуске: причина и что делать.</summary>
    public abstract string ShellHotkeyStartupProblem(string reason);
    /// <summary>Сочетание из настроек не зарегистрировалось: причина и просьба выбрать другое.</summary>
    public abstract string ShellHotkeyChooseAnother(string reason);
    public abstract string ShellSettingsSaveFailed { get; }
    public abstract string ShellSomethingWrong { get; }
    public abstract string ShellArchivesCount(int n);
    public abstract string ShellExtractWhere { get; }
    public abstract string ShellAlreadyRunningForFile { get; }

    // Выделение
    public abstract string ShellExplorerNotResponding { get; }
    public abstract string ShellNoActiveWindow { get; }
    public abstract string ShellNotExplorer { get; }
    public abstract string ShellElevatedWindow { get; }
    public abstract string ShellNoExplorerAccess { get; }
    public abstract string ShellSelectionReadFailed { get; }
    public abstract string ShellExplorerWindowNotFound { get; }
    public abstract string ShellNoActiveTab { get; }
    public abstract string ShellDesktopUnavailable { get; }

    // Горячая клавиша и автозапуск
    public abstract string ShellHotkeyTaken(string gesture);
    public abstract string ShellHotkeyFailed(string gesture, int error);
    public abstract string ShellAutostartFailed { get; }

    // PDF: страницы в картинки, распознавание текста
    public abstract string ShellPdfOpenFailed(string name);
    public abstract string ShellReadingPdf { get; }
    public abstract string ShellPageOf(int page, int count);
    /// <summary>Имя картинки страницы без расширения: «отчёт — стр. 01».</summary>
    public abstract string ShellPageImageName(string stem, string number);
    /// <summary>Папка с картинками страниц: «отчёт (картинки)».</summary>
    public abstract string ShellImagesFolderName(string stem);
    public abstract string ShellProgressDone { get; }
    public abstract string ShellImagesSaved(int count, string folder);
    public abstract string ShellOcrNoLanguage { get; }
    public abstract string ShellOcrUnavailable { get; }
    public abstract string ShellRecognizingPage(int page, int count);
    /// <summary>Разделитель страниц в распознанном тексте: «— Страница 2 —».</summary>
    public abstract string ShellPageMarker(int page);
    public abstract string ShellNoTextFound { get; }
    public abstract string ShellOcrDone(string pages, string language, string file);
}

public sealed partial class RuTexts
{
    public override string ShellAlreadyRunning => "FileDial уже запущен — значок в области уведомлений.";
    public override string ShellNeedsFfmpegTitle => "FileDial: нужен ffmpeg";
    public override string ShellNeedsFfmpegText => "Нажмите, чтобы установить его одной кнопкой (около 170 МБ, один раз).";
    public override string ShellHotkeyStartupProblem(string reason) =>
        $"{reason}\n\nВыберите другое сочетание в настройках или закройте программу, которая его использует.";
    public override string ShellHotkeyChooseAnother(string reason) => $"{reason} Выберите другое.";
    public override string ShellSettingsSaveFailed => "Не удалось сохранить настройки: нет доступа к папке настроек.";
    public override string ShellSomethingWrong => "Что-то пошло не так. Подробности в журнале ошибок.";
    public override string ShellArchivesCount(int n) => $"Архивов: {n}";
    public override string ShellExtractWhere => "Куда распаковать";
    public override string ShellAlreadyRunningForFile => "Это уже выполняется для этого файла";

    public override string ShellExplorerNotResponding => "Проводник не отвечает";
    public override string ShellNoActiveWindow => "Не удалось определить активное окно";
    public override string ShellNotExplorer => "Активное окно — не Проводник";
    public override string ShellElevatedWindow => "Окно запущено с правами администратора — выделение недоступно";
    public override string ShellNoExplorerAccess => "Нет доступа к окну Проводника";
    public override string ShellSelectionReadFailed => "Не удалось прочитать выделение";
    public override string ShellExplorerWindowNotFound => "Окно Проводника не найдено в списке окон оболочки";
    public override string ShellNoActiveTab => "Не удалось определить активную вкладку Проводника";
    public override string ShellDesktopUnavailable => "Рабочий стол недоступен";

    public override string ShellHotkeyTaken(string gesture) => $"Сочетание {gesture} уже занято другой программой.";
    public override string ShellHotkeyFailed(string gesture, int error) => $"Не удалось зарегистрировать {gesture} (код ошибки {error}).";
    public override string ShellAutostartFailed => "Не удалось изменить автозапуск: нет доступа к реестру.";

    public override string ShellPdfOpenFailed(string name) => $"Windows не смогла открыть {name}: файл повреждён или это не PDF.";
    public override string ShellReadingPdf => "Чтение PDF";
    public override string ShellPageOf(int page, int count) => $"Страница {page} из {count}";
    public override string ShellPageImageName(string stem, string number) => $"{stem} — стр. {number}";
    public override string ShellImagesFolderName(string stem) => $"{stem} (картинки)";
    public override string ShellProgressDone => "Готово";
    public override string ShellImagesSaved(int count, string folder) => $"{Images(count)} → папка {folder}.";
    public override string ShellOcrNoLanguage =>
        "В Windows нет языка для распознавания текста: Параметры → Время и язык → Язык → ваш язык → Распознавание текста.";
    public override string ShellOcrUnavailable => "Распознавание текста недоступно.";
    public override string ShellRecognizingPage(int page, int count) => $"Распознавание · страница {page} из {count}";
    public override string ShellPageMarker(int page) => $"— Страница {page} —";
    public override string ShellNoTextFound => "Текст на страницах не найден.";
    public override string ShellOcrDone(string pages, string language, string file) =>
        $"{pages}, язык: {language}. Сохранено как {file}.";
}

public sealed partial class EnTexts
{
    public override string ShellAlreadyRunning => "FileDial is already running — its icon is in the notification area.";
    public override string ShellNeedsFfmpegTitle => "FileDial: ffmpeg needed";
    public override string ShellNeedsFfmpegText => "Click to install it in one step (about 170 MB, once).";
    public override string ShellHotkeyStartupProblem(string reason) =>
        $"{reason}\n\nChoose another shortcut in settings or close the program that uses it.";
    public override string ShellHotkeyChooseAnother(string reason) => $"{reason} Choose another one.";
    public override string ShellSettingsSaveFailed => "Could not save settings: no access to the settings folder.";
    public override string ShellSomethingWrong => "Something went wrong. Details are in the error log.";
    public override string ShellArchivesCount(int n) => $"Archives: {n}";
    public override string ShellExtractWhere => "Extract to";
    public override string ShellAlreadyRunningForFile => "This is already running for this file";

    public override string ShellExplorerNotResponding => "File Explorer is not responding";
    public override string ShellNoActiveWindow => "Could not find the active window";
    public override string ShellNotExplorer => "The active window is not File Explorer";
    public override string ShellElevatedWindow => "The window runs as administrator, so its selection is unavailable";
    public override string ShellNoExplorerAccess => "No access to the File Explorer window";
    public override string ShellSelectionReadFailed => "Could not read the selection";
    public override string ShellExplorerWindowNotFound => "The File Explorer window was not found among shell windows";
    public override string ShellNoActiveTab => "Could not find the active File Explorer tab";
    public override string ShellDesktopUnavailable => "The desktop is unavailable";

    public override string ShellHotkeyTaken(string gesture) => $"{gesture} is already used by another program.";
    public override string ShellHotkeyFailed(string gesture, int error) => $"Could not register {gesture} (error code {error}).";
    public override string ShellAutostartFailed => "Could not change autostart: no access to the registry.";

    public override string ShellPdfOpenFailed(string name) => $"Windows could not open {name}: the file is damaged or not a PDF.";
    public override string ShellReadingPdf => "Reading PDF";
    public override string ShellPageOf(int page, int count) => $"Page {page} of {count}";
    public override string ShellPageImageName(string stem, string number) => $"{stem} — page {number}";
    public override string ShellImagesFolderName(string stem) => $"{stem} (images)";
    public override string ShellProgressDone => "Done";
    public override string ShellImagesSaved(int count, string folder) => $"{Images(count)} → folder {folder}.";
    public override string ShellOcrNoLanguage =>
        "Windows has no text recognition language: Settings → Time & language → Language & region → your language → " +
        "Language options → Optical character recognition.";
    public override string ShellOcrUnavailable => "Text recognition is unavailable.";
    public override string ShellRecognizingPage(int page, int count) => $"Recognizing · page {page} of {count}";
    public override string ShellPageMarker(int page) => $"— Page {page} —";
    public override string ShellNoTextFound => "No text found on the pages.";
    public override string ShellOcrDone(string pages, string language, string file) =>
        $"{pages}, language: {language}. Saved as {file}.";
}
