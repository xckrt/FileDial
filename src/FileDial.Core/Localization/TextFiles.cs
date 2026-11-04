namespace FileDial.Core.Localization;

// Текстовые файлы, SVG и значки: секторы, причины, этапы, итоги и части имён результатов.

public abstract partial class Texts
{
    // Секторы
    public abstract string TxtToUtf8 { get; }
    public abstract string TxtLineEndings { get; }
    /// <summary>Заголовок окна: «В CRLF (Windows)».</summary>
    public abstract string TxtToEol(string style);
    public abstract string TxtToXlsx { get; }
    public abstract string TxtJsonPretty { get; }
    public abstract string TxtJsonMinify { get; }
    public abstract string TxtToHtml { get; }
    public abstract string TxtToPng { get; }
    public abstract string TxtSvgToPngCaption(int side);
    public abstract string TxtPngSizes { get; }
    public abstract string TxtToIco { get; }
    public abstract string TxtIcoLargestCaption { get; }
    public abstract string TxtAllSizesToPng { get; }

    // Причины неактивности (без точки — дописывается там, где это сообщение об ошибке)
    public abstract string TxtLooksBinary { get; }
    public abstract string TxtAsciiIsUtf8 { get; }
    public abstract string TxtAlreadyUtf8(string encoding);
    public abstract string TxtAllUtf8 { get; }
    public abstract string TxtSingleLine { get; }
    public abstract string TxtAlreadyEol(string style);
    public abstract string TxtAllAlreadyEol(string style);
    public abstract string TxtOnlyCsv { get; }
    public abstract string TxtOnlyJson { get; }
    public abstract string TxtOnlyMarkdown { get; }
    public abstract string TxtJsonError(long line, long column);
    public abstract string TxtJsonAlreadyPretty { get; }
    public abstract string TxtJsonAlreadyMinified { get; }
    public abstract string TxtNeedsEdge { get; }
    public abstract string TxtSvgNoDecoder { get; }
    public abstract string TxtIcoUnreadable { get; }
    public abstract string TxtIcoSingleSize { get; }

    // Подпись в центре круга
    public abstract string TxtLines(int n);
    /// <summary>«7 размеров, до 256×256».</summary>
    public abstract string TxtIconSizes(int n, string largest);
    public abstract string TxtTextFiles(int n);
    public abstract string TxtSvgFiles(int n);
    public abstract string TxtIconFiles(int n);

    // Этапы и итоги
    public abstract string TxtStageConverting { get; }
    public abstract string TxtStageTable { get; }
    public abstract string TxtStagePrinting { get; }
    public abstract string TxtStageRendering(int side);
    public abstract string TxtTooLarge(string limit);
    /// <summary>Итог перекодировки с объяснением, почему с BOM или без.</summary>
    public abstract string TxtUtf8Done(string from, bool bom);
    public abstract string TxtEolDone(int count, string style);
    public abstract string TxtXlsxDone(int rows, int columns, string delimiter);
    public abstract string TxtComma { get; }
    public abstract string TxtSemicolon { get; }
    public abstract string TxtTab { get; }
    public abstract string TxtCellsTruncated(int cells, int limit);
    public abstract string TxtTooManyRows(int limit);
    public abstract string TxtTooManyColumns(int limit);
    public abstract string TxtCsvEmpty { get; }
    public abstract string TxtHtmlDone { get; }
    public abstract string TxtPdfDone { get; }
    public abstract string TxtEdgeTimeout { get; }
    public abstract string TxtEdgeFailed { get; }
    public abstract string TxtSvgRenderFailed { get; }
    public abstract string TxtIcoDone(string sizes);
    public abstract string TxtPngSetDone(int files, string folder);

    // Части имён результатов
    /// <summary>«заметки_utf8.txt».</summary>
    public abstract string TxtUtf8Suffix { get; }
    public abstract string TxtCrlfSuffix { get; }
    public abstract string TxtLfSuffix { get; }
    /// <summary>«данные_формат.json».</summary>
    public abstract string TxtJsonPrettySuffix { get; }
    /// <summary>«данные.min.json».</summary>
    public abstract string TxtJsonMinSuffix { get; }
    /// <summary>Папка с PNG: «логотип (PNG)».</summary>
    public abstract string TxtPngFolder(string stem);
}

public sealed partial class RuTexts
{
    public override string TxtToUtf8 => "В UTF-8";
    public override string TxtLineEndings => "Переводы строк";
    public override string TxtToEol(string style) => $"Переводы строк: {style}";
    public override string TxtToXlsx => "В XLSX";
    public override string TxtJsonPretty => "Форматировать JSON";
    public override string TxtJsonMinify => "Сжать JSON";
    public override string TxtToHtml => "В HTML";
    public override string TxtToPng => "В PNG";
    public override string TxtSvgToPngCaption(int side) => $"В PNG ({side} px)";
    public override string TxtPngSizes => "PNG всех размеров";
    public override string TxtToIco => "В ICO";
    public override string TxtIcoLargestCaption => "В PNG (самый крупный размер)";
    public override string TxtAllSizesToPng => "Все размеры в PNG";

