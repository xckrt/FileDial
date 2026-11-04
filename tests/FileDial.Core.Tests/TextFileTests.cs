using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Selection;
using FileDial.Core.TextFiles;

namespace FileDial.Core.Tests;

/// <summary>Общая папка для тестов текстовых файлов: кириллица и пробелы в пути.</summary>
public abstract class TextTestBase : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "FileDialText_" + Guid.NewGuid().ToString("N"), "Мои файлы");

    static TextTestBase() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    protected TextTestBase() => Directory.CreateDirectory(Dir);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(Dir)!, recursive: true);

    protected string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(Dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    protected string Write(string name, string text, Encoding encoding, bool bom = false) =>
        Write(name, [.. bom ? encoding.GetPreamble() : [], .. encoding.GetBytes(text)]);

    protected static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    protected static Encoding Cp1251 => Encoding.GetEncoding(1251);
    protected static Encoding Cp866 => Encoding.GetEncoding(866);

    protected static Task<JobResult> Run(IFileJob job, string path) => job.RunAsync(path, null, CancellationToken.None);

    /// <summary>Запуск с проверкой, что исходник не изменился.</summary>
    protected static async Task<JobResult> RunKeepingSource(IFileJob job, string path)
    {
        var before = Hash(path);
        var result = await Run(job, path);
        Assert.Equal(before, Hash(path));
        return result;
    }

    protected const string Russian = "Съешь же ещё этих мягких французских булок, да выпей чаю.\r\nПривет, мир!\r\n";
}

public class TextEncodingTests : TextTestBase
{
    [Fact]
    public void Detects_Boms_Utf8_Ascii()
    {
        Assert.Equal(TextEncodingKind.Utf8Bom, TextEncodings.Detect([0xEF, 0xBB, 0xBF, (byte)'a']));
        Assert.Equal(TextEncodingKind.Utf16Le, TextEncodings.Detect([0xFF, 0xFE, (byte)'a', 0]));
        Assert.Equal(TextEncodingKind.Utf16Be, TextEncodings.Detect([0xFE, 0xFF, 0, (byte)'a']));
        Assert.Equal(TextEncodingKind.Utf32Le, TextEncodings.Detect([0xFF, 0xFE, 0, 0, (byte)'a', 0, 0, 0]));
        Assert.Equal(TextEncodingKind.Utf8, TextEncodings.Detect(Encoding.UTF8.GetBytes(Russian)));
        Assert.Equal(TextEncodingKind.Ascii, TextEncodings.Detect("plain text\r\n"u8));
        Assert.Equal(TextEncodingKind.Utf16Le, TextEncodings.Detect(Encoding.Unicode.GetBytes("Hello, world! Привет")));
        Assert.Equal(TextEncodingKind.Binary, TextEncodings.Detect([0x50, 0x4B, 0x03, 0x04, 0, 0, 0x14, 0, 0, 0, 0x08, 0, 0, 0]));
    }

    [Fact]
    public void Legacy_Cp1251_Cp866_Cp1252()
    {
        Assert.Equal(TextEncodingKind.Windows1251, TextEncodings.Detect(Cp1251.GetBytes(Russian)));
        Assert.Equal(TextEncodingKind.Cp866, TextEncodings.Detect(Cp866.GetBytes(Russian)));
        Assert.Equal(TextEncodingKind.Windows1252,
            TextEncodings.Detect(Encoding.GetEncoding(1252).GetBytes("Café crème brûlée à la française, déjà vu.")));
        // Короткое слово заглавными — тоже 1251, а не DOS.
        Assert.Equal(TextEncodingKind.Windows1251, TextEncodings.Detect(Cp1251.GetBytes("ОТЧЁТ за март: итого 5 строк")));
    }

