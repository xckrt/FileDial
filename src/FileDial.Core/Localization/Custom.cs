namespace FileDial.Core.Localization;

// Свои действия: группа «Ещё» в круге, запуск программы, проверка, раздел настроек и редактор.

public abstract partial class Texts
{
    // Круг
    public abstract string CstMore { get; }
    public abstract string CstChooseAction { get; }

    // Запуск
    public abstract string CstStageRunning(string program);
    public abstract string CstStarted(string program);
    public abstract string CstFinished(string program);
    public abstract string CstExitCode(int code);
    public abstract string CstNoOutput { get; }
    public abstract string CstNeedsFfmpeg { get; }
    public abstract string CstCannotStart(string reason);
    public abstract string CstProgramNotFound(string program);

    // Проверка
    public abstract string CstNameRequired { get; }
    public abstract string CstNameTooLong(int max);
    public abstract string CstChooseAppliesTo { get; }
    public abstract string CstExtensionsRequired { get; }
    public abstract string CstProgramRequired { get; }
    public abstract string CstProgramNotExe { get; }
    public abstract string CstBadOutputName { get; }
    public abstract string CstUnknownVariable(string variable);
    public abstract string CstUnclosedQuote { get; }

    // Раздел настроек
    public abstract string CstSectionTitle { get; }
    public abstract string CstSectionHint { get; }
    public abstract string CstAdd { get; }
    public abstract string CstEdit { get; }
    public abstract string CstDelete { get; }
    public abstract string CstEmpty { get; }

    // Редактор
    public abstract string CstWindowTitle { get; }
    public abstract string CstNewTitle { get; }
    public abstract string CstEditTitle { get; }
    public abstract string CstRightsNote { get; }
    public abstract string CstName { get; }
    public abstract string CstAppliesTo { get; }
    public abstract string CstKindAny { get; }
    public abstract string CstKindVideo { get; }
    public abstract string CstKindImage { get; }
    public abstract string CstKindAudio { get; }
    public abstract string CstKindPdf { get; }
    public abstract string CstKindDocument { get; }
    public abstract string CstKindArchive { get; }
    public abstract string CstKindExtensions { get; }
    public abstract string CstExtensionsHint { get; }
    public abstract string CstProgram { get; }
    public abstract string CstProgramHint { get; }
    public abstract string CstProgramFilter { get; }
    public abstract string CstArguments { get; }
    public abstract string CstVariablesHint { get; }
    public abstract string CstOutputTitle { get; }
    public abstract string CstSuffix { get; }
    public abstract string CstExtension { get; }
    public abstract string CstOutputHint { get; }
    public abstract string CstShowWindow { get; }
    public abstract string CstShowWindowHint { get; }
    public abstract string CstPreview { get; }
    public abstract string CstExamples { get; }
    public abstract string CstSampleName { get; }

    // Примеры
    public abstract string CstExampleFfmpeg { get; }
    public abstract string CstExampleFfmpegName { get; }
    public abstract string CstExampleNotepad { get; }
    public abstract string CstExampleNotepadName { get; }
    public abstract string CstExampleMagick { get; }
    public abstract string CstExampleMagickName { get; }
    public abstract string CstMagickNote { get; }
}

public sealed partial class RuTexts
{
    public override string CstMore => "Ещё";
    public override string CstChooseAction => "Выберите действие снаружи или Enter";

    public override string CstStageRunning(string program) => $"Работает {program}";
    public override string CstStarted(string program) => $"{program} запущена в своём окне.";
    public override string CstFinished(string program) => $"{program} отработала без ошибок.";
    public override string CstExitCode(int code) => $"Программа завершилась с ошибкой (код {code}).";
    public override string CstNoOutput => "Программа завершилась, но файл результата так и не появился.";
    public override string CstNeedsFfmpeg => "Этому действию нужен ffmpeg — установите его в настройках.";
    public override string CstCannotStart(string reason) => $"Не удалось запустить программу: {reason}";
    public override string CstProgramNotFound(string program) => $"Программа не найдена: {program}";

