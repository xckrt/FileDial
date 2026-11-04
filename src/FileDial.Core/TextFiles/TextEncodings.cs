using System.Text;

namespace FileDial.Core.TextFiles;

/// <summary>Кодировка текстового файла, как её определил <see cref="TextEncodings.Detect"/>.</summary>
public enum TextEncodingKind
{
    /// <summary>Только символы до 0x7F — это одновременно и UTF-8.</summary>
    Ascii,
    Utf8,
    Utf8Bom,
    Utf16Le,
    Utf16Be,
    Utf32Le,
    Utf32Be,
    Windows1251,
    /// <summary>DOS-кириллица.</summary>
    Cp866,
    Windows1252,
    /// <summary>Нулевые байты без признаков UTF-16 — это не текст.</summary>
    Binary,
}

/// <summary>Стиль переводов строк в файле.</summary>
public enum LineEndingStyle
{
    /// <summary>Переводов строк нет — одна строка.</summary>
    None,
    Crlf,
    Lf,
    /// <summary>Только CR (старый Mac).</summary>
    Cr,
    Mixed,
}

/// <summary>Определение кодировки: BOM, UTF-16 без BOM, проверка UTF-8, затем эвристика для однобайтовых.</summary>
public static class TextEncodings
{
    static TextEncodings() => System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly byte[] Utf8BomBytes = [0xEF, 0xBB, 0xBF];

    public static bool IsUtf8(this TextEncodingKind kind) => kind is TextEncodingKind.Ascii or TextEncodingKind.Utf8 or TextEncodingKind.Utf8Bom;

    public static int BomLength(this TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8Bom => 3,
        TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be => 2,
        TextEncodingKind.Utf32Le or TextEncodingKind.Utf32Be => 4,
        _ => 0,
    };

    /// <summary>Размер кодовой единицы в байтах: переводы строк ищем по единицам, а не по байтам.</summary>
    public static int UnitSize(this TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf16Le or TextEncodingKind.Utf16Be => 2,
        TextEncodingKind.Utf32Le or TextEncodingKind.Utf32Be => 4,
        _ => 1,
    };

    public static bool IsBigEndian(this TextEncodingKind kind) => kind is TextEncodingKind.Utf16Be or TextEncodingKind.Utf32Be;

    /// <summary>Кодировка для чтения (без BOM — его пропускаем сами).</summary>
    public static Encoding Encoding(this TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf16Le => new UnicodeEncoding(false, false),
        TextEncodingKind.Utf16Be => new UnicodeEncoding(true, false),
        TextEncodingKind.Utf32Le => new UTF32Encoding(false, false),
        TextEncodingKind.Utf32Be => new UTF32Encoding(true, false),
        TextEncodingKind.Windows1251 => System.Text.Encoding.GetEncoding(1251),
        TextEncodingKind.Cp866 => System.Text.Encoding.GetEncoding(866),
        TextEncodingKind.Windows1252 => System.Text.Encoding.GetEncoding(1252),
        _ => new UTF8Encoding(false),
    };

    /// <summary>Название для подписи и итога: «UTF-8», «Windows-1251».</summary>
    public static string Label(this TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Ascii => "ASCII",
        TextEncodingKind.Utf8 => "UTF-8",
        TextEncodingKind.Utf8Bom => "UTF-8 BOM",
        TextEncodingKind.Utf16Le => "UTF-16 LE",
        TextEncodingKind.Utf16Be => "UTF-16 BE",
        TextEncodingKind.Utf32Le => "UTF-32 LE",
        TextEncodingKind.Utf32Be => "UTF-32 BE",
        TextEncodingKind.Windows1251 => "Windows-1251",
        TextEncodingKind.Cp866 => "CP866",
        TextEncodingKind.Windows1252 => "Windows-1252",
        _ => "?",
    };