    [Fact]
    public void TruncatedSample_DoesNotBreakUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("ёжик");
        Assert.Equal(TextEncodingKind.Utf8, TextEncodings.Detect(bytes.AsSpan(0, bytes.Length - 1), truncated: true));
        Assert.NotEqual(TextEncodingKind.Utf8, TextEncodings.Detect(bytes.AsSpan(0, bytes.Length - 1)));
    }

    [Fact]
    public void Analysis_CountsLinesAndEndings()
    {
        var a = TextAnalysis.Of("a\r\nb\nc\rd"u8);
        Assert.Equal(new LineEndingCounts(1, 1, 1), a.LineEndings);
        Assert.Equal(LineEndingStyle.Mixed, a.LineEndings.Style);
        Assert.Equal(4, a.Lines);
        Assert.Equal(2, TextAnalysis.Of("a\nb\n"u8).Lines);
        Assert.Equal(LineEndingStyle.None, TextAnalysis.Of("одна строка"u8).LineEndings.Style);
        var utf16 = TextAnalysis.Of([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("раз\r\nдва\r\n")]);
        Assert.Equal(LineEndingStyle.Crlf, utf16.LineEndings.Style);
        Assert.Equal(2, utf16.Lines);
    }

    [Fact]
    public async Task Cp1251_ToUtf8_WithoutBom_KeepsCrlf()
    {
        var path = Write("заметки.txt", Russian, Cp1251);
        var result = await RunKeepingSource(new TextToUtf8Job(), path);

        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Equal("заметки_utf8.txt", Path.GetFileName(result.OutputPath));
        var bytes = File.ReadAllBytes(result.OutputPath!);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal(Russian, Encoding.UTF8.GetString(bytes));
        Assert.Contains("Windows-1251", result.Message);
        Assert.Contains("без BOM", result.Message);
    }

    [Fact]
    public async Task Csv_ToUtf8_GetsBom_ForExcel()
    {
        var path = Write("таблица.csv", "Имя;Сумма\nИван;10\n", Cp866);
        var result = await RunKeepingSource(new TextToUtf8Job(), path);
        var bytes = File.ReadAllBytes(result.OutputPath!);
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal("Имя;Сумма\nИван;10\n", Encoding.UTF8.GetString(bytes.AsSpan(3)));
        Assert.Contains("с BOM", result.Message);
    }

    [Fact]
    public async Task Utf16LeBom_ToUtf8()
    {
        var path = Write("лог.log", Russian, Encoding.Unicode, bom: true);
        var result = await RunKeepingSource(new TextToUtf8Job(), path);
        Assert.Equal(Russian, File.ReadAllText(result.OutputPath!, new UTF8Encoding(false)));
        Assert.Equal(TextEncodingKind.Utf8, TextEncodings.Detect(File.ReadAllBytes(result.OutputPath!)));
    }

    [Fact]
    public async Task AlreadyUtf8_NothingToDo_NoFile()
    {
        var path = Write("готово.txt", Russian, new UTF8Encoding(true), bom: true);
        var result = await RunKeepingSource(new TextToUtf8Job(), path);
        Assert.Equal(JobStatus.NothingToDo, result.Status);
        Assert.Single(Directory.GetFiles(Dir));
    }
}

public class LineEndingTests : TextTestBase
{
    [Fact]
    public async Task Crlf_ToLf_AndBack()
    {
        var path = Write("скрипт.txt", "раз\r\nдва\r\nтри", Encoding.UTF8);
        var lf = await RunKeepingSource(new LineEndingsJob(LineEndingStyle.Lf), path);
        Assert.Equal("скрипт_lf.txt", Path.GetFileName(lf.OutputPath));
        Assert.Equal("раз\nдва\nтри", File.ReadAllText(lf.OutputPath!));
        Assert.Contains("2 перевода строки", lf.Message);

        var crlf = await RunKeepingSource(new LineEndingsJob(LineEndingStyle.Crlf), lf.OutputPath!);
        Assert.Equal("раз\r\nдва\r\nтри", File.ReadAllText(crlf.OutputPath!));
    }

    [Fact]
    public void Mixed_AndLoneCr_AllConverted()
    {
        var bytes = LineBreaks.Convert("a\r\nb\nc\rd\r\n"u8, TextEncodingKind.Ascii, LineEndingStyle.Crlf);
        Assert.Equal("a\r\nb\r\nc\r\nd\r\n", Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public void Utf16_KeepsBomAndEncoding()
    {
        byte[] source = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("ы\nю\n")];
        var bytes = LineBreaks.Convert(source, TextEncodingKind.Utf16Le, LineEndingStyle.Crlf);
        Assert.Equal([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("ы\r\nю\r\n")], bytes);
        // «ਊ» (U+0A0A) содержит байт 0x0A, но это не перевод строки.
        byte[] tricky = Encoding.Unicode.GetBytes("\u0A0D\u0A0A");
        Assert.Equal(tricky, LineBreaks.Convert(tricky, TextEncodingKind.Utf16Le, LineEndingStyle.Crlf));
    }

    [Fact]
    public void Cp1251_ConvertedByteForByte()
    {
        var source = Cp1251.GetBytes("Ёлка\nёж\n");
        Assert.Equal(Cp1251.GetBytes("Ёлка\r\nёж\r\n"), LineBreaks.Convert(source, TextEncodingKind.Windows1251, LineEndingStyle.Crlf));
    }

    [Fact]
    public async Task SameStyle_OrSingleLine_NothingToDo()
    {
        var crlf = Write("a.txt", "x\r\ny\r\n", Encoding.UTF8);
        Assert.Equal(JobStatus.NothingToDo, (await RunKeepingSource(new LineEndingsJob(LineEndingStyle.Crlf), crlf)).Status);
        var single = Write("b.txt", "одна", Encoding.UTF8);
        Assert.Equal(JobStatus.NothingToDo, (await RunKeepingSource(new LineEndingsJob(LineEndingStyle.Lf), single)).Status);
        Assert.Equal(2, Directory.GetFiles(Dir).Length);
    }
}

public class CsvXlsxTests : TextTestBase
{
    [Theory]
    [InlineData("a,b,c\n1,2,3\n", ',')]
    [InlineData("a;b;c\n1,5;2;3\n", ';')]
    [InlineData("a\tb\tc\n1\t2\t3\n", '\t')]
    [InlineData("\"x,y\";b\n\"1,2\";3\n", ';')] // запятые внутри кавычек не считаются
    [InlineData("один столбец\nвторая\n", ',')]
    public void DetectsDelimiter(string text, char expected) => Assert.Equal(expected, Csv.DetectDelimiter(text));