    public override string TxtLooksBinary => "Похоже, это не текст, а двоичный файл";
    public override string TxtAsciiIsUtf8 => "Только латиница — это уже годится как UTF-8";
    public override string TxtAlreadyUtf8(string encoding) => $"Уже в кодировке {encoding}";
    public override string TxtAllUtf8 => "Все файлы уже в UTF-8";
    public override string TxtSingleLine => "В файле одна строка — переводить нечего";
    public override string TxtAlreadyEol(string style) => $"Переводы строк уже {style}";
    public override string TxtAllAlreadyEol(string style) => $"Во всех файлах переводы строк уже {style}";
    public override string TxtOnlyCsv => "Только для таблиц CSV и TSV";
    public override string TxtOnlyJson => "Только для файлов JSON";
    public override string TxtOnlyMarkdown => "Только для файлов Markdown (.md)";
    public override string TxtJsonError(long line, long column) => $"Ошибка в JSON: строка {line}, символ {column}";
    public override string TxtJsonAlreadyPretty => "JSON уже оформлен с отступами";
    public override string TxtJsonAlreadyMinified => "JSON уже без лишних пробелов";
    public override string TxtNeedsEdge => "Нужен Microsoft Edge";
    public override string TxtSvgNoDecoder => "Ваш ffmpeg собран без поддержки SVG (нужна «full»-сборка)";
    public override string TxtIcoUnreadable => "Не удалось прочитать значок";
    public override string TxtIcoSingleSize => "В значке только один размер";

    public override string TxtLines(int n) => N(n, "строка", "строки", "строк");
    public override string TxtIconSizes(int n, string largest) =>
        n == 1 ? $"1 размер, {largest}" : $"{N(n, "размер", "размера", "размеров")}, до {largest}";
    public override string TxtTextFiles(int n) => N(n, "текстовый файл", "текстовых файла", "текстовых файлов");
    public override string TxtSvgFiles(int n) => N(n, "рисунок SVG", "рисунка SVG", "рисунков SVG");
    public override string TxtIconFiles(int n) => N(n, "значок", "значка", "значков");

    public override string TxtStageConverting => "Преобразование";
    public override string TxtStageTable => "Сборка таблицы";
    public override string TxtStagePrinting => "Печать в PDF через Microsoft Edge";
    public override string TxtStageRendering(int side) => $"Отрисовка {side} px";
    public override string TxtTooLarge(string limit) => $"Файл больше {limit} — слишком большой для этого действия.";
    public override string TxtUtf8Done(string from, bool bom) => bom
        ? $"Перекодировано из {from} в UTF-8 с BOM — по нему Excel узнаёт кодировку таблицы."
        : $"Перекодировано из {from} в UTF-8 без BOM — так принято для текста, JSON, XML и скриптов.";
    public override string TxtEolDone(int count, string style) =>
        $"{N(count, "перевод строки", "перевода строки", "переводов строки")} теперь {style}.";
    public override string TxtXlsxDone(int rows, int columns, string delimiter) =>
        $"{N(rows, "строка", "строки", "строк")}, {N(columns, "столбец", "столбца", "столбцов")}, разделитель — {delimiter}.";
    public override string TxtComma => "запятая";
    public override string TxtSemicolon => "точка с запятой";
    public override string TxtTab => "табуляция";
    public override string TxtCellsTruncated(int cells, int limit) =>
        $"{N(cells, "ячейка обрезана", "ячейки обрезаны", "ячеек обрезано")} до {limit} символов (предел Excel).";
    public override string TxtTooManyRows(int limit) => $"В таблице больше {limit} строк — столько не помещается в лист Excel.";
    public override string TxtTooManyColumns(int limit) => $"В таблице больше {limit} столбцов — столько не помещается в лист Excel.";
    public override string TxtCsvEmpty => "Таблица пустая";
    public override string TxtHtmlDone => "Страница со встроенными стилями, открывается в любом браузере.";
    public override string TxtPdfDone => "PDF напечатан через Microsoft Edge.";
    public override string TxtEdgeTimeout => "Microsoft Edge не ответил вовремя";
    public override string TxtEdgeFailed => "Microsoft Edge не смог напечатать страницу в PDF";
    public override string TxtSvgRenderFailed => "ffmpeg не смог нарисовать этот SVG";
    public override string TxtIcoDone(string sizes) => $"Размеры внутри: {sizes} px.";
    public override string TxtPngSetDone(int files, string folder) =>
        $"{N(files, "картинка", "картинки", "картинок")} в папке {folder}.";

    public override string TxtUtf8Suffix => "_utf8";
    public override string TxtCrlfSuffix => "_crlf";
    public override string TxtLfSuffix => "_lf";
    public override string TxtJsonPrettySuffix => "_формат";
    public override string TxtJsonMinSuffix => ".min";
    public override string TxtPngFolder(string stem) => $"{stem} (PNG)";
}

