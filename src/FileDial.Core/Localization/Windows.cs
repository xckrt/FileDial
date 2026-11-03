namespace FileDial.Core.Localization;

// Окна приложения: настройки, выполнение действия, история, обрезка, пароль, части, страницы, содержимое архива;
// значок в трее и подсказки круга.

/// <summary>Что защищено паролем — от этого зависит род и форма слова в подписях.</summary>
public enum UiPasswordSubject
{
    Archive,
    Pdf,
    Document,
}

public abstract partial class Texts
{
    // Общие кнопки
    public abstract string UiCancel { get; }
    public abstract string UiClose { get; }
    public abstract string UiSave { get; }
    public abstract string UiBrowse { get; }
    public abstract string UiReset { get; }
    public abstract string UiFfmpegNotFound { get; }
    /// <summary>«clip.mp4: 5 МБ из 20 МБ».</summary>
    public abstract string UiProgressOf(string what, string done, string total);

    // Темы
    public abstract string UiThemeGraphite { get; }
    public abstract string UiThemeBrass { get; }
    public abstract string UiThemePaper { get; }

    // Настройки
    public abstract string UiSettingsWindowTitle { get; }
    public abstract string UiSettingsTitle { get; }
    public abstract string UiHotkeyTitle { get; }
    public abstract string UiHotkeyHint { get; }
    public abstract string UiHotkeyPress { get; }
    public abstract string UiHotkeyEscHint { get; }
    public abstract string UiHotkeyAfterSave { get; }
    public abstract string UiAppearanceTitle { get; }
    public abstract string UiGeneralTitle { get; }
    public abstract string UiAutostart { get; }
    public abstract string UiAnimations { get; }
    public abstract string UiKeepHistory { get; }
    public abstract string UiOutputTitle { get; }
    public abstract string UiOutputNextToSource { get; }
    public abstract string UiOutputToFolder { get; }
    public abstract string UiOpenFolderWhenDone { get; }
    public abstract string UiVideoTargetTitle { get; }
    public abstract string UiImageTargetTitle { get; }
    public abstract string UiSpeechTitle { get; }
    public abstract string UiOutputFolderDialog { get; }
    public abstract string UiChooseOutputFolder { get; }
    public abstract string UiOutputFolderMissing { get; }

    // Настройки: ffmpeg
    public abstract string UiFfmpegFolderDialog { get; }
    public abstract string UiFfmpegAuto { get; }
    public abstract string UiFfmpegNotInFolderUsing(string path);
    public abstract string UiFfmpegNotFoundAnywhere { get; }
    public abstract string UiFfmpegFound(string path);
    public abstract string UiFfmpegMissing { get; }
    public abstract string UiFfmpegInstall { get; }
    public abstract string UiFfmpegInstallWithSpeech { get; }
    public abstract string UiFfmpegUpdate { get; }
    public abstract string UiFfmpegInstallTip(string folder);
    public abstract string UiFfmpegSearching { get; }
    public abstract string UiFfmpegInstalled(string path);
    public abstract string UiFfmpegInstallCancelled { get; }
    public abstract string UiFfmpegDownloadFailed { get; }

    // Настройки: распознавание речи
    public abstract string UiWhisperModelDialog { get; }
    public abstract string UiWhisperModelFilter { get; }
    public abstract string UiWhisperNotInstalled { get; }
    public abstract string UiWhisperAuto(string path);
    public abstract string UiWhisperFileMissing { get; }
    public abstract string UiWhisperChosen(string name, string size);
    public abstract string UiWhisperUsing(string name, string size);
    public abstract string UiWhisperMissing { get; }
    public abstract string UiModelDownloaded(string label);
    public abstract string UiModelDownload(string label, string size);
    public abstract string UiModelAccurateTip { get; }
    public abstract string UiModelFastTip { get; }
    public abstract string UiConnecting(string fileName);
    public abstract string UiModelDownloadedVerified(string fileName);
    public abstract string UiDownloadCancelled { get; }
    public abstract string UiDownloadFailed { get; }