    [Fact]
    public void Tsv_DefaultsToTab() => Assert.Equal('\t', Csv.DetectDelimiter("одно", ".tsv"));

    [Fact]
    public void Parses_Rfc4180_Quotes_EmbeddedNewlines_EmptyFields()
    {
        var rows = Csv.Parse("name,note\r\n\"Иванов, И.\",\"строка 1\r\nстрока 2\"\r\n\"он сказал \"\"да\"\"\",\r\n,\n", ',').ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(["Иванов, И.", "строка 1\r\nстрока 2"], rows[1]);
        Assert.Equal(["он сказал \"да\"", ""], rows[2]);
        Assert.Equal(["", ""], rows[3]);
    }

    [Fact]
    public void Parses_LastRowWithoutNewline_SkipsBlankLines()
    {
        var rows = Csv.Parse("a;b\n\n1;2", ';').ToList();
        Assert.Equal([["a", "b"], ["1", "2"]], rows);
    }

    [Theory]
    [InlineData("42", ',', "42")]
    [InlineData("-3.14", ',', "-3.14")]
    [InlineData("1,5", ';', "1.5")]
    [InlineData("1e5", ',', "1e5")]
    [InlineData(" 7 ", ',', "7")]
    [InlineData("0.5", ',', "0.5")]
    [InlineData("007", ',', null)]                // код с ведущими нулями
    [InlineData("+79991234567", ',', null)]       // телефон
    [InlineData("1234567890123456", ',', null)]   // 16 цифр — Excel потеряет точность
    [InlineData("1,5", ',', null)]
    [InlineData("12.03.2024", ',', null)]
    [InlineData("", ',', null)]
    public void NumberDetection(string text, char delimiter, string? expected) =>
        Assert.Equal(expected, XlsxWriter.AsNumber(text, delimiter));

    [Fact]
    public void ColumnNames()
    {
        Assert.Equal("A", XlsxWriter.ColumnName(0));
        Assert.Equal("Z", XlsxWriter.ColumnName(25));
        Assert.Equal("AA", XlsxWriter.ColumnName(26));
        Assert.Equal("XFD", XlsxWriter.ColumnName(16383));
        Assert.Equal("Отчёт за март", XlsxWriter.SheetName("Отчёт: за [март]?"));
    }

