namespace FileDial.Core.Localization;

// Задачи над файлами: сжатие видео и картинок, преобразования ffmpeg, обрезка, речь, загрузки, история,
// горячая клавиша. Добавки к именам файлов-результатов — тоже здесь (JobSuffix…).

public abstract partial class Texts
{
    // Этапы
    public abstract string JobStageAnalyzing { get; }
    public abstract string JobStageProcessing { get; }
    public abstract string JobStagePreparing { get; }
    public abstract string JobStageDone { get; }
    public abstract string JobStagePass(int pass);
    public abstract string JobStageRefiningPass { get; }
    public abstract string JobStageTuningQuality { get; }
    public abstract string JobStageDownscaling(int side);
    public abstract string JobStagePage(int number, int total, string name);
    public abstract string JobStageBuildingPdf { get; }
    public abstract string JobStageDownloadingFfmpeg(string version);
    /// <summary>Распаковка ffmpeg — окно настроек показывает её без процентов.</summary>
    public abstract string JobStageUnpacking { get; }

    // Сжатие и проверки
    public abstract string JobAlreadySmallerThan(string target);
    public abstract string JobNoVideoTrack { get; }
    public abstract string JobNoAudioTrack { get; }
    public abstract string JobVideoTooLongFor(int minutes, string target);
    public abstract string JobVideoTooLong(string target, int videoKbps, int minKbps);
    public abstract string JobNoWriteAccess { get; }
    public abstract string JobLowDiskSpace(string free, string required);
    public abstract string JobNoFreeName { get; }
    public abstract string JobAlreadySmallNoNeed(string target, string size);
    public abstract string JobSlightlyOverTarget { get; }
    public abstract string JobCannotFitImage(string target);
    public abstract string JobCannotReadImage { get; }
    public abstract string JobCannotReadImageNamed(string name);

    // ffmpeg и ffprobe
    public abstract string JobFfprobeCannotRead { get; }
    public abstract string JobFfprobeBadOutput { get; }
    public abstract string JobFfprobeNoDuration { get; }
    public abstract string JobFfmpegPassFailed(int pass, int code);
    public abstract string JobFfmpegFailed(int code);
    public abstract string JobFfmpegFailedOn(string name);
    public abstract string JobFfmpegNoOutput { get; }
    public abstract string JobFfmpegArchiveNotFound { get; }
    public abstract string JobArchiveLacks(string exe);
    public abstract string JobFfmpegTarFailed(int code);

    // Итоги
    public abstract string JobSavedAs(string name);
    public abstract string JobBatchDone(int done, int total);
    public abstract string JobBatchSkipped(int skipped);
    public abstract string JobBatchFailed(int failed, string name, string message);
    /// <summary>«3 из 10» — номер файла в пакете.</summary>
    public abstract string JobItemOf(int number, int total);
    public abstract string JobFileNotFound { get; }
    public abstract string JobNoFileAccess { get; }
    public abstract string JobAlreadyInFormat(string format);

    // Добавки к имени результата и имена файлов
    public abstract string JobSuffixMuted { get; }
    public abstract string JobSuffixFrame { get; }
    public abstract string JobSuffixRotated { get; }
    public abstract string JobSuffixRotatedLeft { get; }
    public abstract string JobSuffixMirrored { get; }
    public abstract string JobSuffixNormalized { get; }
    public abstract string JobSuffixSpedUp { get; }
    public abstract string JobSuffixSlowedDown { get; }
    public abstract string JobSuffixDenoised { get; }
    public abstract string JobSuffixMono { get; }
    public abstract string JobSuffixTrimmed { get; }
    /// <summary>Имя PDF из картинок, если у папки нет имени (корень диска).</summary>
    public abstract string JobDefaultPdfName { get; }

    // Речь
    public abstract string JobSpeechAuto { get; }
    public abstract string JobNeedsSpeechModel { get; }
    public abstract string JobWhisperModelMissing { get; }
    public abstract string JobFfmpegWithoutWhisper { get; }
    public abstract string JobNoSpeechFound { get; }
    public abstract string JobModelBase { get; }
    public abstract string JobModelAccurate { get; }

    // Обрезка
    public abstract string JobTrimNegativeStart { get; }
    public abstract string JobTrimEndBeforeStart { get; }
    public abstract string JobTrimTooShort { get; }
    public abstract string JobTrimEndPastFile(string duration);
    public abstract string JobTrimWholeFile { get; }

    // Загрузки
    public abstract string JobDownloadNoSpace(long megabytes);
    public abstract string JobDownloadCorrupted { get; }
    public abstract string JobDownloadStalled { get; }