    // Выполнение действия
    public abstract string UiShowInFolder { get; }
    public abstract string UiInstallFfmpegLink { get; }
    public abstract string UiJobPreparing { get; }
    public abstract string UiJobNeedsFfmpeg { get; }
    public abstract string UiJobNotImplemented { get; }
    public abstract string UiJobError { get; }
    public abstract string UiJobNothingToDo { get; }
    public abstract string UiJobDone { get; }
    public abstract string UiJobCancelledOnExit { get; }
    public abstract string UiJobCancelled { get; }
    public abstract string UiJobCancelledDetails { get; }
    public abstract string UiJobSourceNotFound { get; }
    public abstract string UiJobNoAccess { get; }
    public abstract string UiJobFailed(string message);
    public abstract string UiJobNeedsPassword { get; }
    public abstract string UiJobCancelling { get; }

    // История
    public abstract string UiHistoryWindowTitle { get; }
    public abstract string UiHistoryTitle { get; }
    public abstract string UiHistoryClear { get; }
    public abstract string UiHistoryConfirmClear { get; }
    public abstract string UiHistoryEmpty { get; }
    public abstract string UiHistoryDisabled { get; }
    public abstract string UiHistorySummary(int count, int done);
    public abstract string UiHistorySummaryDisabled { get; }
    public abstract string UiHistoryEmptyHint { get; }
    public abstract string UiHistoryEnableHint { get; }
    public abstract string UiShowResult { get; }
    public abstract string UiResultGone { get; }
    public abstract string UiShowSource { get; }

    // Обрезка
    public abstract string UiTrimWindowTitle { get; }
    public abstract string UiReadingFile { get; }
    public abstract string UiTrimStart { get; }
    public abstract string UiTrimEnd { get; }
    public abstract string UiTrimPrecise { get; }
    public abstract string UiTrimNeedsFfmpeg { get; }
    public abstract string UiDurationUnknown { get; }
    public abstract string UiTrimNoDuration { get; }
    public abstract string UiDuration(string duration);
    public abstract string UiTrimStartInvalid { get; }
    public abstract string UiTrimEndInvalid { get; }
    public abstract string UiTrimPrompt { get; }
    public abstract string UiSegmentLength(string length);

    // Пароль
    public abstract string UiPasswordWindowTitle { get; }
    public abstract string UiPassword { get; }
    public abstract string UiPasswordRepeat { get; }
    public abstract string UiProtect { get; }
    public abstract string UiOpen { get; }
    /// <summary>«Архив защищён паролем».</summary>
    public abstract string UiPasswordProtected(UiPasswordSubject subject);
    public abstract string UiPasswordWarning(int minLength, UiPasswordSubject subject);
    public abstract string UiWrongPassword { get; }
    public abstract string UiPasswordTarUnsafe { get; }
    public abstract string UiPasswordTooShort(int minLength);
    public abstract string UiPasswordRepeatPrompt { get; }
    public abstract string UiPasswordsDiffer { get; }
    public abstract string UiPasswordsMatch { get; }

    // Части
    public abstract string UiSplitWindowTitle { get; }
    public abstract string UiPartSize { get; }
    public abstract string UiSplitHint { get; }
    public abstract string UiSplit { get; }
    public abstract string UiFileSmallerThanPart { get; }
    public abstract string UiFileTooSmallToSplit { get; }
    public abstract string UiTooManyParts { get; }
    /// <summary>«Получится 5 частей.»</summary>
    public abstract string UiWillProduce(string result);

    // Страницы
    public abstract string UiPagesWindowTitle { get; }
    public abstract string UiPagesInDocument(string pages);
    public abstract string UiPagesCountUnknown { get; }
    public abstract string UiPages { get; }
    public abstract string UiPagesHint { get; }
    public abstract string UiExtract { get; }

    // Содержимое архива
    public abstract string UiContentsWindowTitle { get; }
    public abstract string UiArchiveContents { get; }
    public abstract string UiReadingArchive { get; }
    public abstract string UiArchiveEmpty { get; }
    public abstract string UiArchiveSummary(string files, string unpacked, string packed);

