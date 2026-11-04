using System.Text;

namespace FileDial.Core.TextFiles;

/// <summary>Разбор CSV/TSV по RFC 4180: кавычки, удвоенные кавычки, переводы строк внутри кавычек.</summary>
public static class Csv
{
    private static readonly char[] Candidates = [',', ';', '\t'];

    /// <summary>
    /// Разделитель по первым строкам: тот, который встречается вне кавычек и даёт одинаковое число столбцов.
    /// Для .tsv по умолчанию — табуляция, иначе — запятая.
    /// </summary>
    public static char DetectDelimiter(string text, string? extension = null)
    {
        var fallback = string.Equals(extension, ".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        var sample = text.Length > 65536 ? text[..65536] : text;
        var best = fallback;
        var bestScore = 0.0;
        foreach (var delimiter in Candidates)
        {
            var counts = Parse(sample, delimiter).Take(20).Select(r => r.Count).ToList();
            // Последняя строка выборки могла обрезаться.
            if (sample.Length < text.Length && counts.Count > 1)
                counts.RemoveAt(counts.Count - 1);
            if (counts.Count == 0 || counts.Max() < 2)
                continue;
            var first = counts[0];
            var consistent = counts.Count(c => c == first) / (double)counts.Count;
            // Совпадающее число столбцов важнее их количества.
            var score = consistent * 10 + Math.Min(first, 50) / 50.0;
            if (score > bestScore + 1e-9 || Math.Abs(score - bestScore) < 1e-9 && delimiter == fallback)
            {
                best = delimiter;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>Строки таблицы. Пустые строки файла пропускаются.</summary>
    public static IEnumerable<List<string>> Parse(string text, char delimiter)
    {
        var field = new StringBuilder();
        var row = new List<string>();
        var quoted = false;
        var fieldStarted = false;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }
                    quoted = false;
                    i++;
                    continue;
                }
                field.Append(c);
                i++;
                continue;
            }

            if (c == '"' && field.Length == 0 && !fieldStarted)
            {
                quoted = true;
                fieldStarted = true;
                i++;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
                i++;
            }
            else if (c is '\r' or '\n')
            {
                // CRLF, LF или CR — конец записи.
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                if (row.Count > 0 || field.Length > 0 || fieldStarted)
                {
                    row.Add(field.ToString());
                    yield return row;
                }
                row = [];
                field.Clear();
                fieldStarted = false;
            }
            else
            {
                // Текст после закрывающей кавычки ("a"b) берём как есть — лучше, чем потерять.
                field.Append(c);
                fieldStarted = true;
                i++;
            }
        }
        if (row.Count > 0 || field.Length > 0 || fieldStarted)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }
}