    // Горячая клавиша
    public abstract string JobHotkeyUseKey { get; }
    public abstract string JobHotkeyAddModifier { get; }

    // История
    public abstract string JobToday { get; }
    public abstract string JobYesterday { get; }
    /// <summary>«28 октября», «28 октября 2025».</summary>
    public abstract string JobDayLabel(DateTime day, bool withYear);
    public abstract string JobStatusDone { get; }
    public abstract string JobStatusNothingToDo { get; }
    public abstract string JobStatusCancelled { get; }
    public abstract string JobStatusFailed { get; }
}

public sealed partial class RuTexts
{
    public override string JobStageAnalyzing => "Анализ файла";
    public override string JobStageProcessing => "Обработка";
    public override string JobStagePreparing => "Подготовка";
    public override string JobStageDone => "Готово";
    public override string JobStagePass(int pass) => $"Проход {pass} из 2";
    public override string JobStageRefiningPass => "Проход 2 из 2, уточнение";
    public override string JobStageTuningQuality => "Подбор качества";
    public override string JobStageDownscaling(int side) => $"Уменьшение до {side} px";
    public override string JobStagePage(int number, int total, string name) => $"Страница {number} из {total} · {name}";
    public override string JobStageBuildingPdf => "Сборка PDF";
    public override string JobStageDownloadingFfmpeg(string version) => $"Скачивание ffmpeg {version}";
    public override string JobStageUnpacking => "Распаковка";

    public override string JobAlreadySmallerThan(string target) => $"Файл уже меньше {target}";
    public override string JobNoVideoTrack => "В файле нет видеодорожки";
    public override string JobNoAudioTrack => "В файле нет звуковой дорожки.";
    public override string JobVideoTooLongFor(int minutes, string target) => $"Видео длиннее {minutes} мин в {target} не уместить";
    public override string JobVideoTooLong(string target, int videoKbps, int minKbps) =>
        $"Видео слишком длинное: в {target} на изображение остаётся {videoKbps} кбит/с, это меньше разумного минимума {minKbps} кбит/с.";
    public override string JobNoWriteAccess =>
        "Нет прав на запись в папку с файлом. Результат сохраняется рядом с исходником — " +
        "скопируйте видео в папку, где можно создавать файлы.";
    public override string JobLowDiskSpace(string free, string required) =>
        $"На диске мало места: свободно {free}, нужно не меньше {required}.";
    public override string JobNoFreeName => "Не удалось подобрать свободное имя для результата.";
    public override string JobAlreadySmallNoNeed(string target, string size) => $"Файл уже меньше {target} ({size}), сжимать не нужно.";
    public override string JobSlightlyOverTarget => "Чуть больше цели: кодер не попал точно.";
    public override string JobCannotFitImage(string target) => $"Не удалось уложить картинку в {target}.";
    public override string JobCannotReadImage => "Не удалось прочитать изображение.";
    public override string JobCannotReadImageNamed(string name) => $"Не удалось прочитать изображение {name}.";

    public override string JobFfprobeCannotRead => "ffprobe не смог прочитать файл. Возможно, он повреждён.";
    public override string JobFfprobeBadOutput => "Не удалось разобрать ответ ffprobe.";
    public override string JobFfprobeNoDuration => "ffprobe не сообщил длительность видео.";
    public override string JobFfmpegPassFailed(int pass, int code) => $"ffmpeg завершился с ошибкой на проходе {pass} (код {code}).";
    public override string JobFfmpegFailed(int code) => $"ffmpeg не смог обработать файл (код {code}).";
    public override string JobFfmpegFailedOn(string name) => $"ffmpeg не смог обработать {name}.";
    public override string JobFfmpegNoOutput => "ffmpeg не создал результат.";
    public override string JobFfmpegArchiveNotFound => "Не нашёлся подходящий архив ffmpeg для Windows.";
    public override string JobArchiveLacks(string exe) => $"В архиве нет {exe}.";
    public override string JobFfmpegTarFailed(int code) => $"Не удалось распаковать архив ffmpeg (tar, код {code}).";

    public override string JobSavedAs(string name) => $"Сохранено как {name}.";
    public override string JobBatchDone(int done, int total) => $"Готово: {done} из {total}.";
    public override string JobBatchSkipped(int skipped) => $"Пропущено: {skipped} (делать нечего).";
    public override string JobBatchFailed(int failed, string name, string message) => $"Ошибок: {failed}. {name}: {message}";
    public override string JobItemOf(int number, int total) => $"{number} из {total}";
    public override string JobFileNotFound => "Файл не найден.";
    public override string JobNoFileAccess => "Нет доступа к файлу.";
    public override string JobAlreadyInFormat(string format) => $"Уже в формате {format}.";