    // Трей
    public abstract string UiTraySettings { get; }
    public abstract string UiTrayHistory { get; }
    public abstract string UiTrayHelp { get; }
    public abstract string UiTrayExit { get; }
    public abstract string UiTrayNoHotkey { get; }
    public abstract string UiTrayTipNoHotkey { get; }
    public abstract string UiTrayTip(string hotkey);

    // Подсказки круга
    public abstract string UiDialEnterAction(string action);
    public abstract string UiDialChooseFormat { get; }
    public abstract string UiDialEnterDownload(string reason);
    public abstract string UiDialEnterRun { get; }
    public abstract string UiDialEnterOrClick(string action);
    public abstract string UiDialEscClose { get; }
    public abstract string UiDialKeys { get; }
    public abstract string UiDialNoActions { get; }
}

public sealed partial class RuTexts
{
    public override string UiCancel => "Отмена";
    public override string UiClose => "Закрыть";
    public override string UiSave => "Сохранить";
    public override string UiBrowse => "Выбрать…";
    public override string UiReset => "Сбросить";
    public override string UiFfmpegNotFound => "ffmpeg не найден";
    public override string UiProgressOf(string what, string done, string total) => $"{what}: {done} из {total}";

    public override string UiThemeGraphite => "Графит";
    public override string UiThemeBrass => "Латунь";
    public override string UiThemePaper => "Бумага";

    public override string UiSettingsWindowTitle => "FileDial — настройки";
    public override string UiSettingsTitle => "Настройки";
    public override string UiHotkeyTitle => "Горячая клавиша";
    public override string UiHotkeyHint => "Щёлкните поле и нажмите сочетание: Ctrl, Alt или Win вместе с буквой, цифрой или F1–F24.";
    public override string UiHotkeyPress => "Нажмите сочетание…";
    public override string UiHotkeyEscHint => "Esc — оставить как было.";
    public override string UiHotkeyAfterSave => "Сочетание будет применено после «Сохранить».";
    public override string UiAppearanceTitle => "Оформление";
    public override string UiGeneralTitle => "Общее";
    public override string UiAutostart => "Запускать вместе с Windows";
    public override string UiAnimations => "Анимации";
    public override string UiKeepHistory => "Вести историю действий (хранится только на этом компьютере)";
    public override string UiOutputTitle => "Куда сохранять результаты";
    public override string UiOutputNextToSource => "Рядом с исходным файлом";
    public override string UiOutputToFolder => "В выбранную папку";
    public override string UiOpenFolderWhenDone => "Открывать папку с результатом после завершения";
    public override string UiVideoTargetTitle => "Сжатие видео — цель";
    public override string UiImageTargetTitle => "Сжатие картинок — цель";
    public override string UiSpeechTitle => "Распознавание речи (субтитры, текст)";
    public override string UiOutputFolderDialog => "Папка для результатов";
    public override string UiChooseOutputFolder => "Выберите папку для результатов.";
    public override string UiOutputFolderMissing => "Папка для результатов не найдена.";

    public override string UiFfmpegFolderDialog => "Папка с ffmpeg.exe и ffprobe.exe";
    public override string UiFfmpegAuto => "Авто (рядом с программой или PATH)";
    public override string UiFfmpegNotInFolderUsing(string path) => $"В выбранной папке ffmpeg нет — будет использован {path}";
    public override string UiFfmpegNotFoundAnywhere => "В выбранной папке нет ffmpeg.exe и ffprobe.exe, в PATH тоже не найден.";
    public override string UiFfmpegFound(string path) => $"Найден: {path}";
    public override string UiFfmpegMissing => "ffmpeg не найден — установите его кнопкой ниже, это нужно один раз.";
    public override string UiFfmpegInstall => "Установить ffmpeg (около 170 МБ)";
    public override string UiFfmpegInstallWithSpeech => "Установить ffmpeg с распознаванием речи";
    public override string UiFfmpegUpdate => "Обновить ffmpeg";
    public override string UiFfmpegInstallTip(string folder) =>
        "Сборка gyan.dev «full» (с распознаванием речи); проверяется по контрольной сумме. Ставится в " + folder;
    public override string UiFfmpegSearching => "Поиск последней версии…";
    public override string UiFfmpegInstalled(string path) => $"ffmpeg установлен: {path}";
    public override string UiFfmpegInstallCancelled => "Установка ffmpeg отменена.";
    public override string UiFfmpegDownloadFailed => "Не удалось скачать ffmpeg: нет подключения к интернету или GitHub недоступен.";

