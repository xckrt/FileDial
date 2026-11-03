namespace FileDial.Core.Localization;

// Круг: подписи секторов, причины неактивности, разбор выделения.

public abstract partial class Texts
{
    // Общие
    public abstract string NotInPrototype { get; }
    public abstract string Format { get; }
    public abstract string AlreadyIn(string format);
    /// <summary>Подпись формата в заголовке окна: «В WebP».</summary>
    public abstract string ConvertTo(string format);
    public abstract string AllAlreadyIn(string format);
    public abstract string AllSmallerThan(string target);
    public abstract string ToPdf { get; }
    public abstract string ToText { get; }
    public abstract string ProtectWithPassword { get; }
    public abstract string CombineIntoPdf { get; }

    // Видео
    public abstract string CompressTo(string target);
    public abstract string Trim { get; }
    public abstract string MakeGif { get; }
    public abstract string GifTooLong(int seconds);
    public abstract string ExtractAudio { get; }
    public abstract string NoSoundInVideo { get; }
    public abstract string FrameToImage { get; }
    public abstract string Subtitles { get; }
    public abstract string RemoveSound { get; }
    public abstract string AlreadySilent { get; }

    // Картинки
    public abstract string ResizeTo(int side);
    public abstract string AlreadyNoBigger(int side, int width, int height);
    public abstract string RotateRight { get; }
    public abstract string RotateLeft { get; }
    public abstract string Mirror { get; }
    public abstract string RemoveBackground { get; }

    // Звук
    public abstract string NormalizeVolume { get; }
    public abstract string RemoveNoise { get; }
    public abstract string SpeedUp { get; }
    public abstract string SlowDown { get; }
    public abstract string ToMono { get; }
    public abstract string AlreadyMono { get; }

    // Архивы
    public abstract string NeedsTar { get; }
    public abstract string ExtractHere { get; }
    public abstract string ExtractToFolder { get; }
    public abstract string ExtractTo { get; }
    public abstract string TestArchive { get; }
    public abstract string SplitIntoParts { get; }
    public abstract string Contents { get; }
    public abstract string TrimOneFile { get; }
    public abstract string ContentsOneArchive { get; }

    // PDF
    public abstract string SplitPages { get; }
    public abstract string SinglePage { get; }
    public abstract string ExtractPages { get; }
    public abstract string PagesToImages { get; }
    public abstract string RecognizeText { get; }
    public abstract string Merge { get; }
    public abstract string MergePdfs { get; }
    public abstract string SelectSeveralPdfs { get; }
    public abstract string PagesOneFile { get; }

    // Документы
    public abstract string SheetsUseCsv { get; }
    public abstract string PasswordOfficeOnly { get; }
    public abstract string ExtractImages { get; }
    public abstract string CompressImages { get; }
    public abstract string RemovePersonalData { get; }
    public abstract string SelectSeveralDocuments { get; }

    // Выделение
    public abstract string Unsupported { get; }
    public abstract string NothingSelected { get; }
    public abstract string NotRegularFile { get; }
    public abstract string FileBusy { get; }
    public abstract string FileUnavailable(string name);
    public abstract string SelectedCount(int n);
    public abstract string AtMostAtOnce(int n);
    public abstract string HasFolders { get; }
    public abstract string MixedTypes { get; }
    public abstract string JoinParts { get; }
    public abstract string CompressToZip { get; }
    public abstract string Videos(int n);
    public abstract string Images(int n);
    public abstract string AudioFiles(int n);
    public abstract string Pdfs(int n);
    public abstract string Documents(int n);
    public abstract string Files(int n);
}

public sealed partial class RuTexts
{
    public override string NotInPrototype => "Недоступно в прототипе";
    public override string Format => "Формат";
    public override string AlreadyIn(string format) => $"Файл уже в {format}";
    public override string ConvertTo(string format) => $"В {format}";
    public override string AllAlreadyIn(string format) => $"Все файлы уже в {format}";
    public override string AllSmallerThan(string target) => $"Все файлы уже меньше {target}";
    public override string ToPdf => "В PDF";
    public override string ToText => "В текст";
    public override string ProtectWithPassword => "Защитить паролем";
    public override string CombineIntoPdf => "Собрать в PDF";