    public override string JobSuffixMuted => "_без_звука";
    public override string JobSuffixFrame => "_кадр";
    public override string JobSuffixRotated => "_повернуто";
    public override string JobSuffixRotatedLeft => "_повернуто_влево";
    public override string JobSuffixMirrored => "_отражено";
    public override string JobSuffixNormalized => "_выровнено";
    public override string JobSuffixSpedUp => "_x1,5";
    public override string JobSuffixSlowedDown => "_x0,75";
    public override string JobSuffixDenoised => "_без_шума";
    public override string JobSuffixMono => "_моно";
    public override string JobSuffixTrimmed => "_обрезка";
    public override string JobDefaultPdfName => "Картинки";

    public override string JobSpeechAuto => "Авто";
    public override string JobNeedsSpeechModel => "Нужна модель распознавания речи";
    public override string JobWhisperModelMissing => "Файл модели whisper не найден";
    public override string JobFfmpegWithoutWhisper => "Ваш ffmpeg собран без whisper";
    public override string JobNoSpeechFound => "Речь не найдена — файл не создан.";
    public override string JobModelBase => "Базовая";
    public override string JobModelAccurate => "Точная";

    public override string JobTrimNegativeStart => "Начало не может быть отрицательным.";
    public override string JobTrimEndBeforeStart => "Конец должен быть позже начала.";
    public override string JobTrimTooShort => "Слишком короткий отрезок.";
    public override string JobTrimEndPastFile(string duration) => $"Конец позже конца файла ({duration}).";
    public override string JobTrimWholeFile => "Выбран весь файл — обрезать нечего.";

    public override string JobDownloadNoSpace(long megabytes) => $"Мало места на диске: нужно около {megabytes} МБ.";
    public override string JobDownloadCorrupted => "Скачанный файл повреждён (не совпала контрольная сумма). Попробуйте ещё раз.";
    public override string JobDownloadStalled => "Загрузка остановилась: сервер перестал присылать данные. Попробуйте ещё раз позже.";

    public override string JobHotkeyUseKey => "Используйте букву, цифру или F1–F24.";
    public override string JobHotkeyAddModifier => "Добавьте Ctrl, Alt или Win.";

    public override string JobToday => "Сегодня";
    public override string JobYesterday => "Вчера";
    public override string JobDayLabel(DateTime day, bool withYear) => day.ToString(withYear ? "d MMMM yyyy" : "d MMMM", Culture);
    public override string JobStatusDone => "Готово";
    public override string JobStatusNothingToDo => "Ничего не нужно";
    public override string JobStatusCancelled => "Отменено";
    public override string JobStatusFailed => "Ошибка";
}

public sealed partial class EnTexts
{
    public override string JobStageAnalyzing => "Analyzing file";
    public override string JobStageProcessing => "Processing";
    public override string JobStagePreparing => "Preparing";
    public override string JobStageDone => "Done";
    public override string JobStagePass(int pass) => $"Pass {pass} of 2";
    public override string JobStageRefiningPass => "Pass 2 of 2, refining";
    public override string JobStageTuningQuality => "Choosing quality";
    public override string JobStageDownscaling(int side) => $"Resizing to {side} px";
    public override string JobStagePage(int number, int total, string name) => $"Page {number} of {total} · {name}";
    public override string JobStageBuildingPdf => "Building PDF";
    public override string JobStageDownloadingFfmpeg(string version) => $"Downloading ffmpeg {version}";
    public override string JobStageUnpacking => "Unpacking";

    public override string JobAlreadySmallerThan(string target) => $"The file is already under {target}";
    public override string JobNoVideoTrack => "The file has no video track";
    public override string JobNoAudioTrack => "The file has no audio track.";
    public override string JobVideoTooLongFor(int minutes, string target) => $"Videos over {minutes} min do not fit in {target}";
    public override string JobVideoTooLong(string target, int videoKbps, int minKbps) =>
        $"The video is too long: {target} leaves {videoKbps} kbit/s for the picture, below the sensible minimum of {minKbps} kbit/s.";
    public override string JobNoWriteAccess =>
        "No permission to write to the file's folder. The result is saved next to the original, " +
        "so copy the video to a folder where you can create files.";
    public override string JobLowDiskSpace(string free, string required) =>
        $"Not enough disk space: {free} free, at least {required} needed.";
    public override string JobNoFreeName => "Could not find a free name for the result.";
    public override string JobAlreadySmallNoNeed(string target, string size) => $"The file is already under {target} ({size}), no need to compress.";
    public override string JobSlightlyOverTarget => "Slightly over the target: the encoder missed a little.";
    public override string JobCannotFitImage(string target) => $"Could not fit the image into {target}.";
    public override string JobCannotReadImage => "Could not read the image.";
    public override string JobCannotReadImageNamed(string name) => $"Could not read the image {name}.";