    public override string UiWhisperModelDialog => "Модель whisper.cpp";
    public override string UiWhisperModelFilter => "Модель whisper (ggml-*.bin)|*.bin|Все файлы|*.*";
    public override string UiWhisperNotInstalled => "Не установлена";
    public override string UiWhisperAuto(string path) => $"Авто: {path}";
    public override string UiWhisperFileMissing => "Файл модели не найден.";
    public override string UiWhisperChosen(string name, string size) => $"Модель: {name}, {size}. Работает офлайн.";
    public override string UiWhisperUsing(string name, string size) => $"Используется {name} ({size}). Работает офлайн.";
    public override string UiWhisperMissing => "Модель не установлена — скачайте её кнопкой ниже. Это нужно один раз, дальше всё работает офлайн.";
    public override string UiModelDownloaded(string label) => $"{label}: скачана";
    public override string UiModelDownload(string label, string size) => $"Скачать: {label.ToLowerInvariant()} ({size})";
    public override string UiModelAccurateTip => "Заметно точнее для русского, медленнее";
    public override string UiModelFastTip => "Быстрая, для простой речи";
    public override string UiConnecting(string fileName) => $"{fileName}: подключение…";
    public override string UiModelDownloadedVerified(string fileName) => $"Модель {fileName} скачана и проверена.";
    public override string UiDownloadCancelled => "Загрузка отменена.";
    public override string UiDownloadFailed => "Не удалось скачать: нет подключения к интернету или сервер недоступен.";

    public override string UiShowInFolder => "Показать в папке";
    public override string UiInstallFfmpegLink => "Установить ffmpeg одной кнопкой";
    public override string UiJobPreparing => "Подготовка";
    public override string UiJobNeedsFfmpeg => "Для этого действия нужен ffmpeg — его ещё нет на компьютере.";
    public override string UiJobNotImplemented => "Это действие пока не реализовано.";
    public override string UiJobError => "Ошибка";
    public override string UiJobNothingToDo => "Ничего делать не нужно";
    public override string UiJobDone => "Готово";
    public override string UiJobCancelledOnExit => "Отменено при выходе из программы.";
    public override string UiJobCancelled => "Отменено";
    public override string UiJobCancelledDetails => "Отменено. Исходный файл не изменён, временные файлы удалены.";
    public override string UiJobSourceNotFound => "Исходный файл не найден: его переместили, переименовали или удалили.";
    public override string UiJobNoAccess => "Нет доступа к исходному файлу.";
    public override string UiJobFailed(string message) => $"Не удалось обработать файл: {message}";
    public override string UiJobNeedsPassword => "Нужен пароль";
    public override string UiJobCancelling => "Отмена…";

    public override string UiHistoryWindowTitle => "FileDial — история";
    public override string UiHistoryTitle => "История";
    public override string UiHistoryClear => "Очистить";
    public override string UiHistoryConfirmClear => "Точно очистить?";
    public override string UiHistoryEmpty => "Пока пусто";
    public override string UiHistoryDisabled => "Запись истории выключена в настройках";
    public override string UiHistorySummary(int count, int done) => $"Действий: {count}, успешно: {done}";
    public override string UiHistorySummaryDisabled => " · запись выключена в настройках";
    public override string UiHistoryEmptyHint =>
        "Здесь появятся действия, выполненные через круг: что сделано, с каким файлом и чем закончилось.";
    public override string UiHistoryEnableHint =>
        "Включите «Вести историю действий» в настройках, чтобы здесь появлялись выполненные действия.";
    public override string UiShowResult => "Показать результат";
    public override string UiResultGone => "Результат удалён или перемещён";
    public override string UiShowSource => "Показать исходник";

