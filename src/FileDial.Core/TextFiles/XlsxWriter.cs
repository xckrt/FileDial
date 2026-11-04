using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace FileDial.Core.TextFiles;

/// <summary>Итог записи таблицы.</summary>
public sealed record XlsxStats(int Rows, int Columns, int Numbers, int TruncatedCells);

/// <summary>
/// Минимальный XLSX своими силами: пять частей пакета, строки — встроенные (inlineStr), числа — числами.
/// Без общей таблицы строк: так файл пишется потоком и не держит всё в памяти дважды.
/// </summary>
public static partial class XlsxWriter
{
    public const int MaxRows = 1_048_576;
    public const int MaxColumns = 16_384;
    public const int MaxCellLength = 32_767;

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Число без потерь: без ведущих нулей (это коды и индексы), не длиннее 15 значащих цифр.</summary>
    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)([.,][0-9]+)?([eE][+-]?[0-9]{1,3})?$")]
    private static partial Regex NumberPattern();

    /// <summary>Значение как число для ячейки или null. Запятая — десятичная, если она не разделитель столбцов.</summary>
    public static string? AsNumber(string text, char delimiter)
    {
        var value = text.Trim();
        if (value.Length == 0 || value.Length > 40 || !NumberPattern().IsMatch(value))
            return null;
        if (value.Contains(',') && delimiter == ',')
            return null;
        var digits = value.TakeWhile(c => c is not ('e' or 'E')).Count(char.IsAsciiDigit);
        if (digits > 15)
            return null;
        var normalized = value.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
            ? normalized
            : null;
    }

    public static string ColumnName(int index)
    {
        var name = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
            name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    /// <summary>Имя листа: до 31 символа, без []:*?/\.</summary>
    public static string SheetName(string stem)
    {
        var clean = new string(stem.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\') && !char.IsControl(c)).ToArray())
            .Trim().Trim('\'');
        if (clean.Length > 31) clean = clean[..31].TrimEnd();
        return clean.Length == 0 ? "Sheet1" : clean;
    }

    public static XlsxStats Write(Stream output, string sheetName, Func<IEnumerable<List<string>>> rows, char delimiter,
        CancellationToken ct = default)
    {
        // Ширина столбцов — по первым строкам, чтобы таблица сразу читалась.
        var widths = new List<int>();
        foreach (var row in rows().Take(1000))
            for (var c = 0; c < row.Count && c < MaxColumns; c++)
            {
                while (widths.Count <= c) widths.Add(0);
                widths[c] = Math.Max(widths[c], Math.Min(row[c].Length, 60));
            }

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        Part(zip, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
            """);
        Part(zip, "_rels/.rels", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="{PackageRel}"><Relationship Id="rId1" Type="{Rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>
            """);
        Part(zip, "xl/workbook.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="{Main}" xmlns:r="{Rel}"><sheets><sheet name="{System.Security.SecurityElement.Escape(sheetName)}" sheetId="1" r:id="rId1"/></sheets></workbook>
            """);
        Part(zip, "xl/_rels/workbook.xml.rels", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="{PackageRel}"><Relationship Id="rId1" Type="{Rel}/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="{Rel}/styles" Target="styles.xml"/></Relationships>
            """);
        Part(zip, "xl/styles.xml", $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="{Main}"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs></styleSheet>
            """);

        var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CheckCharacters = false });
        xml.WriteStartDocument(true);
        xml.WriteStartElement("worksheet", Main);
        if (widths.Count > 0)
        {
            xml.WriteStartElement("cols", Main);
            for (var c = 0; c < widths.Count; c++)
            {
                xml.WriteStartElement("col", Main);
                xml.WriteAttributeString("min", Num(c + 1));
                xml.WriteAttributeString("max", Num(c + 1));
                xml.WriteAttributeString("width", Num(Math.Max(8, widths[c] + 2)));
                xml.WriteAttributeString("customWidth", "1");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteStartElement("sheetData", Main);

        int rowCount = 0, columns = 0, numbers = 0, truncated = 0;
        foreach (var row in rows())
        {
            ct.ThrowIfCancellationRequested();
            if (rowCount == MaxRows)
                throw new XlsxLimitException(true);
            if (row.Count > MaxColumns)
                throw new XlsxLimitException(false);
            rowCount++;
            columns = Math.Max(columns, row.Count);
            xml.WriteStartElement("row", Main);
            xml.WriteAttributeString("r", Num(rowCount));
            for (var c = 0; c < row.Count; c++)
            {
                var value = row[c];
                if (value.Length == 0)
                    continue;
                xml.WriteStartElement("c", Main);
                xml.WriteAttributeString("r", ColumnName(c) + Num(rowCount));
                if (AsNumber(value, delimiter) is { } number)
                {
                    numbers++;
                    xml.WriteElementString("v", Main, number);
                }
                else
                {
                    xml.WriteAttributeString("t", "inlineStr");
                    var text = XmlSafe(value);
                    if (text.Length > MaxCellLength)
                    {
                        text = text[..MaxCellLength];
                        if (char.IsHighSurrogate(text[^1])) text = text[..^1];
                        truncated++;
                    }
                    xml.WriteStartElement("is", Main);
                    xml.WriteStartElement("t", Main);
                    if (text.Length != text.Trim().Length || text.Contains('\n'))
                        xml.WriteAttributeString("xml", "space", null, "preserve");
                    xml.WriteString(text);
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndDocument();
        return new XlsxStats(rowCount, columns, numbers, truncated);
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    /// <summary>Убирает символы, недопустимые в XML (управляющие, одиночные суррогаты); CRLF внутри ячейки — в LF.</summary>
    public static string XmlSafe(string text)
    {
        text = text.Replace("\r\n", "\n");
        StringBuilder? sb = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            bool ok;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb?.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }
            ok = !char.IsSurrogate(c) && XmlConvert.IsXmlChar(c);
            if (!ok && sb is null)
                sb = new StringBuilder(text, 0, i, text.Length);
            if (ok)
                sb?.Append(c);
        }
        return sb?.ToString() ?? text;
    }

    private static void Part(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

/// <summary>Таблица не помещается в лист Excel.</summary>
public sealed class XlsxLimitException(bool rows) : Exception
{
    public bool Rows { get; } = rows;
}