    public override string JobFfprobeCannotRead => "ffprobe could not read the file. It may be damaged.";
    public override string JobFfprobeBadOutput => "Could not parse the ffprobe output.";
    public override string JobFfprobeNoDuration => "ffprobe did not report the video duration.";
    public override string JobFfmpegPassFailed(int pass, int code) => $"ffmpeg failed on pass {pass} (code {code}).";
    public override string JobFfmpegFailed(int code) => $"ffmpeg could not process the file (code {code}).";
    public override string JobFfmpegFailedOn(string name) => $"ffmpeg could not process {name}.";
    public override string JobFfmpegNoOutput => "ffmpeg produced no output.";
    public override string JobFfmpegArchiveNotFound => "No suitable ffmpeg archive for Windows was found.";
    public override string JobArchiveLacks(string exe) => $"The archive has no {exe}.";
    public override string JobFfmpegTarFailed(int code) => $"Could not unpack the ffmpeg archive (tar, code {code}).";

    public override string JobSavedAs(string name) => $"Saved as {name}.";
    public override string JobBatchDone(int done, int total) => $"Done: {done} of {total}.";
    public override string JobBatchSkipped(int skipped) => $"Skipped: {skipped} (nothing to do).";
    public override string JobBatchFailed(int failed, string name, string message) => $"Errors: {failed}. {name}: {message}";
    public override string JobItemOf(int number, int total) => $"{number} of {total}";
    public override string JobFileNotFound => "File not found.";
    public override string JobNoFileAccess => "No access to the file.";
    public override string JobAlreadyInFormat(string format) => $"Already {format}.";

    public override string JobSuffixMuted => "_muted";
    public override string JobSuffixFrame => "_frame";
    public override string JobSuffixRotated => "_rotated";
    public override string JobSuffixRotatedLeft => "_rotated_left";
    public override string JobSuffixMirrored => "_mirrored";
    public override string JobSuffixNormalized => "_normalized";
    public override string JobSuffixSpedUp => "_x1.5";
    public override string JobSuffixSlowedDown => "_x0.75";
    public override string JobSuffixDenoised => "_denoised";
    public override string JobSuffixMono => "_mono";
    public override string JobSuffixTrimmed => "_trimmed";
    public override string JobDefaultPdfName => "Images";

    public override string JobSpeechAuto => "Auto";
    public override string JobNeedsSpeechModel => "Needs a speech recognition model";
    public override string JobWhisperModelMissing => "Whisper model file not found";
    public override string JobFfmpegWithoutWhisper => "Your ffmpeg is built without whisper";
    public override string JobNoSpeechFound => "No speech found, so no file was created.";
    public override string JobModelBase => "Base";
    public override string JobModelAccurate => "Accurate";

    public override string JobTrimNegativeStart => "The start cannot be negative.";
    public override string JobTrimEndBeforeStart => "The end must be after the start.";
    public override string JobTrimTooShort => "The segment is too short.";
    public override string JobTrimEndPastFile(string duration) => $"The end is past the end of the file ({duration}).";
    public override string JobTrimWholeFile => "The whole file is selected, nothing to trim.";

    public override string JobDownloadNoSpace(long megabytes) => $"Not enough disk space: about {megabytes} MB needed.";
    public override string JobDownloadCorrupted => "The downloaded file is damaged (checksum mismatch). Try again.";
    public override string JobDownloadStalled => "The download stalled: the server stopped sending data. Try again later.";

    public override string JobHotkeyUseKey => "Use a letter, a digit or F1–F24.";
    public override string JobHotkeyAddModifier => "Add Ctrl, Alt or Win.";

    public override string JobToday => "Today";
    public override string JobYesterday => "Yesterday";
    public override string JobDayLabel(DateTime day, bool withYear) => day.ToString(withYear ? "MMMM d, yyyy" : "MMMM d", Culture);
    public override string JobStatusDone => "Done";
    public override string JobStatusNothingToDo => "Nothing to do";
    public override string JobStatusCancelled => "Cancelled";
    public override string JobStatusFailed => "Failed";
}