    public override string UiTrimWindowTitle => "FileDial — обрезать";
    public override string UiReadingFile => "Читаю файл…";
    public override string UiTrimStart => "Начало";
    public override string UiTrimEnd => "Конец";
    public override string UiTrimPrecise => "Точно по кадрам (дольше: видео перекодируется)";
    public override string UiTrimNeedsFfmpeg => "Нужен ffmpeg — установите его одной кнопкой: Трей → Настройки → ffmpeg.";
    public override string UiDurationUnknown => "Длительность неизвестна";
    public override string UiTrimNoDuration => "ffprobe не сообщил длительность — обрезать нельзя.";
    public override string UiDuration(string duration) => $"Длительность: {duration}";
    public override string UiTrimStartInvalid => "Начало: введите время, например 0:15 или 1:02:30.";
    public override string UiTrimEndInvalid => "Конец: введите время, например 0:45 или 1:05:00.";
    public override string UiTrimPrompt => "Укажите, какой отрезок оставить.";
    public override string UiSegmentLength(string length) => $"Длина отрезка: {length}";

    public override string UiPasswordWindowTitle => "FileDial — пароль";
    public override string UiPassword => "Пароль";
    public override string UiPasswordRepeat => "Ещё раз";
    public override string UiProtect => "Защитить";
    public override string UiOpen => "Открыть";
    public override string UiPasswordProtected(UiPasswordSubject subject) => subject switch
    {
        UiPasswordSubject.Pdf => "PDF защищён паролем",
        UiPasswordSubject.Document => "Документ защищён паролем",
        _ => "Архив защищён паролем",
    };
    public override string UiPasswordWarning(int minLength, UiPasswordSubject subject) =>
        $"Не короче {minLength} символов. Без пароля {UiSubjectWord(subject)} не открыть — восстановить его нельзя.";
    private static string UiSubjectWord(UiPasswordSubject subject) => subject switch
    {
        UiPasswordSubject.Pdf => "PDF",
        UiPasswordSubject.Document => "документ",
        _ => "архив",
    };
    public override string UiWrongPassword => "Неверный пароль — попробуйте ещё раз.";
    public override string UiPasswordTarUnsafe => "В пароле есть символы, которые встроенный tar.exe не передаёт.";
    public override string UiPasswordTooShort(int minLength) => $"Не короче {minLength} символов.";
    public override string UiPasswordRepeatPrompt => "Повторите пароль.";
    public override string UiPasswordsDiffer => "Пароли не совпадают.";
    public override string UiPasswordsMatch => "Пароли совпадают.";

    public override string UiSplitWindowTitle => "FileDial — разбить на части";
    public override string UiPartSize => "Размер части";
    public override string UiSplitHint =>
        "Части «имя.001, .002…» открываются в 7-Zip или WinRAR через первую часть, а собираются обратно в FileDial.";
    public override string UiSplit => "Разбить";
    public override string UiFileSmallerThanPart => "Файл меньше части — делить нечего";
    public override string UiFileTooSmallToSplit => "Файл меньше 10 МБ — делить его незачем.";
    public override string UiTooManyParts => "Получилось бы больше 999 частей — выберите размер побольше.";
    public override string UiWillProduce(string result) => $"Получится {result}.";

    public override string UiPagesWindowTitle => "FileDial — извлечь страницы";
    public override string UiPagesInDocument(string pages) => $"В документе {pages}";
    public override string UiPagesCountUnknown => "Число страниц станет известно после открытия";
    public override string UiPages => "Страницы";
    public override string UiPagesHint => "Например: 1-3, 5, 8- (до конца). Порядок сохранится как записан.";
    public override string UiExtract => "Извлечь";

    public override string UiContentsWindowTitle => "FileDial — содержимое архива";
    public override string UiArchiveContents => "Содержимое архива";
    public override string UiReadingArchive => "Читаю архив…";
    public override string UiArchiveEmpty => "Архив пуст.";
    public override string UiArchiveSummary(string files, string unpacked, string packed) =>
        $"{files} · {unpacked} после распаковки · архив {packed}";

