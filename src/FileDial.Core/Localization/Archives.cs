namespace FileDial.Core.Localization;

// Архивы: распаковка, проверка, сжатие, пароль, деление на части и сборка.

public abstract partial class Texts
{
    // Пароль и tar.exe
    public abstract string ArchiveLocked { get; }
    public abstract string ArchiveWrongPassword { get; }
    public abstract string ArchiveTarBadPath(string path);
    /// <summary>Без Trim: подробность может быть пустой — обрезает вызывающий.</summary>
    public abstract string ArchiveExtractError(string detail);
    public abstract string ArchiveNoTar { get; }
    public abstract string ArchiveTarEncryption { get; }
    public abstract string ArchivePasswordNotAnsi { get; }
    public abstract string ArchiveOutsideFolder(string entry);
    public abstract string ArchiveLockedOrUnsupported { get; }

    // Задачи
    public abstract string ArchiveStageExtracting { get; }
    public abstract string ArchiveDone { get; }
    public abstract string ArchiveEmptyExtract { get; }
    public abstract string ArchiveExtracted(int files, string name);
    public abstract string ArchiveStageTesting { get; }
    public abstract string ArchiveTestFailed { get; }
    public abstract string ArchiveTestNeedsTarPassword { get; }
    public abstract string ArchiveTestNeedsTar { get; }
    public abstract string ArchiveTestOk(int files);
    public abstract string ArchiveDamaged { get; }
    public abstract string ArchiveCreated(int files, string size, string name);
    /// <summary>Имя архива, когда взять его не у чего.</summary>
    public abstract string ArchiveDefaultName { get; }
    public abstract string ArchiveStageCompressingFile(string name);
    public abstract string ArchiveCannotRead(string name);
    public abstract string ArchiveStageCompressing7z { get; }
    public abstract string ArchiveName7zNotAnsi(string name);
    public abstract string Archive7zFailed { get; }
    public abstract string ArchiveEmptyRepack { get; }
    public abstract string ArchiveRepacked(string before, string after, string name);
    /// <summary>Часть имени файла: «документы_пароль.zip».</summary>
    public abstract string ArchivePasswordSuffix { get; }
    public abstract string ArchiveEmptyProtect { get; }
    public abstract string ArchiveStageEncrypting { get; }
    public abstract string ArchiveStageEncryptingFile(string name);
    public abstract string ArchiveProtected(string name, int files);
    public abstract string ArchiveEmptyPassword { get; }
    public abstract string ArchiveFileTooBig(string name);

    // Содержимое
    public abstract string ArchiveListNeedsTar { get; }
    public abstract string ArchiveListFailed { get; }
    public abstract string ArchiveListDamaged { get; }

    // Части
    public abstract string ArchiveParts(long n);
    public abstract string ArchiveNoFirstPart { get; }
    public abstract string ArchiveOnlyOnePart { get; }
    public abstract string ArchivePartWrongSize(string name);
    public abstract string ArchiveLastPartBigger { get; }
    public abstract string ArchiveSplitNothing(string size);
    public abstract string ArchiveTooManyParts { get; }
    public abstract string ArchiveFileChanged { get; }
    public abstract string ArchiveStagePart(long index, long count);
    public abstract string ArchiveSplitDone(long count, string partSize, string firstName);
    public abstract string ArchiveNotAPart { get; }
    public abstract string ArchiveJoined(int parts, string name, string size);
}

public sealed partial class RuTexts
{
    public override string ArchiveLocked => "Архив защищён паролем.";
    public override string ArchiveWrongPassword => "Неверный пароль.";
    public override string ArchiveTarBadPath(string path) =>
        $"В пути «{path}» есть символы, которые встроенный в Windows tar.exe не понимает. " +
        "Переименуйте папку (только русские и латинские буквы) или используйте ZIP.";
    public override string ArchiveExtractError(string detail) =>
        $"Не удалось распаковать: архив повреждён, защищён паролем или формат не поддерживается. {detail}";
    public override string ArchiveNoTar => "В Windows нет tar.exe — такие архивы не поддерживаются (нужна Windows 10 1803 или новее).";
    public override string ArchiveTarEncryption =>
        "Архив зашифрован способом, который встроенный в Windows tar.exe не поддерживает (зашифрованные 7z и RAR). " +
        "Откройте его в 7-Zip или WinRAR.";
    public override string ArchivePasswordNotAnsi => "В пароле есть символы, которые встроенный tar.exe не передаёт. Откройте архив в 7-Zip.";
    public override string ArchiveOutsideFolder(string entry) => $"Архив пытается записать файл за пределы папки: {entry}";
    public override string ArchiveLockedOrUnsupported => "Архив защищён паролем или сжат неподдерживаемым способом.";