    public override string CompressTo(string target) => $"Сжать до {target}";
    public override string Trim => "Обрезать";
    public override string MakeGif => "Сделать GIF";
    public override string GifTooLong(int seconds) => $"GIF — только для роликов до {seconds} с";
    public override string ExtractAudio => "Извлечь звук";
    public override string NoSoundInVideo => "В видео нет звука";
    public override string FrameToImage => "Кадр в картинку";
    public override string Subtitles => "Субтитры";
    public override string RemoveSound => "Убрать звук";
    public override string AlreadySilent => "В видео и так нет звука";

    public override string ResizeTo(int side) => $"Уменьшить до {side} px";
    public override string AlreadyNoBigger(int side, int width, int height) => $"Уже не больше {side} px ({width}×{height})";
    public override string RotateRight => "Повернуть вправо";
    public override string RotateLeft => "Повернуть влево";
    public override string Mirror => "Отразить";
    public override string RemoveBackground => "Убрать фон";

    public override string NormalizeVolume => "Выровнять громкость";
    public override string RemoveNoise => "Убрать шум";
    public override string SpeedUp => "Ускорить ×1,5";
    public override string SlowDown => "Замедлить ×0,75";
    public override string ToMono => "В моно";
    public override string AlreadyMono => "Звук уже моно";

    public override string NeedsTar => "Нужен tar.exe из Windows 10 1803 или новее";
    public override string ExtractHere => "Распаковать сюда";
    public override string ExtractToFolder => "Распаковать в папку";
    public override string ExtractTo => "Распаковать в…";
    public override string TestArchive => "Проверить архив";
    public override string SplitIntoParts => "Разбить на части";
    public override string Contents => "Содержимое";
    public override string TrimOneFile => "Обрезка — по одному файлу";
    public override string ContentsOneArchive => "Содержимое — по одному архиву";

    public override string SplitPages => "Разделить по страницам";
    public override string SinglePage => "В документе одна страница";
    public override string ExtractPages => "Извлечь страницы";
    public override string PagesToImages => "Страницы в картинки";
    public override string RecognizeText => "Распознать текст";
    public override string Merge => "Объединить";
    public override string MergePdfs => "Объединить PDF";
    public override string SelectSeveralPdfs => "Выделите несколько PDF";
    public override string PagesOneFile => "Страницы — по одному файлу";

    public override string SheetsUseCsv => "Для таблиц — «Формат» → CSV";
    public override string PasswordOfficeOnly => "Пароль — для форматов Microsoft Office";
    public override string ExtractImages => "Извлечь картинки";
    public override string CompressImages => "Сжать картинки";
    public override string RemovePersonalData => "Убрать личные данные";
    public override string SelectSeveralDocuments => "Выделите несколько документов";

    public override string Unsupported => "Пока не поддерживается";
    public override string NothingSelected => "Ничего не выделено";
    public override string NotRegularFile => "Файл недоступен: это не обычный файл на диске";
    public override string FileBusy => "Файл недоступен: нет доступа или он занят";
    public override string FileUnavailable(string name) => $"Файл недоступен: {name}";
    public override string SelectedCount(int n) => $"Выделено {N(n, "объект", "объекта", "объектов")}";
    public override string AtMostAtOnce(int n) => $"Не больше {n} за раз";
    public override string HasFolders => "Среди выделенного есть папки";
    public override string MixedTypes => "Файлы разных типов";
    public override string JoinParts => "Собрать из частей";
    public override string CompressToZip => "Сжать в ZIP";
    public override string Videos(int n) => $"{n} видео";
    public override string Images(int n) => N(n, "картинка", "картинки", "картинок");
    public override string AudioFiles(int n) => N(n, "аудиофайл", "аудиофайла", "аудиофайлов");
    public override string Pdfs(int n) => $"{n} PDF";
    public override string Documents(int n) => N(n, "документ", "документа", "документов");
    public override string Files(int n) => N(n, "файл", "файла", "файлов");
}