    public override string UiTraySettings => "Настройки…";
    public override string UiTrayHistory => "История…";
    public override string UiTrayHelp => "Справка (README)";
    public override string UiTrayExit => "Выход";
    public override string UiTrayNoHotkey => "FileDial — клавиша не назначена";
    public override string UiTrayTipNoHotkey => "FileDial — назначьте горячую клавишу в настройках";
    public override string UiTrayTip(string hotkey) => $"FileDial — выделите файл и нажмите {hotkey}";

    public override string UiDialEnterAction(string action) => $"Enter — {action}";
    public override string UiDialChooseFormat => "Выберите формат снаружи или Enter";
    public override string UiDialEnterDownload(string reason) => $"{reason} — Enter: скачать";
    public override string UiDialEnterRun => "Enter — запустить";
    public override string UiDialEnterOrClick(string action) => $"Enter или клик — {action}";
    public override string UiDialEscClose => "Esc — закрыть";
    public override string UiDialKeys => "1–8, Enter · Esc";
    public override string UiDialNoActions => "Нет доступных действий";
}

public sealed partial class EnTexts
{
    public override string UiCancel => "Cancel";
    public override string UiClose => "Close";
    public override string UiSave => "Save";
    public override string UiBrowse => "Browse…";
    public override string UiReset => "Reset";
    public override string UiFfmpegNotFound => "ffmpeg not found";
    public override string UiProgressOf(string what, string done, string total) => $"{what}: {done} of {total}";

    public override string UiThemeGraphite => "Graphite";
    public override string UiThemeBrass => "Brass";
    public override string UiThemePaper => "Paper";

    public override string UiSettingsWindowTitle => "FileDial — settings";
    public override string UiSettingsTitle => "Settings";
    public override string UiHotkeyTitle => "Hotkey";
    public override string UiHotkeyHint => "Click the field and press a shortcut: Ctrl, Alt or Win with a letter, digit or F1–F24.";
    public override string UiHotkeyPress => "Press a shortcut…";
    public override string UiHotkeyEscHint => "Esc keeps the current one.";
    public override string UiHotkeyAfterSave => "The shortcut applies after you click Save.";
    public override string UiAppearanceTitle => "Appearance";
    public override string UiGeneralTitle => "General";
    public override string UiAutostart => "Start with Windows";
    public override string UiAnimations => "Animations";
    public override string UiKeepHistory => "Keep action history (stored only on this computer)";
    public override string UiOutputTitle => "Where to save results";
    public override string UiOutputNextToSource => "Next to the original file";
    public override string UiOutputToFolder => "In a chosen folder";
    public override string UiOpenFolderWhenDone => "Open the result folder when done";
    public override string UiVideoTargetTitle => "Video compression target";
    public override string UiImageTargetTitle => "Image compression target";
    public override string UiSpeechTitle => "Speech recognition (subtitles, text)";
    public override string UiOutputFolderDialog => "Folder for results";
    public override string UiChooseOutputFolder => "Choose a folder for results.";
    public override string UiOutputFolderMissing => "The folder for results was not found.";

    public override string UiFfmpegFolderDialog => "Folder with ffmpeg.exe and ffprobe.exe";
    public override string UiFfmpegAuto => "Auto (next to the app or in PATH)";
    public override string UiFfmpegNotInFolderUsing(string path) => $"No ffmpeg in the chosen folder; using {path}";
    public override string UiFfmpegNotFoundAnywhere => "The chosen folder has no ffmpeg.exe and ffprobe.exe, and they are not in PATH either.";
    public override string UiFfmpegFound(string path) => $"Found: {path}";
    public override string UiFfmpegMissing => "ffmpeg not found. Install it with the button below; you only need to do this once.";
    public override string UiFfmpegInstall => "Install ffmpeg (about 170 MB)";
    public override string UiFfmpegInstallWithSpeech => "Install ffmpeg with speech recognition";
    public override string UiFfmpegUpdate => "Update ffmpeg";
    public override string UiFfmpegInstallTip(string folder) =>
        "gyan.dev \"full\" build (with speech recognition), verified by checksum. Installs to " + folder;
    public override string UiFfmpegSearching => "Looking for the latest version…";
    public override string UiFfmpegInstalled(string path) => $"ffmpeg installed: {path}";
    public override string UiFfmpegInstallCancelled => "ffmpeg installation cancelled.";
    public override string UiFfmpegDownloadFailed => "Could not download ffmpeg: no internet connection, or GitHub is unavailable.";