    public override string ArchiveStageExtracting => "Распаковка";
    public override string ArchiveDone => "Готово";
    public override string ArchiveEmptyExtract => "Архив пуст — распаковывать нечего.";
    public override string ArchiveExtracted(int files, string name) => $"Распаковано: {Files(files)} → {name}.";
    public override string ArchiveStageTesting => "Проверка";
    public override string ArchiveTestFailed => "Архив повреждён или защищён паролем — прочитать его целиком не удалось.";
    public override string ArchiveTestNeedsTarPassword => "Для проверки архива с паролем нужен tar.exe (Windows 10 1803 или новее).";
    public override string ArchiveTestNeedsTar => "Для проверки такого архива нужен tar.exe (Windows 10 1803 или новее).";
    public override string ArchiveTestOk(int files) => $"Архив в порядке: {Files(files)}, ошибок нет.";
    public override string ArchiveDamaged => "Архив повреждён.";
    public override string ArchiveCreated(int files, string size, string name) => $"{Files(files)} → {size}. Сохранено как {name}.";
    public override string ArchiveDefaultName => "Архив";
    public override string ArchiveStageCompressingFile(string name) => $"Сжатие · {name}";
    public override string ArchiveCannotRead(string name) => $"Не удалось прочитать {name}: файл занят или нет доступа.";
    public override string ArchiveStageCompressing7z => "Сжатие в 7z";
    public override string ArchiveName7zNotAnsi(string name) =>
        $"В имени «{name}» есть символы, которые встроенный tar.exe не передаёт в 7z. Сожмите в ZIP — там такой проблемы нет.";
    public override string Archive7zFailed => "Не удалось создать архив 7z.";
    public override string ArchiveEmptyRepack => "Архив пуст — перепаковывать нечего.";
    public override string ArchiveRepacked(string before, string after, string name) => $"{before} → {after}. Сохранено как {name}.";
    public override string ArchivePasswordSuffix => "_пароль";
    public override string ArchiveEmptyProtect => "Архив пуст — защищать нечего.";
    public override string ArchiveStageEncrypting => "Шифрование";
    public override string ArchiveStageEncryptingFile(string name) => $"Шифрование · {name}";
    public override string ArchiveProtected(string name, int files) =>
        $"Создан архив с паролем: {name} ({Files(files)}, AES-256). " +
        "Имена файлов в ZIP видны без пароля — так устроен формат.";
    public override string ArchiveEmptyPassword => "Пустой пароль.";
    public override string ArchiveFileTooBig(string name) => $"Файл {name} больше 4 ГБ — для архива с паролем это пока не поддерживается.";

    public override string ArchiveListNeedsTar => "Для просмотра такого архива нужен tar.exe (Windows 10 1803 или новее).";
    public override string ArchiveListFailed => "Не удалось прочитать список файлов: архив повреждён или защищён паролем.";
    public override string ArchiveListDamaged => "Не удалось прочитать список файлов: архив повреждён.";

    public override string ArchiveParts(long n) => N(n, "часть", "части", "частей");
    public override string ArchiveNoFirstPart => "Не найдена первая часть (.001).";
    public override string ArchiveOnlyOnePart => "Найдена только одна часть — остальные (.002 и дальше) должны лежать рядом.";
    public override string ArchivePartWrongSize(string name) => $"Часть {name} другого размера — похоже, она повреждена или из другого набора.";
    public override string ArchiveLastPartBigger => "Последняя часть больше первой — похоже, набор неполный или перепутан.";
    public override string ArchiveSplitNothing(string size) => $"Файл ({size}) не больше одной части — делить нечего.";
    public override string ArchiveTooManyParts => "Получилось бы больше 999 частей — выберите размер части побольше.";
    public override string ArchiveFileChanged => "Файл изменился во время деления.";
    public override string ArchiveStagePart(long index, long count) => $"Часть {index} из {count}";
    public override string ArchiveSplitDone(long count, string partSize, string firstName) =>
        $"{ArchiveParts(count)} по {partSize}: {firstName} … .{count:000}. " +
        "Открываются 7-Zip или WinRAR (через .001), собираются FileDial.";
    public override string ArchiveNotAPart => "Это не часть файла (.001, .002, …).";
    public override string ArchiveJoined(int parts, string name, string size) =>
        $"Собрано из {N(parts, "части", "частей", "частей")}: {name} ({size}).";
}

