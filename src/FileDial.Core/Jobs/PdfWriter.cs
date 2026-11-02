using System.Globalization;
using System.Text;

namespace FileDial.Core.Jobs;

/// <summary>Страница PDF с одной JPEG-картинкой (RGB, 8 бит на канал).</summary>
public sealed record PdfImagePage(byte[] Jpeg, int Width, int Height);

/// <summary>
/// Минимальный писатель PDF 1.4: по одной картинке на страницу, JPEG вставляется как есть (DCTDecode),
/// без перекодирования. Страница — размер картинки при 96 dpi. Без сторонних библиотек.
/// </summary>
public static class PdfWriter
{
    /// <summary>Пункты PDF (1/72 дюйма) на пиксель при 96 dpi.</summary>
    public const double PointsPerPixel = 72.0 / 96.0;

    public static void Write(Stream output, IReadOnlyList<PdfImagePage> pages)
    {
        if (pages.Count == 0)
            throw new ArgumentException("Нужна хотя бы одна страница.", nameof(pages));

        var w = new Writer(output);
        w.Raw("%PDF-1.4\n%âãÏÓ\n"); // двоичная метка: файл не текстовый

        // Объекты: 1 — каталог, 2 — дерево страниц, затем по три на страницу: страница, содержимое, картинка.
        const int catalogId = 1, pagesId = 2;
        int PageId(int i) => 3 + i * 3;

        w.Object(catalogId, $"<< /Type /Catalog /Pages {pagesId} 0 R >>");
        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{PageId(i)} 0 R"));
        w.Object(pagesId, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var pageId = PageId(i);
            var contentId = pageId + 1;
            var imageId = pageId + 2;
            var width = Num(page.Width * PointsPerPixel);
            var height = Num(page.Height * PointsPerPixel);

            w.Object(pageId,
                $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {width} {height}] " +
                $"/Resources << /XObject << /Im0 {imageId} 0 R >> >> /Contents {contentId} 0 R >>");

            var content = Encoding.ASCII.GetBytes($"q {width} 0 0 {height} 0 0 cm /Im0 Do Q\n");
            w.StreamObject(contentId, $"<< /Length {content.Length} >>", content);
            w.StreamObject(imageId,
                $"<< /Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} " +
                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {page.Jpeg.Length} >>",
                page.Jpeg);
        }

        w.Trailer(catalogId);
    }

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Пишет объекты и запоминает их смещения для таблицы xref.</summary>
    private sealed class Writer(Stream stream)
    {
        private readonly SortedDictionary<int, long> _offsets = [];
        private readonly long _start = stream.Position;

        public void Raw(string text)
        {
            // Latin-1: байты 0–255 один в один (для двоичной метки в заголовке).
            var bytes = Encoding.Latin1.GetBytes(text);
            stream.Write(bytes);
        }

        public void Object(int id, string body)
        {
            _offsets[id] = stream.Position - _start;
            Raw($"{id} 0 obj\n{body}\nendobj\n");
        }

        public void StreamObject(int id, string dictionary, byte[] data)
        {
            _offsets[id] = stream.Position - _start;
            Raw($"{id} 0 obj\n{dictionary}\nstream\n");
            stream.Write(data);
            Raw("\nendstream\nendobj\n");
        }

        public void Trailer(int rootId)
        {
            var xref = stream.Position - _start;
            var count = _offsets.Count + 1;
            var sb = new StringBuilder();
            sb.Append($"xref\n0 {count}\n");
            sb.Append("0000000000 65535 f \n");
            foreach (var offset in _offsets.Values)
                sb.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            sb.Append($"trailer\n<< /Size {count} /Root {rootId} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            Raw(sb.ToString());
        }
    }
}
