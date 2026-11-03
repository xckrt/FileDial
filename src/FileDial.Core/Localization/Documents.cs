namespace FileDial.Core.Localization;

// PDF и документы Office: задачи, ошибки, части имён результатов, запись страниц.

public abstract partial class Texts
{
    // PDF
    public abstract string PdfLocked { get; }
    public abstract string PdfPages(int n);
    public abstract string PdfCannotRead(string name);
    public abstract string PdfStageReading { get; }
    public abstract string PdfStageSaving { get; }
    public abstract string PdfStageEncrypting { get; }
    /// <summary>Часть имени файла: «отчёт_повёрнут.pdf».</summary>
    public abstract string PdfRotatedSuffix { get; }
    public abstract string PdfRotated(int pages, bool right, string name);
    public abstract string PdfSinglePageNothing { get; }
    public abstract string PdfStagePage(int index, int count);
    /// <summary>Имя файла страницы без расширения: «отчёт — стр. 01».</summary>
    public abstract string PdfPageFileName(string stem, string number);
    /// <summary>Папка со страницами: «отчёт (страницы)».</summary>
    public abstract string PdfPagesFolder(string stem);
    public abstract string PdfSplitDone(int pages, string folder);
    public abstract string PdfNoSuchPage(int pageCount);
    /// <summary>Часть имени файла: «отчёт_стр_1-3».</summary>
    public abstract string PdfPagesSuffix(string label);
    public abstract string PdfExtracted(int pages, int total, string name);
    public abstract string PdfMergeNeedsSeveral { get; }
    public abstract string PdfStageFile(int index, int count, string name);
    public abstract string PdfMergeLocked(string name);
    /// <summary>Часть имени файла: «Отчёты_объединено.pdf».</summary>
    public abstract string PdfMergedSuffix { get; }
    public abstract string PdfMerged(int files, int pages, string name);

    // Номера страниц
    public abstract string PdfRangeEmpty { get; }
    public abstract string PdfRangeUnclear(string part);
    public abstract string PdfRangeFromOne { get; }
    public abstract string PdfRangeTooFar(int pageCount);
    public abstract string PdfRangeReversed(string part);

    // Документы
    public abstract string DocDone { get; }
    public abstract string DocNoConverter { get; }
    public abstract string DocNoOffice { get; }
    public abstract string DocPackageOnly { get; }
    /// <summary>Имя, которое ставится вместо автора комментариев и исправлений.</summary>
    public abstract string DocAnonymous { get; }
    public abstract string DocAnonymousInitials { get; }
    /// <summary>Часть имени файла: «письмо_с_паролем.docx» (и у PDF).</summary>
    public abstract string DocPasswordSuffix { get; }
    public abstract string DocProtected(string name);
    public abstract string DocStageSavingAs(string app, string format);
    /// <summary>Дописывается к итогу через пробел в начале.</summary>
    public abstract string DocCsvFirstSheetNote { get; }
    public abstract string DocStageEncrypting(string app);
    public abstract string DocStageDocument(int index, int count, string name);
    public abstract string DocStageBuildingPdf { get; }
    public abstract string DocCombined(int documents, int pages, string name);
    public abstract string DocStageReadingText { get; }
    public abstract string DocNoText { get; }
    public abstract string DocTextDone(int words, string name);
    public abstract string DocNoImages { get; }
    public abstract string DocStageImage(int index, int count);
    /// <summary>Папка с картинками: «письмо (картинки)».</summary>
    public abstract string DocImagesFolder(string stem);
    public abstract string DocImagesDone(int images, string folder);
    public abstract string DocStageCleaning { get; }
    public abstract string DocRemovedProperties { get; }
    public abstract string DocRemovedCommentNames { get; }
    public abstract string DocRemovedRevisionNames { get; }
    public abstract string DocRemovedTemplatePath { get; }
    public abstract string DocNothingPersonal { get; }
    /// <summary>Часть имени файла: «письмо_без_данных.docx».</summary>
    public abstract string DocCleanSuffix { get; }
    public abstract string DocStripped(string removed, string name);
    public abstract string DocImagesAlreadyCompact { get; }
    public abstract string DocStageRebuilding { get; }
    public abstract string DocCompressFailed { get; }
    /// <summary>Часть имени файла: «письмо_сжат.docx».</summary>
    public abstract string DocCompressedSuffix { get; }
    public abstract string DocImagesRecompressed(int images);
    public abstract string DocAppFailed(string app, string name);
    public abstract string DocOfficeTimeout { get; }
    public abstract string DocPasswordOfficeOnly { get; }
    public abstract string DocLibreFailed(string name);
    public abstract string DocCannotReadPackage(string name);
    /// <summary>Разделитель слайдов в извлечённом тексте: «— Слайд 2 —».</summary>
    public abstract string DocSlideMarker(int number);
    public abstract string DocTextUnsupported { get; }
    public abstract string DocDamaged { get; }
}