public sealed partial class EnTexts
{
    public override string ArchiveLocked => "The archive is password-protected.";
    public override string ArchiveWrongPassword => "Wrong password.";
    public override string ArchiveTarBadPath(string path) =>
        $"The path “{path}” has characters that the tar.exe built into Windows cannot handle. " +
        "Rename the folder using plain Latin letters, or use ZIP.";
    public override string ArchiveExtractError(string detail) =>
        $"Could not extract: the archive is damaged, password-protected, or in an unsupported format. {detail}";
    public override string ArchiveNoTar => "Windows has no tar.exe, so these archives are not supported (needs Windows 10 1803 or later).";
    public override string ArchiveTarEncryption =>
        "The archive uses encryption that the tar.exe built into Windows does not support (encrypted 7z and RAR). " +
        "Open it in 7-Zip or WinRAR.";
    public override string ArchivePasswordNotAnsi => "The password has characters that the built-in tar.exe cannot pass on. Open the archive in 7-Zip.";
    public override string ArchiveOutsideFolder(string entry) => $"The archive tries to write a file outside the folder: {entry}";
    public override string ArchiveLockedOrUnsupported => "The archive is password-protected or uses an unsupported compression method.";

    public override string ArchiveStageExtracting => "Extracting";
    public override string ArchiveDone => "Done";
    public override string ArchiveEmptyExtract => "The archive is empty, nothing to extract.";
    public override string ArchiveExtracted(int files, string name) => $"Extracted {Files(files)} → {name}.";
    public override string ArchiveStageTesting => "Testing";
    public override string ArchiveTestFailed => "The archive is damaged or password-protected and could not be read in full.";
    public override string ArchiveTestNeedsTarPassword => "Testing a password-protected archive needs tar.exe (Windows 10 1803 or later).";
    public override string ArchiveTestNeedsTar => "Testing this kind of archive needs tar.exe (Windows 10 1803 or later).";
    public override string ArchiveTestOk(int files) => $"The archive is OK: {Files(files)}, no errors.";
    public override string ArchiveDamaged => "The archive is damaged.";
    public override string ArchiveCreated(int files, string size, string name) => $"{Files(files)} → {size}. Saved as {name}.";
    public override string ArchiveDefaultName => "Archive";
    public override string ArchiveStageCompressingFile(string name) => $"Compressing · {name}";
    public override string ArchiveCannotRead(string name) => $"Could not read {name}: the file is in use or access is denied.";
    public override string ArchiveStageCompressing7z => "Compressing to 7z";
    public override string ArchiveName7zNotAnsi(string name) =>
        $"The name “{name}” has characters that the built-in tar.exe cannot pass to 7z. Compress to ZIP instead, it has no such problem.";
    public override string Archive7zFailed => "Could not create the 7z archive.";
    public override string ArchiveEmptyRepack => "The archive is empty, nothing to repack.";
    public override string ArchiveRepacked(string before, string after, string name) => $"{before} → {after}. Saved as {name}.";
    public override string ArchivePasswordSuffix => "_protected";
    public override string ArchiveEmptyProtect => "The archive is empty, nothing to protect.";
    public override string ArchiveStageEncrypting => "Encrypting";
    public override string ArchiveStageEncryptingFile(string name) => $"Encrypting · {name}";
    public override string ArchiveProtected(string name, int files) =>
        $"Created a password-protected archive: {name} ({Files(files)}, AES-256). " +
        "File names in a ZIP are visible without the password; that is how the format works.";
    public override string ArchiveEmptyPassword => "The password is empty.";
    public override string ArchiveFileTooBig(string name) => $"{name} is larger than 4 GB; password-protected archives do not support that yet.";

    public override string ArchiveListNeedsTar => "Viewing this kind of archive needs tar.exe (Windows 10 1803 or later).";
    public override string ArchiveListFailed => "Could not read the file list: the archive is damaged or password-protected.";
    public override string ArchiveListDamaged => "Could not read the file list: the archive is damaged.";

    public override string ArchiveParts(long n) => N(n, "part", "parts");
    public override string ArchiveNoFirstPart => "The first part (.001) was not found.";
    public override string ArchiveOnlyOnePart => "Only one part was found. The others (.002 and on) must be in the same folder.";
    public override string ArchivePartWrongSize(string name) => $"Part {name} has a different size; it may be damaged or from another set.";
    public override string ArchiveLastPartBigger => "The last part is larger than the first; the set may be incomplete or mixed up.";
    public override string ArchiveSplitNothing(string size) => $"The file ({size}) fits in one part, nothing to split.";
    public override string ArchiveTooManyParts => "That would make more than 999 parts. Choose a larger part size.";
    public override string ArchiveFileChanged => "The file changed while it was being split.";
    public override string ArchiveStagePart(long index, long count) => $"Part {index} of {count}";
    public override string ArchiveSplitDone(long count, string partSize, string firstName) =>
        $"{ArchiveParts(count)} of {partSize}: {firstName} … .{count:000}. " +
        "Open them in 7-Zip or WinRAR (via .001), or join them with FileDial.";
    public override string ArchiveNotAPart => "This is not a file part (.001, .002, …).";
    public override string ArchiveJoined(int parts, string name, string size) =>
        $"Joined from {N(parts, "part", "parts")}: {name} ({size}).";
}