    public override string CstNameRequired => "Введите название.";
    public override string CstNameTooLong(int max) => $"Название длиннее {max} символов — в круге его не прочитать.";
    public override string CstChooseAppliesTo => "Выберите, для каких файлов действие.";
    public override string CstExtensionsRequired => "Перечислите расширения, например: txt, md.";
    public override string CstProgramRequired => "Укажите программу.";
    public override string CstProgramNotExe =>
        "Запускаются только .exe. Скрипт можно запустить через его программу, например: " +
        "powershell.exe -File script.ps1 {file}.";
    public override string CstBadOutputName => "В добавке к имени и расширении нельзя использовать \\ / : * ? \" < > |.";
    public override string CstUnknownVariable(string variable) =>
        $"Неизвестная переменная {variable}. Есть: {{file}}, {{dir}}, {{name}}, {{ext}}, {{out}}, {{ffmpeg}}.";
    public override string CstUnclosedQuote => "Не закрыта кавычка в аргументах.";

    public override string CstSectionTitle => "Мои действия";
    public override string CstSectionHint =>
        "Свои команды в круге для выбранного файла. Если свободных секторов нет, последнее действие круга " +
        "превращается в группу «Ещё ›»: в её веере оно само и ваши действия (до восьми на тип файла).";
    public override string CstAdd => "Добавить…";
    public override string CstEdit => "Изменить";
    public override string CstDelete => "Удалить";
    public override string CstEmpty => "Пока ничего нет.";

    public override string CstWindowTitle => "Моё действие";
    public override string CstNewTitle => "Новое действие";
    public override string CstEditTitle => "Изменить действие";
    public override string CstRightsNote =>
        "Команда запускается с вашими правами и может делать всё, что можете вы. Добавляйте только то, чему доверяете.";
    public override string CstName => "Название";
    public override string CstAppliesTo => "Для каких файлов";
    public override string CstKindAny => "Любые";
    public override string CstKindVideo => "Видео";
    public override string CstKindImage => "Картинки";
    public override string CstKindAudio => "Звук";
    public override string CstKindPdf => "PDF";
    public override string CstKindDocument => "Документы";
    public override string CstKindArchive => "Архивы";
    public override string CstKindExtensions => "По расширению";
    public override string CstExtensionsHint => "Через запятую или пробел: txt, md, log";
    public override string CstProgram => "Программа";
    public override string CstProgramHint => "Путь к .exe, имя из PATH (notepad.exe) или {ffmpeg} — ffmpeg, который нашёл FileDial.";
    public override string CstProgramFilter => "Программы (*.exe)|*.exe";
    public override string CstArguments => "Аргументы";
    public override string CstVariablesHint =>
        "{file} — полный путь, {dir} — папка, {name} — имя без расширения, {ext} — расширение, " +
        "{out} — новый файл рядом с исходным, {ffmpeg} — путь к ffmpeg. Пути с пробелами передаются правильно " +
        "и без кавычек; кавычки нужны, только чтобы склеить свой текст с пробелами в один аргумент.";
    public override string CstOutputTitle => "Файл результата (для {out})";
    public override string CstSuffix => "Добавка к имени";
    public override string CstExtension => "Расширение";
    public override string CstOutputHint =>
        "Пустое расширение — как у исходного файла. Существующие файлы не перезаписываются: имя_2, имя_3…";
    public override string CstShowWindow => "Показывать окно программы";
    public override string CstShowWindowHint =>
        "Без {out} программа просто открывается, FileDial её не ждёт. С {out} — ждёт завершения.";
    public override string CstPreview => "Команда для примера";
    public override string CstExamples => "Примеры:";
    public override string CstSampleName => "Отпуск";

    public override string CstExampleFfmpeg => "ffmpeg: 720p";
    public override string CstExampleFfmpegName => "В 720p";
    public override string CstExampleNotepad => "Блокнот";
    public override string CstExampleNotepadName => "Открыть в Блокноте";
    public override string CstExampleMagick => "ImageMagick";
    public override string CstExampleMagickName => "Уменьшить вдвое";
    public override string CstMagickNote => "Нужен установленный ImageMagick (magick.exe в PATH).";
}

public sealed partial class EnTexts
{
    public override string CstMore => "More";
    public override string CstChooseAction => "Pick an action outside the dial or press Enter";