public sealed partial class RuTexts
{
    public override string PdfLocked => "PDF защищён паролем.";
    public override string PdfPages(int n) => N(n, "страница", "страницы", "страниц");
    public override string PdfCannotRead(string name) => $"Не удалось прочитать {name}: файл повреждён или это не PDF.";
    public override string PdfStageReading => "Чтение PDF";
    public override string PdfStageSaving => "Сохранение";
    public override string PdfStageEncrypting => "Шифрование";
    public override string PdfRotatedSuffix => "_повёрнут";
    public override string PdfRotated(int pages, bool right, string name) =>
        $"{PdfPages(pages)} {(right ? "вправо" : "влево")}. Сохранено как {name}.";
    public override string PdfSinglePageNothing => "В документе одна страница — делить нечего.";
    public override string PdfStagePage(int index, int count) => $"Страница {index} из {count}";
    public override string PdfPageFileName(string stem, string number) => $"{stem} — стр. {number}";
    public override string PdfPagesFolder(string stem) => $"{stem} (страницы)";
    public override string PdfSplitDone(int pages, string folder) => $"{PdfPages(pages)} → папка {folder}.";
    public override string PdfNoSuchPage(int pageCount) => $"В документе {PdfPages(pageCount)} — такой страницы нет.";
    public override string PdfPagesSuffix(string label) => $"_стр_{label}";
    public override string PdfExtracted(int pages, int total, string name) => $"{PdfPages(pages)} из {total}. Сохранено как {name}.";
    public override string PdfMergeNeedsSeveral => "Для объединения выделите несколько PDF.";
    public override string PdfStageFile(int index, int count, string name) => $"Файл {index} из {count} · {name}";
    public override string PdfMergeLocked(string name) => $"{name} защищён паролем — снимите защиту и повторите.";
    public override string PdfMergedSuffix => "_объединено";
    public override string PdfMerged(int files, int pages, string name) =>
        $"{Files(files)}, {PdfPages(pages)}. Сохранено как {name}.";

    public override string PdfRangeEmpty => "Укажите страницы, например 1-3, 5";
    public override string PdfRangeUnclear(string part) => $"Непонятно: «{part}»";
    public override string PdfRangeFromOne => "Страницы считаются с 1";
    public override string PdfRangeTooFar(int pageCount) => $"В документе {PdfPages(pageCount)}";
    public override string PdfRangeReversed(string part) => $"«{part}»: начало больше конца";