public sealed partial class EnTexts
{
    public override string NotInPrototype => "Not available in the prototype";
    public override string Format => "Format";
    public override string AlreadyIn(string format) => $"Already {format}";
    public override string ConvertTo(string format) => $"To {format}";
    public override string AllAlreadyIn(string format) => $"All files are already {format}";
    public override string AllSmallerThan(string target) => $"All files are already under {target}";
    public override string ToPdf => "To PDF";
    public override string ToText => "To text";
    public override string ProtectWithPassword => "Password-protect";
    public override string CombineIntoPdf => "Combine into PDF";

    public override string CompressTo(string target) => $"Compress to {target}";
    public override string Trim => "Trim";
    public override string MakeGif => "Make GIF";
    public override string GifTooLong(int seconds) => $"GIF is for clips up to {seconds} s";
    public override string ExtractAudio => "Extract audio";
    public override string NoSoundInVideo => "The video has no sound";
    public override string FrameToImage => "Frame to image";
    public override string Subtitles => "Subtitles";
    public override string RemoveSound => "Remove sound";
    public override string AlreadySilent => "The video is already silent";

    public override string ResizeTo(int side) => $"Resize to {side} px";
    public override string AlreadyNoBigger(int side, int width, int height) => $"Already {side} px or smaller ({width}×{height})";
    public override string RotateRight => "Rotate right";
    public override string RotateLeft => "Rotate left";
    public override string Mirror => "Mirror";
    public override string RemoveBackground => "Remove background";

    public override string NormalizeVolume => "Normalize volume";
    public override string RemoveNoise => "Remove noise";
    public override string SpeedUp => "Speed up ×1.5";
    public override string SlowDown => "Slow down ×0.75";
    public override string ToMono => "To mono";
    public override string AlreadyMono => "The audio is already mono";

    public override string NeedsTar => "Needs tar.exe from Windows 10 1803 or later";
    public override string ExtractHere => "Extract here";
    public override string ExtractToFolder => "Extract to folder";
    public override string ExtractTo => "Extract to…";
    public override string TestArchive => "Test archive";
    public override string SplitIntoParts => "Split into parts";
    public override string Contents => "Contents";
    public override string TrimOneFile => "Trim works on one file at a time";
    public override string ContentsOneArchive => "Contents: one archive at a time";

    public override string SplitPages => "Split into pages";
    public override string SinglePage => "The document has one page";
    public override string ExtractPages => "Extract pages";
    public override string PagesToImages => "Pages to images";
    public override string RecognizeText => "Recognize text";
    public override string Merge => "Merge";
    public override string MergePdfs => "Merge PDFs";
    public override string SelectSeveralPdfs => "Select several PDFs";
    public override string PagesOneFile => "Pages: one file at a time";

    public override string SheetsUseCsv => "For spreadsheets use Format → CSV";
    public override string PasswordOfficeOnly => "Passwords are for Microsoft Office formats";
    public override string ExtractImages => "Extract images";
    public override string CompressImages => "Compress images";
    public override string RemovePersonalData => "Remove personal data";
    public override string SelectSeveralDocuments => "Select several documents";

    public override string Unsupported => "Not supported yet";
    public override string NothingSelected => "Nothing is selected";
    public override string NotRegularFile => "File unavailable: not a regular file on disk";
    public override string FileBusy => "File unavailable: no access, or it is in use";
    public override string FileUnavailable(string name) => $"File unavailable: {name}";
    public override string SelectedCount(int n) => $"{N(n, "item", "items")} selected";
    public override string AtMostAtOnce(int n) => $"No more than {n} at a time";
    public override string HasFolders => "The selection contains folders";
    public override string MixedTypes => "Files of different types";
    public override string JoinParts => "Join parts";
    public override string CompressToZip => "Compress to ZIP";
    public override string Videos(int n) => N(n, "video", "videos");
    public override string Images(int n) => N(n, "image", "images");
    public override string AudioFiles(int n) => N(n, "audio file", "audio files");
    public override string Pdfs(int n) => N(n, "PDF", "PDFs");
    public override string Documents(int n) => N(n, "document", "documents");
    public override string Files(int n) => N(n, "file", "files");
}