    public override string CstStageRunning(string program) => $"Running {program}";
    public override string CstStarted(string program) => $"{program} has been opened in its own window.";
    public override string CstFinished(string program) => $"{program} finished without errors.";
    public override string CstExitCode(int code) => $"The program finished with an error (code {code}).";
    public override string CstNoOutput => "The program finished, but the result file never appeared.";
    public override string CstNeedsFfmpeg => "This action needs ffmpeg. You can install it in Settings.";
    public override string CstCannotStart(string reason) => $"Could not start the program: {reason}";
    public override string CstProgramNotFound(string program) => $"Program not found: {program}";

    public override string CstNameRequired => "Enter a name.";
    public override string CstNameTooLong(int max) => $"The name is longer than {max} characters and won't fit in the dial.";
    public override string CstChooseAppliesTo => "Choose which files the action is for.";
    public override string CstExtensionsRequired => "List the extensions, for example: txt, md.";
    public override string CstProgramRequired => "Specify the program.";
    public override string CstProgramNotExe =>
        "Only .exe programs can be run. Run a script through its program instead, for example: " +
        "powershell.exe -File script.ps1 {file}.";
    public override string CstBadOutputName => "The suffix and extension can't contain \\ / : * ? \" < > |.";
    public override string CstUnknownVariable(string variable) =>
        $"Unknown variable {variable}. Available: {{file}}, {{dir}}, {{name}}, {{ext}}, {{out}}, {{ffmpeg}}.";
    public override string CstUnclosedQuote => "A quote in the arguments is not closed.";

    public override string CstSectionTitle => "My actions";
    public override string CstSectionHint =>
        "Your own commands in the dial for the selected file. If there are no free sectors, the dial's last action " +
        "turns into a \"More ›\" group whose fan holds that action and yours (up to eight per file type).";
    public override string CstAdd => "Add…";
    public override string CstEdit => "Edit";
    public override string CstDelete => "Delete";
    public override string CstEmpty => "Nothing here yet.";

    public override string CstWindowTitle => "My action";
    public override string CstNewTitle => "New action";
    public override string CstEditTitle => "Edit action";
    public override string CstRightsNote =>
        "The command runs with your user rights and can do anything you can. Only add commands you trust.";
    public override string CstName => "Name";
    public override string CstAppliesTo => "Applies to";
    public override string CstKindAny => "Any file";
    public override string CstKindVideo => "Video";
    public override string CstKindImage => "Images";
    public override string CstKindAudio => "Audio";
    public override string CstKindPdf => "PDF";
    public override string CstKindDocument => "Documents";
    public override string CstKindArchive => "Archives";
    public override string CstKindExtensions => "By extension";
    public override string CstExtensionsHint => "Separated by commas or spaces: txt, md, log";
    public override string CstProgram => "Program";
    public override string CstProgramHint => "Path to an .exe, a name on PATH (notepad.exe) or {ffmpeg}, the ffmpeg FileDial found.";
    public override string CstProgramFilter => "Programs (*.exe)|*.exe";
    public override string CstArguments => "Arguments";
    public override string CstVariablesHint =>
        "{file} is the full path, {dir} the folder, {name} the name without extension, {ext} the extension, " +
        "{out} a new file next to the original, {ffmpeg} the path to ffmpeg. Paths with spaces are passed correctly " +
        "without quotes; use quotes only to join your own text with spaces into one argument.";
    public override string CstOutputTitle => "Result file (for {out})";
    public override string CstSuffix => "Name suffix";
    public override string CstExtension => "Extension";
    public override string CstOutputHint =>
        "An empty extension keeps the original one. Existing files are never overwritten: name_2, name_3…";
    public override string CstShowWindow => "Show the program window";
    public override string CstShowWindowHint =>
        "Without {out} the program is just opened and FileDial doesn't wait for it. With {out} it waits until it finishes.";
    public override string CstPreview => "Command for a sample file";
    public override string CstExamples => "Examples:";
    public override string CstSampleName => "Holiday";

    public override string CstExampleFfmpeg => "ffmpeg: 720p";
    public override string CstExampleFfmpegName => "To 720p";
    public override string CstExampleNotepad => "Notepad";
    public override string CstExampleNotepadName => "Open in Notepad";
    public override string CstExampleMagick => "ImageMagick";
    public override string CstExampleMagickName => "Shrink by half";
    public override string CstMagickNote => "Requires ImageMagick to be installed (magick.exe on PATH).";
}