    public override string DocDone => "Готово";
    public override string DocNoConverter => "Нужен Microsoft Office или LibreOffice";
    public override string DocNoOffice => "Нужен Microsoft Office";
    public override string DocPackageOnly => "Только для DOCX, XLSX, PPTX и OpenDocument";
    public override string DocAnonymous => "Автор";
    public override string DocAnonymousInitials => "А";
    public override string DocPasswordSuffix => "_с_паролем";
    public override string DocProtected(string name) => $"Сохранено как {name}. Без пароля документ не открыть — запишите его.";
    public override string DocStageSavingAs(string app, string format) => $"{app}: сохранение в {format}";
    public override string DocCsvFirstSheetNote => " В CSV попадает только первый лист.";
    public override string DocStageEncrypting(string app) => $"{app}: шифрование";
    public override string DocStageDocument(int index, int count, string name) => $"Документ {index} из {count} · {name}";
    public override string DocStageBuildingPdf => "Сборка PDF";
    public override string DocCombined(int documents, int pages, string name) =>
        $"{Documents(documents)}, {PdfPages(pages)}. Сохранено как {name}.";
    public override string DocStageReadingText => "Чтение текста";
    public override string DocNoText => "В документе нет текста — возможно, там только картинки.";
    public override string DocTextDone(int words, string name) => $"{N(words, "слово", "слова", "слов")}. Сохранено как {name}.";
    public override string DocNoImages => "В документе нет картинок.";
    public override string DocStageImage(int index, int count) => $"Картинка {index} из {count}";
    public override string DocImagesFolder(string stem) => $"{stem} (картинки)";
    public override string DocImagesDone(int images, string folder) => $"{Images(images)} → папка {folder}.";
    public override string DocStageCleaning => "Очистка";
    public override string DocRemovedProperties => "автор и компания в свойствах";
    public override string DocRemovedCommentNames => "имена у комментариев";
    public override string DocRemovedRevisionNames => "имена у исправлений";
    public override string DocRemovedTemplatePath => "путь к шаблону";
    public override string DocNothingPersonal => "Личных данных не найдено — документ и так чистый.";
    public override string DocCleanSuffix => "_без_данных";
    public override string DocStripped(string removed, string name) => $"Убрано: {removed}. Сохранено как {name}.";
    public override string DocImagesAlreadyCompact => "Картинки в документе и так компактные — сжимать нечего.";
    public override string DocStageRebuilding => "Сборка документа";
    public override string DocCompressFailed => "Сжать заметно не получилось — документ оставлен как есть.";
    public override string DocCompressedSuffix => "_сжат";
    public override string DocImagesRecompressed(int images) => $"{Images(images)} пересжато.";
    public override string DocAppFailed(string app, string name) =>
        $"{app} не смог открыть или сохранить {name}: документ защищён паролем, повреждён или формат не поддерживается.";
    public override string DocOfficeTimeout => "Office не ответил за 5 минут — возможно, документ ждёт ответа в скрытом окне.";
    public override string DocPasswordOfficeOnly => "Пароль на документ ставится только через Microsoft Office.";
    public override string DocLibreFailed(string name) =>
        $"LibreOffice не смог сохранить {name}: документ защищён паролем, повреждён или формат не поддерживается.";
    public override string DocCannotReadPackage(string name) => $"Не удалось прочитать {name}: документ защищён паролем или повреждён.";
    public override string DocSlideMarker(int number) => $"— Слайд {number} —";
    public override string DocTextUnsupported => "Текст из этого формата напрямую не читается.";
    public override string DocDamaged => "Документ повреждён: нет основной части.";
}

public sealed partial class EnTexts
{
    public override string PdfLocked => "The PDF is password-protected.";
    public override string PdfPages(int n) => N(n, "page", "pages");
    public override string PdfCannotRead(string name) => $"Could not read {name}: the file is damaged or is not a PDF.";
    public override string PdfStageReading => "Reading PDF";
    public override string PdfStageSaving => "Saving";
    public override string PdfStageEncrypting => "Encrypting";
    public override string PdfRotatedSuffix => "_rotated";
    public override string PdfRotated(int pages, bool right, string name) =>
        $"{PdfPages(pages)} rotated {(right ? "right" : "left")}. Saved as {name}.";
    public override string PdfSinglePageNothing => "The document has one page, nothing to split.";
    public override string PdfStagePage(int index, int count) => $"Page {index} of {count}";
    public override string PdfPageFileName(string stem, string number) => $"{stem} — page {number}";
    public override string PdfPagesFolder(string stem) => $"{stem} (pages)";
    public override string PdfSplitDone(int pages, string folder) => $"{PdfPages(pages)} → folder {folder}.";
    public override string PdfNoSuchPage(int pageCount) => $"The document has {PdfPages(pageCount)}; there is no such page.";
    public override string PdfPagesSuffix(string label) => $"_pages_{label}";
    public override string PdfExtracted(int pages, int total, string name) => $"{PdfPages(pages)} of {total}. Saved as {name}.";
    public override string PdfMergeNeedsSeveral => "Select several PDFs to merge.";
    public override string PdfStageFile(int index, int count, string name) => $"File {index} of {count} · {name}";
    public override string PdfMergeLocked(string name) => $"{name} is password-protected. Remove the password and try again.";
    public override string PdfMergedSuffix => "_merged";
    public override string PdfMerged(int files, int pages, string name) =>
        $"{Files(files)}, {PdfPages(pages)}. Saved as {name}.";