    public override string UiWhisperModelDialog => "whisper.cpp model";
    public override string UiWhisperModelFilter => "Whisper model (ggml-*.bin)|*.bin|All files|*.*";
    public override string UiWhisperNotInstalled => "Not installed";
    public override string UiWhisperAuto(string path) => $"Auto: {path}";
    public override string UiWhisperFileMissing => "Model file not found.";
    public override string UiWhisperChosen(string name, string size) => $"Model: {name}, {size}. Works offline.";
    public override string UiWhisperUsing(string name, string size) => $"Using {name} ({size}). Works offline.";
    public override string UiWhisperMissing => "No model installed. Download one with the button below; you only need to do this once, then everything works offline.";
    public override string UiModelDownloaded(string label) => $"{label}: downloaded";
    public override string UiModelDownload(string label, string size) => $"Download: {label.ToLowerInvariant()} ({size})";
    public override string UiModelAccurateTip => "Noticeably more accurate for Russian, slower";
    public override string UiModelFastTip => "Fast, for simple speech";
    public override string UiConnecting(string fileName) => $"{fileName}: connecting…";
    public override string UiModelDownloadedVerified(string fileName) => $"Model {fileName} downloaded and verified.";
    public override string UiDownloadCancelled => "Download cancelled.";
    public override string UiDownloadFailed => "Could not download: no internet connection, or the server is unavailable.";

    public override string UiShowInFolder => "Show in folder";
    public override string UiInstallFfmpegLink => "Install ffmpeg with one click";
    public override string UiJobPreparing => "Preparing";
    public override string UiJobNeedsFfmpeg => "This action needs ffmpeg, which is not on this computer yet.";
    public override string UiJobNotImplemented => "This action is not implemented yet.";
    public override string UiJobError => "Error";
    public override string UiJobNothingToDo => "Nothing to do";
    public override string UiJobDone => "Done";
    public override string UiJobCancelledOnExit => "Cancelled when the app exited.";
    public override string UiJobCancelled => "Cancelled";
    public override string UiJobCancelledDetails => "Cancelled. The original file is unchanged; temporary files were deleted.";
    public override string UiJobSourceNotFound => "Original file not found: it was moved, renamed or deleted.";
    public override string UiJobNoAccess => "No access to the original file.";
    public override string UiJobFailed(string message) => $"Could not process the file: {message}";
    public override string UiJobNeedsPassword => "Password required";
    public override string UiJobCancelling => "Cancelling…";

    public override string UiHistoryWindowTitle => "FileDial — history";
    public override string UiHistoryTitle => "History";
    public override string UiHistoryClear => "Clear";
    public override string UiHistoryConfirmClear => "Really clear?";
    public override string UiHistoryEmpty => "Nothing here yet";
    public override string UiHistoryDisabled => "History is turned off in settings";
    public override string UiHistorySummary(int count, int done) => $"Actions: {count}, succeeded: {done}";
    public override string UiHistorySummaryDisabled => " · recording is off in settings";
    public override string UiHistoryEmptyHint =>
        "Actions you run from the dial will appear here: what was done, to which file, and how it ended.";
    public override string UiHistoryEnableHint =>
        "Turn on \"Keep action history\" in settings to see completed actions here.";
    public override string UiShowResult => "Show result";
    public override string UiResultGone => "The result was deleted or moved";
    public override string UiShowSource => "Show original";

    public override string UiTrimWindowTitle => "FileDial — trim";
    public override string UiReadingFile => "Reading the file…";
    public override string UiTrimStart => "Start";
    public override string UiTrimEnd => "End";
    public override string UiTrimPrecise => "Frame-accurate (slower: the video is re-encoded)";
    public override string UiTrimNeedsFfmpeg => "ffmpeg is required. Install it with one click: tray → Settings → ffmpeg.";
    public override string UiDurationUnknown => "Duration unknown";
    public override string UiTrimNoDuration => "ffprobe did not report the duration, so the file cannot be trimmed.";
    public override string UiDuration(string duration) => $"Duration: {duration}";
    public override string UiTrimStartInvalid => "Start: enter a time, for example 0:15 or 1:02:30.";
    public override string UiTrimEndInvalid => "End: enter a time, for example 0:45 or 1:05:00.";
    public override string UiTrimPrompt => "Choose which part to keep.";
    public override string UiSegmentLength(string length) => $"Segment length: {length}";