    [Fact]
    public async Task Csv_ToXlsx_ValidPackage_NumbersAndStrings()
    {
        var csv = "Товар;Цена;Код\r\n\"Хлеб; ржаной\";45,50;007\r\n\"Молоко\r\n3,2%\";89;100\r\n";
        var path = Write("продажи.csv", csv, Cp1251);
        var result = await RunKeepingSource(new CsvToXlsxJob(), path);

        Assert.Equal(JobStatus.Done, result.Status);
        Assert.Equal("продажи.xlsx", Path.GetFileName(result.OutputPath));
        Assert.Contains("3 строки", result.Message);
        Assert.Contains("точка с запятой", result.Message);

        using var zip = ZipFile.OpenRead(result.OutputPath!);
        Assert.Equal(
            ["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml", "xl/worksheets/sheet1.xml"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        foreach (var entry in zip.Entries)
            using (var s = entry.Open())
                XDocument.Load(s); // каждая часть — правильный XML

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument sheet;
        using (var s = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open())
            sheet = XDocument.Load(s);
        var cells = sheet.Descendants(ns + "c").ToDictionary(c => (string)c.Attribute("r")!);

        Assert.Equal("inlineStr", (string?)cells["A2"].Attribute("t"));
        Assert.Equal("Хлеб; ржаной", cells["A2"].Descendants(ns + "t").Single().Value);
        Assert.Null(cells["B2"].Attribute("t"));
        Assert.Equal("45.50", cells["B2"].Element(ns + "v")!.Value);
        Assert.Equal("inlineStr", (string?)cells["C2"].Attribute("t")); // 007 — текст
        Assert.Equal("Молоко\n3,2%", cells["A3"].Descendants(ns + "t").Single().Value);
        Assert.Equal("89", cells["B3"].Element(ns + "v")!.Value);

        XDocument workbook;
        using (var s = zip.GetEntry("xl/workbook.xml")!.Open())
            workbook = XDocument.Load(s);
        Assert.Equal("продажи", (string)workbook.Descendants(ns + "sheet").Single().Attribute("name")!);
    }

    [Fact]
    public async Task NotCsv_NothingToDo()
    {
        var path = Write("a.txt", "a,b\n1,2\n", Encoding.UTF8);
        Assert.Equal(JobStatus.NothingToDo, (await Run(new CsvToXlsxJob(), path)).Status);
    }

    [Fact]
    public void XmlSafe_DropsControlChars_KeepsEmoji()
    {
        Assert.Equal("ab😀", XlsxWriter.XmlSafe("a\u0001b😀"));
        Assert.Equal("a", XlsxWriter.XmlSafe("a\uD800"));
    }
}

public class JsonTextTests : TextTestBase
{
    [Fact]
    public void Pretty_UnescapesUnicode_KeepsNumbers()
    {
        var pretty = JsonText.Rewrite("{\"name\":\"\\u041F\\u0440\\u0438\\u0432\\u0435\\u0442\",\"n\":1.50,\"a\":[1,2]}", true, "\n");
        Assert.Equal("{\n  \"name\": \"Привет\",\n  \"n\": 1.50,\n  \"a\": [\n    1,\n    2\n  ]\n}\n", pretty);
    }

    [Fact]
    public void Minify_RemovesWhitespace()
    {
        Assert.Equal("{\"a\":[1,2],\"b\":\"x y\"}", JsonText.Rewrite("{\n  \"a\": [ 1, 2 ],\n  \"b\": \"x y\"\n}", false));
    }

    [Fact]
    public void Invalid_ReportsLineAndColumn()
    {
        var ex = Assert.Throws<JsonSyntaxException>(() => JsonText.Rewrite("{\n  \"a\": 1,\n  \"b\": }", true));
        Assert.Equal(3, ex.Line);
    }

    [Fact]
    public async Task Jobs_WriteUtf8WithoutBom_SourceUntouched()
    {
        var path = Write("данные.json", "{\"город\":\"Москва\",\"n\":[1,2]}", Encoding.UTF8);
        var pretty = await RunKeepingSource(new JsonJob(indented: true), path);
        Assert.Equal("данные_формат.json", Path.GetFileName(pretty.OutputPath));
        var bytes = File.ReadAllBytes(pretty.OutputPath!);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("\"город\": \"Москва\"", Encoding.UTF8.GetString(bytes));

        var min = await RunKeepingSource(new JsonJob(indented: false), pretty.OutputPath!);
        Assert.Equal("данные_формат.min.json", Path.GetFileName(min.OutputPath));
        Assert.Equal("{\"город\":\"Москва\",\"n\":[1,2]}", File.ReadAllText(min.OutputPath!));

        Assert.Equal(JobStatus.NothingToDo, (await Run(new JsonJob(indented: false), path)).Status);
    }

    [Fact]
    public async Task BrokenJson_FailsWithPlace()
    {
        var path = Write("битый.json", "{\"a\": [1, 2}", Encoding.UTF8);
        var ex = await Assert.ThrowsAsync<CompressionException>(() => Run(new JsonJob(true), path));
        Assert.Contains("строка 1", ex.Message);
    }
}

public class MarkdownTests : TextTestBase
{
    [Fact]
    public void Blocks_And_Inline()
    {
        var html = Markdown.ToHtml("""
            # Заголовок

            Текст с **жирным**, *курсивом*, `кодом` и [ссылкой](https://example.com "подсказка").

            - первый
            - второй
              - вложенный

            1. раз
            2. два

            > цитата

            ```csharp
            var x = 1 < 2;
            ```

            | Имя | Сумма |
            |:----|------:|
            | Иван | 10 |

            ---
            Второй
            ======
            """);

        Assert.Contains("<h1 id=\"заголовок\">Заголовок</h1>", html);
        Assert.Contains("<strong>жирным</strong>", html);
        Assert.Contains("<em>курсивом</em>", html);
        Assert.Contains("<code>кодом</code>", html);
        Assert.Contains("<a href=\"https://example.com\" title=\"подсказка\">ссылкой</a>", html);
        Assert.Contains("<ul>\n<li>первый</li>\n<li>второй\n<ul>\n<li>вложенный</li>\n</ul></li>\n</ul>", html);
        Assert.Contains("<ol>\n<li>раз</li>\n<li>два</li>\n</ol>", html);
        Assert.Contains("<blockquote>\n<p>цитата</p>\n</blockquote>", html);
        Assert.Contains("<pre><code class=\"language-csharp\">var x = 1 &lt; 2;</code></pre>", html);
        Assert.Contains("<th style=\"text-align:left\">Имя</th>", html);
        Assert.Contains("<td style=\"text-align:right\">10</td>", html);
        Assert.Contains("<hr>", html);
        Assert.Contains("<h1 id=\"второй\">Второй</h1>", html);
    }

    [Fact]
    public void RawHtml_AndScriptLinks_AreNeutralized()
    {
        var html = Markdown.ToHtml("<script>alert(1)</script> [x](javascript:alert(1)) ![i](data:image/png;base64,AA)");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<a href=\"#\">x</a>", html);
        Assert.Contains("src=\"data:image/png;base64,AA\"", html);
    }

    [Fact]
    public void CodeSpans_KeepMarkup_Escapes()
    {
        var html = Markdown.ToHtml("`**не жирный**` и \\*звёздочка\\*");
        Assert.Contains("<code>**не жирный**</code>", html);
        Assert.Contains("*звёздочка*", html);
        Assert.DoesNotContain("<em>", html);
    }

    [Fact]
    public void RelativeImages_BecomeAbsolute_WhenOutputElsewhere()
    {
        var html = Markdown.ToHtml("![logo](img/logo.png)", @"C:\Docs\Проект");
        Assert.Contains("src=\"file:///C:/Docs/", html);
        Assert.Contains("img/logo.png\"", html);
    }

    [Fact]
    public async Task Job_WritesSelfContainedPage()
    {
        var path = Write("README.md", "# Привет\n\nТекст.\n", Encoding.UTF8);
        var result = await RunKeepingSource(new MarkdownHtmlJob(), path);
        Assert.Equal("README.html", Path.GetFileName(result.OutputPath));
        var page = File.ReadAllText(result.OutputPath!);
        Assert.Contains("<meta charset=\"utf-8\">", page);
        Assert.Contains("<title>Привет</title>", page);
        Assert.Contains("<style>", page);
        Assert.DoesNotContain("<link", page);
    }

    [Fact]
    public async Task Pdf_ThroughEdge()
    {
        if (EdgeBrowser.Find() is null)
            return; // Edge нет — проверять нечего
        var path = Write("заметка.md", "# Отчёт\n\n- пункт\n", Encoding.UTF8);
        var result = await RunKeepingSource(new MarkdownPdfJob(), path);
        Assert.Equal("заметка.pdf", Path.GetFileName(result.OutputPath));
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(result.OutputPath!), 0, 5));
        Assert.Empty(Directory.GetFiles(Dir, ".filedial-*"));
    }
}

