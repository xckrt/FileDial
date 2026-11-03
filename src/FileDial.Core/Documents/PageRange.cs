using System.Globalization;

namespace FileDial.Core.Documents;

/// <summary>Номера страниц в записи «1-3, 5, 8-»: диапазоны, отдельные страницы, «до конца».</summary>
public static class PageRange
{
    /// <summary>
    /// Страницы (с единицы) в порядке записи, без повторов. null — запись неверна, причина в <paramref name="error"/>.
    /// </summary>
    public static IReadOnlyList<int>? TryParse(string text, int pageCount, out string? error)
    {
        error = null;
        var pages = new List<int>();
        var seen = new HashSet<int>();
        var parts = text.Replace(';', ',').Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "Укажите страницы, например 1-3, 5";
            return null;
        }

        foreach (var part in parts)
        {
            // Тире любого вида: «1–3» из Word тоже годится.
            var normalized = part.Replace('–', '-').Replace('—', '-').Replace(" ", "");
            int from, to;
            var dash = normalized.IndexOf('-');
            if (dash < 0)
            {
                if (!TryNumber(normalized, out from))
                    return Fail(out error, $"Непонятно: «{part}»");
                to = from;
            }
            else
            {
                var left = normalized[..dash];
                var right = normalized[(dash + 1)..];
                if (!TryNumber(left.Length == 0 ? "1" : left, out from) ||
                    !TryNumber(right.Length == 0 ? pageCount.ToString(CultureInfo.InvariantCulture) : right, out to))
                    return Fail(out error, $"Непонятно: «{part}»");
            }

            if (from < 1 || to < 1)
                return Fail(out error, "Страницы считаются с 1");
            if (from > pageCount || to > pageCount)
                return Fail(out error, $"В документе {Text.Plural.Count(pageCount, "страница", "страницы", "страниц")}");
            if (from > to)
                return Fail(out error, $"«{part}»: начало больше конца");

            for (var p = from; p <= to; p++)
                if (seen.Add(p))
                    pages.Add(p);
        }
        return pages;
    }

    /// <summary>Короткая запись для имени файла: 1-3,5,8.</summary>
    public static string Label(IReadOnlyList<int> pages)
    {
        var chunks = new List<string>();
        for (var i = 0; i < pages.Count;)
        {
            var j = i;
            while (j + 1 < pages.Count && pages[j + 1] == pages[j] + 1)
                j++;
            chunks.Add(j > i ? $"{pages[i]}-{pages[j]}" : pages[i].ToString(CultureInfo.InvariantCulture));
            i = j + 1;
        }
        var label = string.Join(",", chunks);
        return label.Length <= 40 ? label : label[..40].TrimEnd(',', '-') + "…";
    }

    private static bool TryNumber(string s, out int value) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static IReadOnlyList<int>? Fail(out string? error, string message)
    {
        error = message;
        return null;
    }
}