    public override string UiPasswordWindowTitle => "FileDial — password";
    public override string UiPassword => "Password";
    public override string UiPasswordRepeat => "Repeat";
    public override string UiProtect => "Protect";
    public override string UiOpen => "Open";
    public override string UiPasswordProtected(UiPasswordSubject subject) => $"This {UiSubjectWord(subject)} is password-protected";
    public override string UiPasswordWarning(int minLength, UiPasswordSubject subject) =>
        $"At least {minLength} characters. Without the password the {UiSubjectWord(subject)} cannot be opened, and the password cannot be recovered.";
    private static string UiSubjectWord(UiPasswordSubject subject) => subject switch
    {
        UiPasswordSubject.Pdf => "PDF",
        UiPasswordSubject.Document => "document",
        _ => "archive",
    };
    public override string UiWrongPassword => "Wrong password. Try again.";
    public override string UiPasswordTarUnsafe => "The password contains characters that the built-in tar.exe cannot pass on.";
    public override string UiPasswordTooShort(int minLength) => $"At least {minLength} characters.";
    public override string UiPasswordRepeatPrompt => "Repeat the password.";
    public override string UiPasswordsDiffer => "The passwords do not match.";
    public override string UiPasswordsMatch => "The passwords match.";

    public override string UiSplitWindowTitle => "FileDial — split into parts";
    public override string UiPartSize => "Part size";
    public override string UiSplitHint =>
        "Parts (name.001, .002…) open in 7-Zip or WinRAR from the first part, and FileDial joins them back.";
    public override string UiSplit => "Split";
    public override string UiFileSmallerThanPart => "The file is smaller than a part; nothing to split";
    public override string UiFileTooSmallToSplit => "The file is under 10 MB; no need to split it.";
    public override string UiTooManyParts => "That would make more than 999 parts. Choose a larger size.";
    public override string UiWillProduce(string result) => $"Result: {result}.";

    public override string UiPagesWindowTitle => "FileDial — extract pages";
    public override string UiPagesInDocument(string pages) => $"The document has {pages}";
    public override string UiPagesCountUnknown => "The page count will be known after opening";
    public override string UiPages => "Pages";
    public override string UiPagesHint => "For example: 1-3, 5, 8- (to the end). Pages keep the order you write.";
    public override string UiExtract => "Extract";

    public override string UiContentsWindowTitle => "FileDial — archive contents";
    public override string UiArchiveContents => "Archive contents";
    public override string UiReadingArchive => "Reading the archive…";
    public override string UiArchiveEmpty => "The archive is empty.";
    public override string UiArchiveSummary(string files, string unpacked, string packed) =>
        $"{files} · {unpacked} unpacked · archive {packed}";

    public override string UiTraySettings => "Settings…";
    public override string UiTrayHistory => "History…";
    public override string UiTrayHelp => "Help (README)";
    public override string UiTrayExit => "Exit";
    public override string UiTrayNoHotkey => "FileDial — no hotkey set";
    public override string UiTrayTipNoHotkey => "FileDial — set a hotkey in settings";
    public override string UiTrayTip(string hotkey) => $"FileDial — select a file and press {hotkey}";

    public override string UiDialEnterAction(string action) => $"Enter — {action}";
    public override string UiDialChooseFormat => "Pick a format outside the dial or press Enter";
    public override string UiDialEnterDownload(string reason) => $"{reason} — Enter: download";
    public override string UiDialEnterRun => "Enter — run";
    public override string UiDialEnterOrClick(string action) => $"Enter or click — {action}";
    public override string UiDialEscClose => "Esc — close";
    public override string UiDialKeys => "1–8, Enter · Esc";
    public override string UiDialNoActions => "No actions available";
}