public class IcoTests : TextTestBase
{
    private static byte[] SolidPng(int size, byte r, byte g, byte b, byte a = 255)
    {
        var rgba = new byte[size * size * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = a;
        }
        return Png.Encode(size, size, rgba);
    }

    [Fact]
    public void Write_Read_RoundTrip_256IsZeroByte()
    {
        var ico = IcoFile.Write([SolidPng(16, 255, 0, 0), SolidPng(32, 0, 255, 0), SolidPng(256, 0, 0, 255)]);
        Assert.Equal([0, 0, 1, 0, 3, 0], ico[..6]);
        Assert.Equal(16, ico[6]);
        Assert.Equal(0, ico[6 + 32]); // 256 записывается нулём
        Assert.Equal(32, BitConverter.ToUInt16(ico, 6 + 6)); // 32 бита

        var frames = IcoFile.Read(ico);
        Assert.Equal([16, 32, 256], frames.Select(f => f.Width));
        Assert.All(frames, f => Assert.True(f.IsPng));
        Assert.Equal(256, IcoFile.Largest(frames).Width);
    }

    [Fact]
    public void Png_IsValidStructure()
    {
        var png = SolidPng(3, 1, 2, 3);
        Assert.Equal((3, 3), Png.Size(png));
        Assert.Equal("IEND", Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }

    /// <summary>DIB 32 бит 2×2 с альфой и DIB 1 бит с маской AND.</summary>
    private static byte[] Dib(int bits, byte[] pixels, byte[] mask, byte[]? palette = null)
    {
        var header = new byte[40];
        BitConverter.GetBytes(40).CopyTo(header, 0);
        BitConverter.GetBytes(2).CopyTo(header, 4);
        BitConverter.GetBytes(4).CopyTo(header, 8); // высота ×2
        BitConverter.GetBytes((short)1).CopyTo(header, 12);
        BitConverter.GetBytes((short)bits).CopyTo(header, 14);
        return [.. header, .. palette ?? [], .. pixels, .. mask];
    }

    private static byte[] Rgba(byte[] png)
    {
        // Раскодировать свой же PNG: без фильтров, один IDAT.
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33));
        using var z = new ZLibStream(new MemoryStream(png, 41, length), CompressionMode.Decompress);
        var raw = new MemoryStream();
        z.CopyTo(raw);
        var (w, h) = Png.Size(png);
        var data = raw.ToArray();
        return Enumerable.Range(0, h).SelectMany(y => data.Skip(y * (w * 4 + 1) + 1).Take(w * 4)).ToArray();
    }

    [Fact]
    public void Dib32_UsesAlpha_BottomUp()
    {
        // Нижняя строка: красный, зелёный; верхняя: синий полупрозрачный, прозрачный.
        byte[] pixels = [0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 128, 0, 0, 0, 0];
        var png = new IcoFrame(2, 2, 32, Dib(32, pixels, new byte[8])).ToPng();
        Assert.Equal(new byte[] { 0, 0, 255, 128, 0, 0, 0, 0, 255, 0, 0, 255, 0, 255, 0, 255 }, Rgba(png));
    }

    [Fact]
    public void Dib1_PaletteAndMask()
    {
        byte[] palette = [0, 0, 0, 0, 255, 255, 255, 0];
        // Строки по 4 байта: нижняя 10……, верхняя 01……
        byte[] pixels = [0b1000_0000, 0, 0, 0, 0b0100_0000, 0, 0, 0];
        // Маска: нижняя строка — правая точка прозрачна.
        byte[] mask = [0b0100_0000, 0, 0, 0, 0, 0, 0, 0];
        var rgba = Rgba(new IcoFrame(2, 2, 1, Dib(1, pixels, mask, palette)).ToPng());
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 255, 255, 255, 255, 0, 0, 0, 0 }, rgba);
    }

    [Fact]
    public async Task IcoToPng_TakesLargest_AllSizesToFolder()
    {
        var path = Write("app.ico", IcoFile.Write([SolidPng(16, 1, 1, 1), SolidPng(48, 2, 2, 2), SolidPng(32, 3, 3, 3)]));
        var largest = await RunKeepingSource(new IcoToPngJob(), path);
        Assert.Equal("app.png", Path.GetFileName(largest.OutputPath));
        Assert.Equal((48, 48), Png.Size(File.ReadAllBytes(largest.OutputPath!)));

        var all = await RunKeepingSource(new IcoAllPngJob(), path);
        Assert.Equal("app (PNG)", Path.GetFileName(all.OutputPath));
        Assert.Equal(["app_16x16.png", "app_32x32.png", "app_48x48.png"],
            Directory.GetFiles(all.OutputPath!).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task BrokenIco_ClearError()
    {
        var path = Write("broken.ico", [1, 2, 3, 4, 5, 6, 7]);
        var ex = await Assert.ThrowsAsync<CompressionException>(() => Run(new IcoToPngJob(), path));
        Assert.Contains("значок", ex.Message);
    }

    [Fact]
    public void IcoSizes_ForSmallImage_NoBigUpscale()
    {
        Assert.Equal([16, 24, 32, 48], ToIcoJob.SizesFor(40));
        Assert.Equal(IcoFile.StandardSizes, ToIcoJob.SizesFor(1000));
        Assert.Equal(IcoFile.StandardSizes, ToIcoJob.SizesFor(0));
    }

    [FfmpegFact]
    public async Task ImageToIco_WithFfmpeg()
    {
        var tools = FfmpegLocator.LocateDefault()!;
        var png = Path.Combine(Dir, "лого.png");
        var gen = await ProcessRunner.RunAsync(tools.Ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc=size=300x120", "-frames:v", "1", png],
            null, false, CancellationToken.None);
        Assert.Equal(0, gen.ExitCode);

        var job = FileJobs.Create(ActionIds.ImageToIco, tools)!;
        var result = await RunKeepingSource(job, png);
        Assert.Equal("лого.ico", Path.GetFileName(result.OutputPath));
        var frames = IcoFile.Read(File.ReadAllBytes(result.OutputPath!));
        Assert.Equal([16, 24, 32, 48, 64, 128, 256], frames.Select(f => f.Width));
        Assert.All(frames, f => Assert.Equal(f.Width, f.Height)); // вписано в квадрат
    }
}

/// <summary>Круги и подписи. Подмена «есть ли декодер SVG» общая — поэтому без параллельного запуска.</summary>
[Collection(LanguageCollection.Name)]
public class TextCatalogTests : TextTestBase
{
    private static DialAction Get(IReadOnlyList<DialAction> actions, string id) => ActionCatalog.Flatten(actions).Single(a => a.Id == id);

    [Theory]
    [InlineData("a.txt", FileKind.Text)]
    [InlineData("a.CSV", FileKind.Text)]
    [InlineData("a.json", FileKind.Text)]
    [InlineData("README.md", FileKind.Text)]
    [InlineData("a.svg", FileKind.Svg)]
    [InlineData("a.ico", FileKind.Icon)]
    public void Kinds(string name, FileKind kind) => Assert.Equal(kind, FileKinds.Of(name));

    [Fact]
    public void TextSectors_UnknownFile_AllButForeignEnabled()
    {
        var actions = ActionCatalog.ForFile(@"C:\нет\заметки.txt", 100);
        Assert.Equal(7, actions.Count);
        Assert.Equal(["В UTF-8", "Переводы строк", "В XLSX", "Форматировать JSON", "Сжать JSON", "В HTML", "В PDF"],
            actions.Select(a => a.Label));
        Assert.True(actions[0].IsEnabled);
        Assert.True(actions[1].IsGroup);
        Assert.Equal("Только для таблиц CSV и TSV", Get(actions, ActionIds.CsvToXlsx).DisabledReason);
        Assert.Equal("Только для файлов JSON", Get(actions, ActionIds.JsonFormat).DisabledReason);
        Assert.Equal("Только для файлов Markdown (.md)", Get(actions, ActionIds.MarkdownToHtml).DisabledReason);
    }

    [Fact]
    public void Utf8File_ToUtf8Disabled_OwnLineEndingDisabled()
    {
        var path = Write("a.txt", "привет\nмир\n", new UTF8Encoding(false));
        var actions = ActionCatalog.ForFile(path, 10);
        Assert.Equal("Уже в кодировке UTF-8", Get(actions, ActionIds.TextToUtf8).DisabledReason);
        Assert.Equal("Переводы строк уже LF", Get(actions, ActionIds.TextToLf).DisabledReason);
        Assert.True(Get(actions, ActionIds.TextToCrlf).IsEnabled);
        Assert.Equal("Переводы строк: CRLF (Windows)", Get(actions, ActionIds.TextToCrlf).Title);

        var ascii = ActionCatalog.ForFile(Write("b.txt", "abc", Encoding.ASCII), 3);
        Assert.Equal("Только латиница — это уже годится как UTF-8", Get(ascii, ActionIds.TextToUtf8).DisabledReason);
        Assert.False(ascii[1].IsEnabled);
        Assert.Equal("В файле одна строка — переводить нечего", ascii[1].DisabledReason);

        var legacy = ActionCatalog.ForFile(Write("c.txt", Russian, Cp1251), 3);
        Assert.True(Get(legacy, ActionIds.TextToUtf8).IsEnabled);
    }

    [Fact]
    public void Json_ReasonsFromContent()
    {
        var broken = ActionCatalog.ForFile(Write("a.json", "{\n\"a\": }", Encoding.UTF8), 1);
        Assert.StartsWith("Ошибка в JSON: строка 2", Get(broken, ActionIds.JsonFormat).DisabledReason);
        Assert.False(Get(broken, ActionIds.JsonMinify).IsEnabled);

        var minified = ActionCatalog.ForFile(Write("b.json", "{\"a\":1}", Encoding.UTF8), 1);
        Assert.True(Get(minified, ActionIds.JsonFormat).IsEnabled);
        Assert.Equal("JSON уже без лишних пробелов", Get(minified, ActionIds.JsonMinify).DisabledReason);

        var pretty = ActionCatalog.ForFile(Write("c.json", "{\r\n  \"a\": 1\r\n}\r\n", Encoding.UTF8), 1);
        Assert.Equal("JSON уже оформлен с отступами", Get(pretty, ActionIds.JsonFormat).DisabledReason);
        Assert.True(Get(pretty, ActionIds.JsonMinify).IsEnabled);
    }

    [Fact]
    public void CsvAndMarkdown_Enabled()
    {
        Assert.True(Get(ActionCatalog.ForFile(@"C:\нет\a.csv", 1), ActionIds.CsvToXlsx).IsEnabled);
        var md = ActionCatalog.ForFile(@"C:\нет\a.md", 1);
        Assert.True(Get(md, ActionIds.MarkdownToHtml).IsEnabled);
        Assert.Equal(EdgeBrowser.Find() is not null, Get(md, ActionIds.MarkdownToPdf).IsEnabled);
    }

    [Fact]
    public void Svg_WithoutDecoder_DisabledWithReason()
    {
        SvgSupport.Override = false;
        try
        {
            var actions = ActionCatalog.ForFile(@"C:\нет\logo.svg", 1);
            Assert.Equal(["В PNG", "PNG всех размеров", "В ICO"], actions.Select(a => a.Label));
            Assert.All(actions, a => Assert.StartsWith("Ваш ffmpeg собран без поддержки SVG", a.DisabledReason));
            SvgSupport.Override = true;
            Assert.All(ActionCatalog.ForFile(@"C:\нет\logo.svg", 1), a => Assert.True(a.IsEnabled));
            Assert.Equal("В PNG (1024 px)", ActionCatalog.ForFile(@"C:\нет\logo.svg", 1)[0].Title);
        }
        finally
        {
            SvgSupport.Override = null;
        }
    }

    [Fact]
    public void DecodersList_Parsing()
    {
        Assert.True(SvgSupport.HasSvgDecoder(" V....D librsvg              Librsvg rasterizer (codec svg)\n"));
        Assert.False(SvgSupport.HasSvgDecoder(" VF...D png                  PNG (Portable Network Graphics) image\n"));
    }

    [Fact]
    public void SvgDimensions_FromAttributesOrViewBox()
    {
        var a = Write("a.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"2in\" height=\"96px\"/>", Encoding.UTF8);
        Assert.Equal((192.0, 96.0), SvgSupport.Dimensions(a));
        var b = Write("b.svg", "<?xml version=\"1.0\"?><!DOCTYPE svg><svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 300 100\"/>", Encoding.UTF8);
        Assert.Equal((300.0, 100.0), SvgSupport.Dimensions(b));
        Assert.Equal((1024, 341), SvgSupport.Fit(SvgSupport.Dimensions(b), 1024));
    }

    [Fact]
    public void Icon_SingleSize_AllSizesDisabled()
    {
        var one = Write("a.ico", IcoFile.Write([Png.Encode(1, 1, [0, 0, 0, 255])]));
        var actions = ActionCatalog.ForFile(one, 1);
        Assert.True(actions[0].IsEnabled);
        Assert.Equal("В значке только один размер", actions[1].DisabledReason);
        var broken = ActionCatalog.ForFile(Write("b.ico", [9, 9, 9]), 3);
        Assert.All(broken, a => Assert.Equal("Не удалось прочитать значок", a.DisabledReason));
    }

    [Fact]
    public void ImageFormat_HasIco()
    {
        var ico = ActionCatalog.ForFile(@"C:\p\a.png", 1)[0].Children!.Single(c => c.Label == "ICO");
        Assert.Equal(ActionIds.ImageToIco, ico.Id);
        Assert.True(ico.IsEnabled);
        Assert.True(FileJobs.IsImplemented(ico.Id));
        Assert.True(FileJobs.NeedsFfmpeg(ico.Id));
    }

    [Fact]
    public void EveryNewAction_IsImplemented_FfmpegOnlyForGraphics()
    {
        var paths = new[] { @"C:\a.txt", @"C:\a.svg", @"C:\a.ico" };
        foreach (var action in paths.SelectMany(p => ActionCatalog.Flatten(ActionCatalog.ForFile(p, 1))))
        {
            Assert.True(FileJobs.IsImplemented(action.Id), action.Id);
            Assert.Equal(action.Id.StartsWith("svg-"), FileJobs.NeedsFfmpeg(action.Id));
            if (!FileJobs.NeedsFfmpeg(action.Id))
                Assert.NotNull(FileJobs.Create(action.Id, null));
        }
    }

    [Fact]
    public void Batch_EnabledIfAnyFileNeedsIt()
    {
        var a = Write("a.txt", "привет\n", new UTF8Encoding(false));
        var b = Write("b.json", "{\"a\":1}", Encoding.UTF8);
        var actions = ActionCatalog.ForBatch(FileKind.Text, [a, b], [1, 1]);
        Assert.Equal("Все файлы уже в UTF-8", Get(actions, ActionIds.TextToUtf8).DisabledReason);
        Assert.True(Get(actions, ActionIds.JsonFormat).IsEnabled);
        Assert.Equal("Только для таблиц CSV и TSV", Get(actions, ActionIds.CsvToXlsx).DisabledReason);
        Assert.Equal("Во всех файлах переводы строк уже LF", Get(actions, ActionIds.TextToLf).DisabledReason);
        Assert.True(Get(actions, ActionIds.TextToCrlf).IsEnabled);

        var c = Write("c.txt", Russian, Cp1251);
        Assert.True(Get(ActionCatalog.ForBatch(FileKind.Text, [a, c], [1, 1]), ActionIds.TextToUtf8).IsEnabled);
    }

    [Fact]
    public async Task Batch_SkipsFilesThatDoNotFit()
    {
        var json = Write("a.json", "{\"a\":1}", Encoding.UTF8);
        var txt = Write("b.txt", "x", Encoding.UTF8);
        var summary = await BatchRunner.RunAsync([json, txt], _ => FileJobs.Create(ActionIds.JsonFormat, null)!, null, null, null,
            CancellationToken.None);
        Assert.Equal(1, summary.Done);
        Assert.Equal(1, summary.Skipped);
    }

    private sealed class RealFile : IFileProbe
    {
        public FileState Probe(string path) => new(FileStateKind.File, new FileInfo(path).Length);
    }

    [Fact]
    public void Subtitles()
    {
        var text = Write("лог.txt", "раз\r\nдва\r\nтри\r\n", Cp1251);
        var menu = ((HotkeyOutcome.ShowMenu)SelectionInterpreter.Interpret(SelectionResult.Success([text]), new RealFile())).Menu;
        Assert.Equal("15 Б · 3 строки · Windows-1251", menu.Subtitle);
        Assert.Equal(DialMenuKind.Actions, menu.Kind);

        var ico = Write("app.ico", IcoFile.Write([Png.Encode(1, 1, [0, 0, 0, 255]), Png.Encode(2, 2, new byte[16])]));
        Assert.EndsWith("2 размера, до 2×2", TextFileCatalog.Subtitle(FileKind.Icon, ico, "1 КБ"));

        var svg = Write("logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 512 256\"/>", Encoding.UTF8);
        Assert.Equal("1 КБ · 512×256", TextFileCatalog.Subtitle(FileKind.Svg, svg, "1 КБ"));

        Assert.Equal("3 текстовых файла", SelectionInterpreter.KindCount(FileKind.Text, 3));
        Assert.Equal("2 значка", SelectionInterpreter.KindCount(FileKind.Icon, 2));
    }

    [Fact]
    public void English_Labels()
    {
        using var _ = new EnglishScope();
        var actions = ActionCatalog.ForFile(@"C:\нет\a.txt", 1);
        Assert.Equal(["To UTF-8", "Line endings", "To XLSX", "Format JSON", "Minify JSON", "To HTML", "To PDF"],
            actions.Select(a => a.Label));
        Assert.Equal("3 lines", Core.Localization.Lang.T.TxtLines(3));
        Assert.Equal("_formatted", Core.Localization.Lang.T.TxtJsonPrettySuffix);
    }
}

/// <summary>Тест пропускается, если в ffmpeg нет декодера SVG (librsvg).</summary>
public sealed class SvgFactAttribute : FactAttribute
{
    public SvgFactAttribute()
    {
        var tools = FfmpegLocator.LocateDefault();
        if (tools is null || !SvgSupport.DecoderAvailable(tools))
            Skip = "Нет ffmpeg с декодером SVG (librsvg)";
    }
}

[Collection(LanguageCollection.Name)]
public class SvgIntegrationTests : TextTestBase
{
    private const string Logo = """
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100"><rect width="200" height="100" fill="#3a7"/><circle cx="100" cy="50" r="40" fill="#fff"/></svg>
        """;

    [SvgFact]
    public async Task SvgToPng_ToIco_PngSet()
    {
        var tools = FfmpegLocator.LocateDefault()!;
        var svg = Write("логотип.svg", Logo, new UTF8Encoding(false));

        var png = await RunKeepingSource(FileJobs.Create(ActionIds.SvgToPng, tools)!, svg);
        Assert.Equal((1024, 512), Png.Size(File.ReadAllBytes(png.OutputPath!)));

        var ico = await RunKeepingSource(FileJobs.Create(ActionIds.SvgToIco, tools)!, svg);
        Assert.Equal(IcoFile.StandardSizes, IcoFile.Read(File.ReadAllBytes(ico.OutputPath!)).Select(f => f.Width));

        var set = await RunKeepingSource(FileJobs.Create(ActionIds.SvgPngSizes, tools)!, svg);
        Assert.Equal(8, Directory.GetFiles(set.OutputPath!).Length);
    }
}