public sealed partial class EnTexts
{
    public override string TxtToUtf8 => "To UTF-8";
    public override string TxtLineEndings => "Line endings";
    public override string TxtToEol(string style) => $"Line endings: {style}";
    public override string TxtToXlsx => "To XLSX";
    public override string TxtJsonPretty => "Format JSON";
    public override string TxtJsonMinify => "Minify JSON";
    public override string TxtToHtml => "To HTML";
    public override string TxtToPng => "To PNG";
    public override string TxtSvgToPngCaption(int side) => $"To PNG ({side} px)";
    public override string TxtPngSizes => "PNG in all sizes";
    public override string TxtToIco => "To ICO";
    public override string TxtIcoLargestCaption => "To PNG (largest size)";
    public override string TxtAllSizesToPng => "All sizes to PNG";

    public override string TxtLooksBinary => "This looks like a binary file, not text";
    public override string TxtAsciiIsUtf8 => "Plain ASCII is already valid UTF-8";
    public override string TxtAlreadyUtf8(string encoding) => $"Already {encoding}";
    public override string TxtAllUtf8 => "All files are already UTF-8";
    public override string TxtSingleLine => "The file is a single line, nothing to change";
    public override string TxtAlreadyEol(string style) => $"Line endings are already {style}";
    public override string TxtAllAlreadyEol(string style) => $"All files already use {style}";
    public override string TxtOnlyCsv => "Only for CSV and TSV tables";
    public override string TxtOnlyJson => "Only for JSON files";
    public override string TxtOnlyMarkdown => "Only for Markdown (.md) files";
    public override string TxtJsonError(long line, long column) => $"JSON error: line {line}, character {column}";
    public override string TxtJsonAlreadyPretty => "The JSON is already indented";
    public override string TxtJsonAlreadyMinified => "The JSON is already minified";
    public override string TxtNeedsEdge => "Needs Microsoft Edge";
    public override string TxtSvgNoDecoder => "Your ffmpeg is built without SVG support (a \"full\" build is needed)";
    public override string TxtIcoUnreadable => "Could not read the icon";
    public override string TxtIcoSingleSize => "The icon has only one size";

    public override string TxtLines(int n) => N(n, "line", "lines");
    public override string TxtIconSizes(int n, string largest) =>
        n == 1 ? $"1 size, {largest}" : $"{N(n, "size", "sizes")}, up to {largest}";
    public override string TxtTextFiles(int n) => N(n, "text file", "text files");
    public override string TxtSvgFiles(int n) => N(n, "SVG image", "SVG images");
    public override string TxtIconFiles(int n) => N(n, "icon", "icons");

    public override string TxtStageConverting => "Converting";
    public override string TxtStageTable => "Building the table";
    public override string TxtStagePrinting => "Printing to PDF with Microsoft Edge";
    public override string TxtStageRendering(int side) => $"Rendering {side} px";
    public override string TxtTooLarge(string limit) => $"The file is larger than {limit}, too big for this action.";
    public override string TxtUtf8Done(string from, bool bom) => bom
        ? $"Converted from {from} to UTF-8 with BOM, so Excel recognizes the encoding of the table."
        : $"Converted from {from} to UTF-8 without BOM, the usual choice for text, JSON, XML and scripts.";
    public override string TxtEolDone(int count, string style) =>
        count == 1 ? $"1 line ending is now {style}." : $"{count} line endings are now {style}.";
    public override string TxtXlsxDone(int rows, int columns, string delimiter) =>
        $"{N(rows, "row", "rows")}, {N(columns, "column", "columns")}, delimiter: {delimiter}.";
    public override string TxtComma => "comma";
    public override string TxtSemicolon => "semicolon";
    public override string TxtTab => "tab";
    public override string TxtCellsTruncated(int cells, int limit) =>
        $"{N(cells, "cell was", "cells were")} cut to {limit} characters (the Excel limit).";
    public override string TxtTooManyRows(int limit) => $"The table has more than {limit} rows, which do not fit in an Excel sheet.";
    public override string TxtTooManyColumns(int limit) => $"The table has more than {limit} columns, which do not fit in an Excel sheet.";
    public override string TxtCsvEmpty => "The table is empty";
    public override string TxtHtmlDone => "A page with built-in styles that opens in any browser.";
    public override string TxtPdfDone => "Printed to PDF with Microsoft Edge.";
    public override string TxtEdgeTimeout => "Microsoft Edge did not respond in time";
    public override string TxtEdgeFailed => "Microsoft Edge could not print the page to PDF";
    public override string TxtSvgRenderFailed => "ffmpeg could not render this SVG";
    public override string TxtIcoDone(string sizes) => $"Sizes inside: {sizes} px.";
    public override string TxtPngSetDone(int files, string folder) => $"{N(files, "image", "images")} in the folder {folder}.";

    public override string TxtUtf8Suffix => "_utf8";
    public override string TxtCrlfSuffix => "_crlf";
    public override string TxtLfSuffix => "_lf";
    public override string TxtJsonPrettySuffix => "_formatted";
    public override string TxtJsonMinSuffix => ".min";
    public override string TxtPngFolder(string stem) => $"{stem} (PNG)";
}