    /// <param name="truncated">Байты — только начало файла: обрезанная в конце последовательность UTF-8 не ошибка.</param>
    public static TextEncodingKind Detect(ReadOnlySpan<byte> bytes, bool truncated = false)
    {
        if (bytes.StartsWith(Utf8BomBytes)) return TextEncodingKind.Utf8Bom;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0, 0])) return TextEncodingKind.Utf32Le;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0, 0, 0xFE, 0xFF])) return TextEncodingKind.Utf32Be;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE])) return TextEncodingKind.Utf16Le;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])) return TextEncodingKind.Utf16Be;

        if (GuessUtf16(bytes) is { } utf16) return utf16;
        if (bytes.Contains((byte)0)) return TextEncodingKind.Binary;
        if (!HasHighBytes(bytes)) return TextEncodingKind.Ascii;
        if (IsValidUtf8(bytes, truncated)) return TextEncodingKind.Utf8;
        return GuessSingleByte(bytes);
    }

    private static bool HasHighBytes(ReadOnlySpan<byte> bytes) => bytes.IndexOfAnyInRange((byte)0x80, (byte)0xFF) >= 0;

    /// <summary>UTF-16 без BOM: латиница и цифры дают ноль в каждом втором байте.</summary>
    private static TextEncodingKind? GuessUtf16(ReadOnlySpan<byte> bytes)
    {
        var pairs = Math.Min(bytes.Length, 8192) / 2;
        if (pairs < 2) return null;
        int zeroEven = 0, zeroOdd = 0;
        for (var i = 0; i < pairs; i++)
        {
            if (bytes[2 * i] == 0) zeroEven++;
            if (bytes[2 * i + 1] == 0) zeroOdd++;
        }
        if (zeroOdd > pairs * 0.4 && zeroEven < pairs * 0.05) return TextEncodingKind.Utf16Le;
        if (zeroEven > pairs * 0.4 && zeroOdd < pairs * 0.05) return TextEncodingKind.Utf16Be;
        return null;
    }

    public static bool IsValidUtf8(ReadOnlySpan<byte> bytes, bool truncated = false)
    {
        var i = 0;
        while (i < bytes.Length)
        {
            var b = bytes[i];
            if (b < 0x80) { i++; continue; }
            int need;
            int min;
            if (b is >= 0xC2 and <= 0xDF) { need = 1; min = 0x80; }
            else if (b is >= 0xE0 and <= 0xEF) { need = 2; min = 0x800; }
            else if (b is >= 0xF0 and <= 0xF4) { need = 3; min = 0x10000; }
            else return false;

            if (i + need >= bytes.Length)
            {
                if (!truncated) return false;
                // Последовательность обрезана концом выборки — проверяем то, что есть.
                for (var k = i + 1; k < bytes.Length; k++)
                    if ((bytes[k] & 0xC0) != 0x80) return false;
                return true;
            }

            var code = b & (0x3F >> need);
            for (var k = 1; k <= need; k++)
            {
                var c = bytes[i + k];
                if ((c & 0xC0) != 0x80) return false;
                code = (code << 6) | (c & 0x3F);
            }
            if (code < min || code > 0x10FFFF || code is >= 0xD800 and <= 0xDFFF) return false;
            i += need + 1;
        }
        return true;
    }

    /// <summary>
    /// Однобайтовая кодировка. В кириллице слова целиком из «высоких» байтов, в западноевропейском тексте
    /// они одиночные среди латиницы — так отличаем Windows-1252. Между 1251 и 866 выбираем по частотам букв.
    /// </summary>
    private static TextEncodingKind GuessSingleByte(ReadOnlySpan<byte> bytes)
    {
        int high = 0, paired = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] < 0x80) continue;
            high++;
            if (i > 0 && bytes[i - 1] >= 0x80 || i + 1 < bytes.Length && bytes[i + 1] >= 0x80)
                paired++;
        }
        if (paired < high * 0.5)
            return TextEncodingKind.Windows1252;

        var sample = bytes.Length > 65536 ? bytes[..65536] : bytes;
        var cp1251 = RussianScore(TextEncodingKind.Windows1251.Encoding().GetString(sample));
        var cp866 = RussianScore(TextEncodingKind.Cp866.Encoding().GetString(sample));
        return cp866 > cp1251 ? TextEncodingKind.Cp866 : TextEncodingKind.Windows1251;
    }

    private const string RussianLetters = "оеаинтсрвлкмдпуяыьгзбчйхжшюцщэфъё";

    /// <summary>Частоты букв русского текста, %, в порядке <see cref="RussianLetters"/>.</summary>
    private static readonly double[] Frequencies =
        [10.97, 8.45, 8.01, 7.35, 6.70, 6.26, 5.47, 4.73, 4.54, 4.40, 3.49, 3.21, 2.98, 2.81, 2.62, 2.01, 1.90, 1.74,
            1.70, 1.65, 1.59, 1.44, 1.21, 0.97, 0.94, 0.73, 0.64, 0.48, 0.36, 0.32, 0.26, 0.04, 0.04];

    /// <summary>Похожесть на русский текст: частые буквы — плюс, псевдографика и редкие символы — минус.</summary>
    private static double RussianScore(string text)
    {
        double score = 0;
        foreach (var ch in text)
        {
            if (ch < 0x80) continue;
            var index = RussianLetters.IndexOf(char.ToLowerInvariant(ch));
            if (index >= 0)
                score += Frequencies[index];
            else if (ch is '«' or '»' or '—' or '–' or '…' or '№' or ' ' or '“' or '”' or '„')
                continue;
            else
                score -= 3;
        }
        return score;
    }
}
