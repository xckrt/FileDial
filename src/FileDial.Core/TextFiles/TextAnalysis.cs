namespace FileDial.Core.TextFiles;

/// <summary>Сколько каких переводов строк в файле.</summary>
public readonly record struct LineEndingCounts(int Crlf, int Lf, int Cr)
{
    public int Total => Crlf + Lf + Cr;

    public LineEndingStyle Style => (Crlf, Lf, Cr) switch
    {
        (0, 0, 0) => LineEndingStyle.None,
        (> 0, 0, 0) => LineEndingStyle.Crlf,
        (0, > 0, 0) => LineEndingStyle.Lf,
        (0, 0, > 0) => LineEndingStyle.Cr,
        _ => LineEndingStyle.Mixed,
    };
}

/// <summary>Что известно о текстовом файле: кодировка, переводы строк, число строк.</summary>
public sealed record TextAnalysis(TextEncodingKind Encoding, LineEndingCounts LineEndings, int Lines, bool Complete)
{
    /// <summary>Для круга читаем только начало файла: этого хватает и для кодировки, и для переводов строк.</summary>
    public const int SampleBytes = 4 * 1024 * 1024;

    /// <summary>Разбор начала файла; null — файл не прочитать (нет, занят, нет доступа).</summary>
    public static TextAnalysis? TryRead(string path, int sampleBytes = SampleBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(stream.Length, sampleBytes);
            var buffer = new byte[length];
            stream.ReadExactly(buffer);
            return Of(buffer, complete: stream.Length <= sampleBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static TextAnalysis Of(ReadOnlySpan<byte> bytes, bool complete = true)
    {
        var encoding = TextEncodings.Detect(bytes, truncated: !complete);
        if (encoding == TextEncodingKind.Binary)
            return new TextAnalysis(encoding, default, 0, complete);
        var counts = LineBreaks.Count(bytes, encoding);
        var body = bytes.Length - encoding.BomLength();
        // Последняя строка без перевода в конце — тоже строка.
        var lines = body <= 0 ? 0 : counts.Total + (LineBreaks.EndsWithBreak(bytes, encoding) ? 0 : 1);
        return new TextAnalysis(encoding, counts, lines, complete);
    }
}

/// <summary>
/// Переводы строк на уровне кодовых единиц (байт, 2 байта UTF-16, 4 байта UTF-32): без перекодирования,
/// поэтому остальной текст остаётся байт в байт тем же.
/// </summary>
public static class LineBreaks
{
    private const int Cr = 0x0D;
    private const int Lf = 0x0A;

    private static int Unit(ReadOnlySpan<byte> bytes, int offset, int size, bool bigEndian) => size switch
    {
        1 => bytes[offset],
        2 => bigEndian ? bytes[offset] << 8 | bytes[offset + 1] : bytes[offset] | bytes[offset + 1] << 8,
        _ => bigEndian
            ? bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]
            : bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24,
    };

    public static LineEndingCounts Count(ReadOnlySpan<byte> bytes, TextEncodingKind encoding)
    {
        var size = encoding.UnitSize();
        var big = encoding.IsBigEndian();
        int crlf = 0, lf = 0, cr = 0;
        for (var i = encoding.BomLength(); i + size <= bytes.Length; i += size)
        {
            var unit = Unit(bytes, i, size, big);
            if (unit == Lf)
            {
                lf++;
            }
            else if (unit == Cr)
            {
                if (i + 2 * size <= bytes.Length && Unit(bytes, i + size, size, big) == Lf)
                {
                    crlf++;
                    i += size;
                }
                else
                {
                    cr++;
                }
            }
        }
        return new LineEndingCounts(crlf, lf, cr);
    }

    public static bool EndsWithBreak(ReadOnlySpan<byte> bytes, TextEncodingKind encoding)
    {
        var size = encoding.UnitSize();
        var body = bytes.Length - encoding.BomLength();
        if (body < size) return false;
        var last = Unit(bytes, encoding.BomLength() + (body / size - 1) * size, size, encoding.IsBigEndian());
        return last is Cr or Lf;
    }

    /// <summary>Все переводы строк (CRLF, LF, одиночный CR) — в <paramref name="target"/>; BOM сохраняется.</summary>
    public static byte[] Convert(ReadOnlySpan<byte> bytes, TextEncodingKind encoding, LineEndingStyle target)
    {
        if (target is not (LineEndingStyle.Crlf or LineEndingStyle.Lf))
            throw new ArgumentOutOfRangeException(nameof(target));
        var size = encoding.UnitSize();
        var big = encoding.IsBigEndian();
        var bom = encoding.BomLength();

        var output = new MemoryStream(bytes.Length + bytes.Length / 16 + 16);
        output.Write(bytes[..bom]);
        Span<byte> cr = stackalloc byte[size];
        Span<byte> lf = stackalloc byte[size];
        cr[big ? size - 1 : 0] = Cr;
        lf[big ? size - 1 : 0] = Lf;

        var i = bom;
        for (; i + size <= bytes.Length; i += size)
        {
            var unit = Unit(bytes, i, size, big);
            if (unit is not (Cr or Lf))
            {
                output.Write(bytes.Slice(i, size));
                continue;
            }
            if (unit == Cr && i + 2 * size <= bytes.Length && Unit(bytes, i + size, size, big) == Lf)
                i += size;
            if (target == LineEndingStyle.Crlf)
                output.Write(cr);
            output.Write(lf);
        }
        // Неполная кодовая единица в конце (битый файл) — переносим как есть.
        output.Write(bytes[i..]);
        return output.ToArray();
    }
}