    public override string PdfRangeEmpty => "Enter pages, for example 1-3, 5";
    public override string PdfRangeUnclear(string part) => $"Not understood: “{part}”";
    public override string PdfRangeFromOne => "Pages start at 1";
    public override string PdfRangeTooFar(int pageCount) => $"The document has {PdfPages(pageCount)}";
    public override string PdfRangeReversed(string part) => $"“{part}”: the start is after the end";

    public override string DocDone => "Done";
    public override string DocNoConverter => "Needs Microsoft Office or LibreOffice";
    public override string DocNoOffice => "Needs Microsoft Office";
    public override string DocPackageOnly => "Only for DOCX, XLSX, PPTX and OpenDocument";
    public override string DocAnonymous => "Author";
    public override string DocAnonymousInitials => "A";
    public override string DocPasswordSuffix => "_protected";
    public override string DocProtected(string name) => $"Saved as {name}. It cannot be opened without the password, so write it down.";
    public override string DocStageSavingAs(string app, string format) => $"{app}: saving as {format}";
    public override string DocCsvFirstSheetNote => " Only the first sheet goes into a CSV.";
    public override string DocStageEncrypting(string app) => $"{app}: encrypting";
    public override string DocStageDocument(int index, int count, string name) => $"Document {index} of {count} · {name}";
    public override string DocStageBuildingPdf => "Building PDF";
    public override string DocCombined(int documents, int pages, string name) =>
        $"{Documents(documents)}, {PdfPages(pages)}. Saved as {name}.";
    public override string DocStageReadingText => "Reading text";
    public override string DocNoText => "The document has no text; it may contain only images.";
    public override string DocTextDone(int words, string name) => $"{N(words, "word", "words")}. Saved as {name}.";
    public override string DocNoImages => "The document has no images.";
    public override string DocStageImage(int index, int count) => $"Image {index} of {count}";
    public override string DocImagesFolder(string stem) => $"{stem} (images)";
    public override string DocImagesDone(int images, string folder) => $"{Images(images)} → folder {folder}.";
    public override string DocStageCleaning => "Cleaning";
    public override string DocRemovedProperties => "author and company in properties";
    public override string DocRemovedCommentNames => "names on comments";
    public override string DocRemovedRevisionNames => "names on tracked changes";
    public override string DocRemovedTemplatePath => "template path";
    public override string DocNothingPersonal => "No personal data found; the document is already clean.";
    public override string DocCleanSuffix => "_clean";
    public override string DocStripped(string removed, string name) => $"Removed: {removed}. Saved as {name}.";
    public override string DocImagesAlreadyCompact => "The images in the document are already compact, nothing to compress.";
    public override string DocStageRebuilding => "Rebuilding the document";
    public override string DocCompressFailed => "Could not make it noticeably smaller; the document is left as is.";
    public override string DocCompressedSuffix => "_compressed";
    public override string DocImagesRecompressed(int images) => $"{Images(images)} recompressed.";
    public override string DocAppFailed(string app, string name) =>
        $"{app} could not open or save {name}: the document is password-protected, damaged, or in an unsupported format.";
    public override string DocOfficeTimeout => "Office did not respond in 5 minutes; the document may be waiting for an answer in a hidden window.";
    public override string DocPasswordOfficeOnly => "Only Microsoft Office can set a document password.";
    public override string DocLibreFailed(string name) =>
        $"LibreOffice could not save {name}: the document is password-protected, damaged, or in an unsupported format.";
    public override string DocCannotReadPackage(string name) => $"Could not read {name}: the document is password-protected or damaged.";
    public override string DocSlideMarker(int number) => $"— Slide {number} —";
    public override string DocTextUnsupported => "Text cannot be read directly from this format.";
    public override string DocDamaged => "The document is damaged: its main part is missing.";
}
