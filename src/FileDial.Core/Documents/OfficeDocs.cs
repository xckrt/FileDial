using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FileDial.Core.Compression;

namespace FileDial.Core.Documents;

/// <summary>Вид документа: от него зависят форматы и действия.</summary>
public enum DocumentFamily
{
    Text,
    Sheet,
    Slides,
}

/// <summary>
/// Документы Office и OpenDocument. Новые форматы (DOCX, XLSX, PPTX, ODT, ODS, ODP) — это zip с XML внутри:
/// текст, картинки и сведения об авторе читаются напрямую. Старые (DOC, XLS, PPT, RTF) — только через конвертер.
/// </summary>
public static class OfficeDocs
{
    public const string PackageOnly = "Только для DOCX, XLSX, PPTX и OpenDocument";
    public const string Anonymous = "Автор";

    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".doc", ".rtf", ".odt", ".xlsx", ".xls", ".ods", ".pptx", ".ppt", ".odp",
    };

    public static DocumentFamily FamilyOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".xlsx" or ".xls" or ".ods" => DocumentFamily.Sheet,
        ".pptx" or ".ppt" or ".odp" => DocumentFamily.Slides,
        _ => DocumentFamily.Text,
    };

    public static bool IsOoxml(string path) => Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx";

    public static bool IsOpenDocument(string path) => Path.GetExtension(path).ToLowerInvariant() is ".odt" or ".ods" or ".odp";

    /// <summary>Zip-контейнер с XML (новые форматы Office и OpenDocument).</summary>
    public static bool IsPackage(string path) => IsOoxml(path) || IsOpenDocument(path);

    /// <summary>Форматы Office (для пароля через Word, Excel, PowerPoint).</summary>
    public static bool IsMicrosoftFormat(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".doc" or ".rtf" or ".xlsx" or ".xls" or ".pptx" or ".ppt";

    internal static ZipArchive OpenPackage(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            // Документ Office под паролем — это не zip, а зашифрованный контейнер.
            throw new CompressionException($"Не удалось прочитать {Path.GetFileName(path)}: документ защищён паролем или повреждён.");
        }
    }

    // --- Текст ---

    /// <summary>Текст документа по абзацам; у презентации — по слайдам.</summary>
    public static string ExtractText(string path)
    {
        using var zip = OpenPackage(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = new StringBuilder();
        switch (ext)
        {
            case ".docx":
                AppendWordText(Load(zip, "word/document.xml"), text);
                break;
            case ".pptx":
                var slides = zip.Entries
                    .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
                    .OrderBy(e => SlideNumber(e.FullName))
                    .ToList();
                for (var i = 0; i < slides.Count; i++)
                {
                    if (i > 0)
                        text.AppendLine();
                    text.AppendLine($"— Слайд {i + 1} —");
                    using var stream = slides[i].Open();
                    AppendDrawingText(XDocument.Load(stream), text);
                }
                break;
            case ".odt" or ".odp":
                AppendOpenDocumentText(Load(zip, "content.xml"), text, ext == ".odp");
                break;
            default:
                throw new CompressionException("Текст из этого формата напрямую не читается.");
        }
        return text.ToString().TrimEnd() + Environment.NewLine;
    }

    private static int SlideNumber(string name)
    {
        var digits = new string(Path.GetFileNameWithoutExtension(name).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : int.MaxValue;
    }

    private static XDocument Load(ZipArchive zip, string entry)
    {
        var e = zip.GetEntry(entry) ?? throw new CompressionException("Документ повреждён: нет основной части.");
        using var stream = e.Open();
        return XDocument.Load(stream);
    }

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace OText = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace ODraw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    private static void AppendWordText(XDocument doc, StringBuilder text)
    {
        // Абзацы внутри таблиц и надписей тоже w:p — каждый становится строкой.
        foreach (var paragraph in doc.Descendants(W + "p"))
        {
            // Вложенные абзацы (надпись внутри абзаца) выводятся отдельно — здесь их пропускаем.
            foreach (var node in paragraph.Descendants().Where(d => d.Ancestors(W + "p").First() == paragraph))
            {
                if (node.Name == W + "t")
                    text.Append(node.Value);
                else if (node.Name == W + "tab")
                    text.Append('\t');
                else if (node.Name == W + "br" || node.Name == W + "cr")
                    text.AppendLine();
            }
            text.AppendLine();
        }
    }

    private static void AppendDrawingText(XDocument slide, StringBuilder text)
    {
        foreach (var paragraph in slide.Descendants(A + "p"))
        {
            var line = string.Concat(paragraph.Descendants().Select(d =>
                d.Name == A + "t" ? d.Value : d.Name == A + "br" ? Environment.NewLine : ""));
            if (line.Length > 0)
                text.AppendLine(line);
        }
    }

    private static void AppendOpenDocumentText(XDocument content, StringBuilder text, bool slides)
    {
        var pages = slides ? content.Descendants(ODraw + "page").Cast<XContainer>().ToList() : [content];
        for (var i = 0; i < pages.Count; i++)
        {
            if (slides)
            {
                if (i > 0)
                    text.AppendLine();
                text.AppendLine($"— Слайд {i + 1} —");
            }
            foreach (var paragraph in pages[i].Descendants().Where(e => e.Name == OText + "p" || e.Name == OText + "h"))
            {
                if (paragraph.Ancestors().Any(a => a.Name == OText + "p" || a.Name == OText + "h"))
                    continue;
                AppendOpenDocumentNodes(paragraph, text);
                text.AppendLine();
            }
        }
    }

    private static void AppendOpenDocumentNodes(XElement element, StringBuilder text)
    {
        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText t:
                    text.Append(t.Value);
                    break;
                case XElement e when e.Name == OText + "s":
                    text.Append(' ', int.TryParse((string?)e.Attribute(OText + "c"), out var c) ? c : 1);
                    break;
                case XElement e when e.Name == OText + "tab":
                    text.Append('\t');
                    break;
                case XElement e when e.Name == OText + "line-break":
                    text.AppendLine();
                    break;
                // Сноски и примечания — не часть основного текста.
                case XElement e when e.Name == OText + "note" || e.Name.LocalName == "annotation":
                    break;
                case XElement e:
                    AppendOpenDocumentNodes(e, text);
                    break;
            }
        }
    }

    // --- Картинки ---

    /// <summary>Картинки из документа (как они лежат внутри): имя внутри документа → содержимое.</summary>
    public static IEnumerable<ZipArchiveEntry> ImageEntries(ZipArchive zip) =>
        zip.Entries.Where(e => e.Length > 0 && IsMediaPath(e.FullName));

    private static bool IsMediaPath(string name) =>
        name.StartsWith("word/media/", StringComparison.Ordinal) ||
        name.StartsWith("xl/media/", StringComparison.Ordinal) ||
        name.StartsWith("ppt/media/", StringComparison.Ordinal) ||
        name.StartsWith("Pictures/", StringComparison.Ordinal);

    // --- Личные данные ---

    /// <summary>Что убрано из документа (для итогового сообщения).</summary>
    public sealed record StripReport(bool Properties, int Comments, int Revisions, int Links);

    /// <summary>
    /// Копия пакета без имён людей: автор и «кем изменён», компания и руководитель в свойствах,
    /// авторы комментариев и исправлений («Автор» вместо имени), пути к шаблонам на диске.
    /// </summary>
    public static StripReport StripPersonalData(string source, string target)
    {
        var properties = false;
        int comments = 0, revisions = 0, links = 0;
        RewritePackage(source, target, (name, read) =>
        {
            if (!name.EndsWith(".xml", StringComparison.Ordinal) && !name.EndsWith(".rels", StringComparison.Ordinal))
                return null;
            var doc = LoadXml(read);
            if (doc is null)
                return null;
            var changed = false;

            switch (name)
            {
                case "docProps/core.xml":
                    foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "creator" or "lastModifiedBy").ToList())
                        changed |= Clear(e);
                    foreach (var e in doc.Descendants().Where(e => e.Name.LocalName == "lastPrinted").ToList())
                    {
                        e.Remove();
                        changed = true;
                    }
                    properties |= changed;
                    break;
                case "docProps/app.xml":
                    foreach (var e in doc.Descendants().Where(e => e.Name.LocalName is "Company" or "Manager").ToList())
                        changed |= Clear(e);
                    properties |= changed;
                    break;
                case "meta.xml":
                    // OpenDocument: автор, «кем изменён», «кем напечатан» — убираем целиком.
                    foreach (var e in doc.Descendants().Where(e =>
                                 e.Name == Dc + "creator" || e.Name.LocalName is "initial-creator" or "printed-by").ToList())
                    {
                        e.Remove();
                        changed = true;
                    }
                    properties |= changed;
                    break;
                default:
                    if (name.EndsWith(".rels", StringComparison.Ordinal))
                    {
                        // Ссылка на шаблон вида file:///C:/Users/Имя/... — выдаёт имя пользователя и папки.
                        foreach (var rel in doc.Descendants().Where(e => e.Name.LocalName == "Relationship" &&
                                     (string?)e.Attribute("TargetMode") == "External" &&
                                     ((string?)e.Attribute("Target"))?.StartsWith("file:", StringComparison.OrdinalIgnoreCase) == true))
                        {
                            var targetPath = (string)rel.Attribute("Target")!;
                            rel.SetAttributeValue("Target", Uri.UnescapeDataString(targetPath.Split('/', '\\').Last()));
                            links++;
                            changed = true;
                        }
                        break;
                    }
                    changed |= AnonymizeAuthors(name, doc, ref comments, ref revisions);
                    break;
            }
            return changed ? Save(doc) : null;
        });
        return new StripReport(properties, comments, revisions, links);
    }

    private static bool AnonymizeAuthors(string name, XDocument doc, ref int comments, ref int revisions)
    {
        var changed = false;
        var isComments = name.Contains("comment", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("people", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("person", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("authors", StringComparison.OrdinalIgnoreCase);

        foreach (var element in doc.Descendants().ToList())
        {
            // Word: учётная запись автора комментариев (почта) — необязательная часть, убираем целиком.
            if (element.Name.LocalName == "presenceInfo")
            {
                element.Remove();
                changed = true;
                continue;
            }
            // PowerPoint и Excel: учётная запись у автора и участника.
            if (element.Name.LocalName is "author" or "person")
                foreach (var account in element.Attributes().Where(a => a.Name.LocalName is "userId" or "providerId" && a.Value.Length > 0))
                {
                    account.Value = "";
                    changed = true;
                }

            // Word: w:author и w:initials у комментариев и исправлений.
            foreach (var attribute in element.Attributes().Where(a => a.Name.LocalName is "author" or "initials").ToList())
            {
                var replacement = attribute.Name.LocalName == "author" ? Anonymous : "А";
                if (attribute.Value == replacement)
                    continue;
                attribute.Value = replacement;
                changed = true;
                if (attribute.Name.LocalName == "author")
                {
                    if (isComments || element.Name.LocalName == "comment") comments++;
                    else revisions++;
                }
            }

            switch (element.Name.LocalName)
            {
                // Excel: <authors><author>Имя</author></authors>; PowerPoint: commentAuthors/authors; Excel: persons.
                case "cmAuthor" or "author" or "person" when element.Attribute("name") is { } n:
                    if (n.Value != Anonymous)
                    {
                        n.Value = Anonymous;
                        if (element.Attribute("initials") is { } ini) ini.Value = "А";
                        changed = true;
                    }
                    break;
                case "author" when !element.HasAttributes && !element.HasElements && element.Value != Anonymous:
                    element.Value = Anonymous;
                    comments++;
                    changed = true;
                    break;
                case "person" when element.Attribute("displayName") is { } d && d.Value != Anonymous:
                    d.Value = Anonymous;
                    changed = true;
                    break;
            }

            // OpenDocument: dc:creator у комментариев (office:annotation) и исправлений (office:change-info).
            if (element.Name == Dc + "creator" && element.Value != Anonymous)
            {
                if (element.Parent?.Name.LocalName == "annotation") comments++;
                else revisions++;
                element.Value = Anonymous;
                changed = true;
            }
        }
        return changed;
    }

    private static bool Clear(XElement element)
    {
        if (element.IsEmpty || element.Value.Length == 0)
            return false;
        element.Value = "";
        return true;
    }

    // --- Переписывание пакета ---

    /// <summary>
    /// Копирует zip-пакет по одной части в том же порядке; <paramref name="replace"/> может вернуть новое содержимое части
    /// (null — оставить как есть). У OpenDocument первая часть «mimetype» остаётся несжатой — так требует формат.
    /// </summary>
    public static void RewritePackage(string source, string target, Func<string, Func<Stream>, byte[]?> replace)
    {
        using var input = OpenPackage(source);
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);
        foreach (var entry in input.Entries)
        {
            var replacement = replace(entry.FullName, entry.Open);
            var level = entry.FullName == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
            var copy = zip.CreateEntry(entry.FullName, level);
            copy.LastWriteTime = entry.LastWriteTime;
            using var to = copy.Open();
            if (replacement is not null)
            {
                to.Write(replacement);
            }
            else
            {
                using var from = entry.Open();
                from.CopyTo(to);
            }
        }
    }

    private static XDocument? LoadXml(Func<Stream> read)
    {
        try
        {
            using var stream = read();
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static byte[] Save(XDocument doc)
    {
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
               {
                   Encoding = new UTF8Encoding(false),
                   Indent = false,
               }))
            doc.Save(writer);
        return buffer.ToArray();
    }
}
