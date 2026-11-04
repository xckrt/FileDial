using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FileDial.Core.TextFiles;

/// <summary>
/// Markdown → HTML без сторонних библиотек: заголовки, абзацы, списки (вложенные), цитаты, код (```, отступ),
/// таблицы GFM, ссылки, картинки, выделение, зачёркивание, линии. Встроенный HTML экранируется —
/// результат безопасно открывать в браузере.
/// </summary>
public sealed partial class Markdown
{
    /// <summary>Переписывает относительные адреса (картинки и ссылки), если HTML лежит не рядом с .md.</summary>
    private readonly Func<string, string>? _resolve;
    private readonly HashSet<string> _ids = [];

    private Markdown(Func<string, string>? resolve) => _resolve = resolve;

    /// <param name="baseDirectory">Папка исходного .md — для абсолютных путей к картинкам; null — оставить как есть.</param>
    public static string ToHtml(string markdown, string? baseDirectory = null)
    {
        Func<string, string>? resolve = baseDirectory is null ? null : url => Resolve(url, baseDirectory);
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(ExpandTabs).ToList();
        var html = new StringBuilder();
        new Markdown(resolve).Blocks(lines, html, tight: false);
        return html.ToString();
    }

    /// <summary>Первый заголовок — для &lt;title&gt;.</summary>
    public static string? FirstHeading(string markdown)
    {
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
            if (AtxHeading().Match(line) is { Success: true } m)
                return m.Groups[2].Value.Trim();
        return null;
    }

    private static string ExpandTabs(string line)
    {
        if (!line.Contains('\t')) return line;
        var sb = new StringBuilder();
        foreach (var c in line)
        {
            if (c == '\t') sb.Append(' ', 4 - sb.Length % 4);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^ {0,3}(```+|~~~+)\s*([^`\s]*)")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^ {0,3}(#{1,6})(?:\s+(.*?))?(?:\s+#+)?\s*$")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"^ {0,3}([-*_])(?:\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^ {0,3}(=+|-+)\s*$")]
    private static partial Regex SetextUnderline();

    [GeneratedRegex(@"^( {0,3})([-*+]|\d{1,9}[.)])(\s+|$)(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$")]
    private static partial Regex TableDelimiter();

    private static bool Blank(string line) => line.Trim().Length == 0;

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private void Blocks(List<string> lines, StringBuilder html, bool tight)
    {
        var paragraph = new List<string>();
        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var text = Inline(string.Join("\n", paragraph).Trim());
            html.Append(tight ? text + "\n" : $"<p>{text}</p>\n");
            paragraph.Clear();
        }

        var i = 0;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (Blank(line))
            {
                FlushParagraph();
                i++;
                continue;
            }

            if (Fence().Match(line) is { Success: true } fence)
            {
                FlushParagraph();
                var marker = fence.Groups[1].Value;
                var language = fence.Groups[2].Value;
                var indent = Indent(line);
                var code = new List<string>();
                i++;
                while (i < lines.Count && !(lines[i].TrimStart().StartsWith(marker[..3], StringComparison.Ordinal)
                                            && lines[i].Trim().All(c => c == marker[0])
                                            && lines[i].Trim().Length >= marker.Length))
                {
                    var l = lines[i];
                    code.Add(l[Math.Min(indent, Indent(l))..]);
                    i++;
                }
                i++; // закрывающая ограда (или конец файла)
                var cls = language.Length > 0 ? $" class=\"language-{Attr(language)}\"" : "";
                html.Append($"<pre><code{cls}>{Escape(string.Join("\n", code))}</code></pre>\n");
                continue;
            }

            if (AtxHeading().Match(line) is { Success: true } atx)
            {
                FlushParagraph();
                Heading(html, atx.Groups[1].Length, atx.Groups[2].Value.Trim());
                i++;
                continue;
            }

            // Строка из = или - под абзацем — заголовок (setext); проверяем раньше линии «---».
            if (paragraph.Count > 0 && SetextUnderline().Match(line) is { Success: true } setext)
            {
                var text = string.Join(" ", paragraph.Select(p => p.Trim()));
                paragraph.Clear();
                Heading(html, setext.Groups[1].Value[0] == '=' ? 1 : 2, text);
                i++;
                continue;
            }

            if (Rule().IsMatch(line))
            {
                FlushParagraph();
                html.Append("<hr>\n");
                i++;
                continue;
            }

            if (line.TrimStart().StartsWith('>') && Indent(line) < 4)
            {
                FlushParagraph();
                var quote = new List<string>();
                while (i < lines.Count && !Blank(lines[i]) && (lines[i].TrimStart().StartsWith('>') || quote.Count > 0 && !StartsBlock(lines[i])))
                {
                    var l = lines[i].TrimStart();
                    if (l.StartsWith('>'))
                    {
                        l = l[1..];
                        if (l.StartsWith(' ')) l = l[1..];
                    }
                    quote.Add(l);
                    i++;
                }
                html.Append("<blockquote>\n");
                Blocks(quote, html, tight: false);
                html.Append("</blockquote>\n");
                continue;
            }

            if (line.Contains('|') && i + 1 < lines.Count && TableDelimiter().IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
            {
                FlushParagraph();
                i = Table(lines, i, html);
                continue;
            }

            if (ListItem().Match(line) is { Success: true } item && (paragraph.Count == 0 || !Blank(item.Groups[4].Value)))
            {
                FlushParagraph();
                i = List(lines, i, html);
                continue;
            }

            if (paragraph.Count == 0 && Indent(line) >= 4)
            {
                var code = new List<string>();
                while (i < lines.Count && (Indent(lines[i]) >= 4 || Blank(lines[i])))
                {
                    code.Add(lines[i].Length >= 4 ? lines[i][4..] : "");
                    i++;
                }
                while (code.Count > 0 && Blank(code[^1])) code.RemoveAt(code.Count - 1);
                html.Append($"<pre><code>{Escape(string.Join("\n", code))}</code></pre>\n");
                continue;
            }

            paragraph.Add(line);
            i++;
        }
        FlushParagraph();
    }

    /// <summary>Начинается ли с этой строки новый блок (а не продолжение абзаца).</summary>
    private static bool StartsBlock(string line) =>
        Fence().IsMatch(line) || AtxHeading().IsMatch(line) || Rule().IsMatch(line) || ListItem().IsMatch(line);

    private void Heading(StringBuilder html, int level, string text)
    {
        var id = Slug(text);
        html.Append($"<h{level} id=\"{Attr(id)}\">{Inline(text)}</h{level}>\n");
    }

    private string Slug(string text)
    {
        var plain = InlineMarkup().Replace(text, "");
        var slug = new string(plain.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : c is ' ' or '-' or '_' ? '-' : '\0')
            .Where(c => c != '\0').ToArray()).Trim('-');
        if (slug.Length == 0) slug = "section";
        var unique = slug;
        for (var n = 1; !_ids.Add(unique); n++)
            unique = $"{slug}-{n}";
        return unique;
    }

    [GeneratedRegex(@"[*_`~\[\]()!#]")]
    private static partial Regex InlineMarkup();

    private int List(List<string> lines, int start, StringBuilder html)
    {
        var first = ListItem().Match(lines[start]);
        var ordered = char.IsDigit(first.Groups[2].Value[0]);
        var bullet = first.Groups[2].Value[^1];
        var items = new List<List<string>>();
        var loose = false;
        var i = start;
        List<string>? current = null;
        var contentIndent = 0;
        var pendingBlank = false;

        while (i < lines.Count)
        {
            var line = lines[i];
            var m = ListItem().Match(line);
            // Пункт того же уровня; глубже — это вложенное содержимое текущего пункта.
            if (m.Success && SameKind(m, ordered, bullet) && (current is null || Indent(line) < contentIndent))
            {
                if (pendingBlank && current is not null) loose = true;
                current = [m.Groups[4].Value];
                items.Add(current);
                // Содержимое пункта — от начала текста после маркера.
                contentIndent = m.Groups[1].Length + m.Groups[2].Length + Math.Max(1, Math.Min(m.Groups[3].Length, 4));
                pendingBlank = false;
                i++;
                continue;
            }
            if (Blank(line))
            {
                // Пустая строка: список продолжается, только если дальше вложенное содержимое или новый пункт.
                var next = i + 1 < lines.Count ? lines[i + 1] : null;
                if (next is null || Blank(next) && i + 2 >= lines.Count)
                    break;
                var nextItem = ListItem().Match(next);
                if (Indent(next) >= contentIndent || nextItem.Success && SameKind(nextItem, ordered, bullet) && Indent(next) < contentIndent)
                {
                    pendingBlank = true;
                    current!.Add("");
                    i++;
                    continue;
                }
                break;
            }
            if (Indent(line) >= contentIndent)
            {
                if (pendingBlank) loose = true;
                current!.Add(line[contentIndent..]);
                pendingBlank = false;
                i++;
                continue;
            }
            // Ленивое продолжение абзаца пункта.
            if (!pendingBlank && !StartsBlock(line) && !line.TrimStart().StartsWith('>'))
            {
                current!.Add(line.TrimStart());
                i++;
                continue;
            }
            break;
        }

        var startNumber = ordered && int.TryParse(first.Groups[2].Value[..^1], out var n) && n != 1 ? $" start=\"{n}\"" : "";
        html.Append(ordered ? $"<ol{startNumber}>\n" : "<ul>\n");
        foreach (var lines2 in items)
        {
            while (lines2.Count > 0 && Blank(lines2[^1])) lines2.RemoveAt(lines2.Count - 1);
            html.Append("<li>");
            var task = lines2.Count > 0 ? TaskBox().Match(lines2[0]) : Match.Empty;
            if (task.Success)
            {
                html.Append(task.Groups[1].Value is "x" or "X" ? "<input type=\"checkbox\" checked disabled> " : "<input type=\"checkbox\" disabled> ");
                lines2[0] = lines2[0][task.Length..];
            }
            var inner = new StringBuilder();
            Blocks(lines2, inner, tight: !loose);
            html.Append(inner.ToString().TrimEnd('\n'));
            html.Append("</li>\n");
        }
        html.Append(ordered ? "</ol>\n" : "</ul>\n");
        return i;
    }

    [GeneratedRegex(@"^\[([ xX])\]\s+")]
    private static partial Regex TaskBox();

    private static bool SameKind(Match m, bool ordered, char bullet)
    {
        var marker = m.Groups[2].Value;
        return char.IsDigit(marker[0]) ? ordered && marker[^1] == bullet : !ordered && marker[0] == bullet;
    }

    private int Table(List<string> lines, int start, StringBuilder html)
    {
        var header = Cells(lines[start]);
        var aligns = Cells(lines[start + 1]).Select(c =>
        {
            var t = c.Trim();
            return t.StartsWith(':') && t.EndsWith(':') ? "center" : t.EndsWith(':') ? "right" : t.StartsWith(':') ? "left" : null;
        }).ToList();
        string Align(int col) => col < aligns.Count && aligns[col] is { } a ? $" style=\"text-align:{a}\"" : "";

        html.Append("<table>\n<thead>\n<tr>");
        for (var c = 0; c < header.Count; c++)
            html.Append($"<th{Align(c)}>{Inline(header[c].Trim())}</th>");
        html.Append("</tr>\n</thead>\n<tbody>\n");
        var i = start + 2;
        while (i < lines.Count && !Blank(lines[i]) && lines[i].Contains('|'))
        {
            var cells = Cells(lines[i]);
            html.Append("<tr>");
            for (var c = 0; c < header.Count; c++)
                html.Append($"<td{Align(c)}>{(c < cells.Count ? Inline(cells[c].Trim()) : "")}</td>");
            html.Append("</tr>\n");
            i++;
        }
        html.Append("</tbody>\n</table>\n");
        return i;
    }

    /// <summary>Ячейки строки таблицы: «|» внутри `кода` и экранированный «\|» не делят.</summary>
    private static List<string> Cells(string line)
    {
        var t = line.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|")) t = t[..^1];
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inCode = false;
        for (var i = 0; i < t.Length; i++)
        {
            var c = t[i];
            if (c == '\\' && i + 1 < t.Length && t[i + 1] == '|')
            {
                sb.Append('|');
                i++;
            }
            else if (c == '`')
            {
                inCode = !inCode;
                sb.Append(c);
            }
            else if (c == '|' && !inCode)
            {
                cells.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        cells.Add(sb.ToString());
        return cells;
    }

    // --- Строчная разметка ---

    [GeneratedRegex(@"(`+)(.+?)(?<!`)\1(?!`)", RegexOptions.Singleline)]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"(!?)\[((?:[^\[\]]|\[[^\[\]]*\])*)\]\(\s*<?([^\s()<>]*(?:\([^\s()]*\)[^\s()<>]*)*)>?(?:\s+(?:""([^""]*)""|'([^']*)'))?\s*\)")]
    private static partial Regex LinkOrImage();

    [GeneratedRegex(@"<((?:https?|ftp|mailto):[^\s<>]+)>")]
    private static partial Regex AutoLink();

    [GeneratedRegex(@"\\([\\`*_{}\[\]()#+\-.!|~<>""'])")]
    private static partial Regex BackslashEscape();

    [GeneratedRegex(@"(\*\*|__)(?=\S)(.+?)(?<=\S)\1", RegexOptions.Singleline)]
    private static partial Regex Strong();

    [GeneratedRegex(@"(?<![\w*])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![\w*])|(?<![\w_])_(?=[^\s_])(.+?)(?<=[^\s_])_(?![\w_])", RegexOptions.Singleline)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"~~(?=\S)(.+?)(?<=\S)~~", RegexOptions.Singleline)]
    private static partial Regex Strike();

    [GeneratedRegex(@"( {2,}|\\)\n")]
    private static partial Regex HardBreak();

    /// <summary>
    /// Строчная разметка. Код, ссылки и экранированные символы заменяются метками \u0001N\u0002, остальное
    /// экранируется, затем выделение, и метки возвращаются на место.
    /// </summary>
    private string Inline(string text)
    {
        var slots = new List<string>();
        string Slot(string html)
        {
            slots.Add(html);
            return $"\u0001{slots.Count - 1}\u0002";
        }

        text = text.Replace("\u0001", "").Replace("\u0002", "");
        text = CodeSpan().Replace(text, m => Slot($"<code>{Escape(m.Groups[2].Value.Trim())}</code>"));
        text = BackslashEscape().Replace(text, m => Slot(Escape(m.Groups[1].Value)));
        text = LinkOrImage().Replace(text, m =>
        {
            var url = Url(m.Groups[3].Value, image: m.Groups[1].Value == "!");
            var title = m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Success ? m.Groups[5].Value : null;
            var titleAttr = title is null ? "" : $" title=\"{Attr(title)}\"";
            if (m.Groups[1].Value == "!")
                return Slot($"<img src=\"{Attr(url)}\" alt=\"{Attr(m.Groups[2].Value)}\"{titleAttr}>");
            return Slot($"<a href=\"{Attr(url)}\"{titleAttr}>{Inline(m.Groups[2].Value)}</a>");
        });
        text = AutoLink().Replace(text, m => Slot($"<a href=\"{Attr(Url(m.Groups[1].Value, false))}\">{Escape(m.Groups[1].Value)}</a>"));

        text = Escape(text);
        text = Strong().Replace(text, "<strong>$2</strong>");
        text = Emphasis().Replace(text, m => $"<em>{(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)}</em>");
        text = Strike().Replace(text, "<del>$1</del>");
        text = HardBreak().Replace(text, "<br>\n");

        // Метки могут быть вложены (ссылка с кодом внутри уже собрана целиком) — подставляем, пока есть.
        for (var guard = 0; guard < 4 && text.Contains('\u0001'); guard++)
            text = SlotPattern().Replace(text, m => slots[int.Parse(m.Groups[1].Value)]);
        return text;
    }

    [GeneratedRegex("\u0001(\\d+)\u0002")]
    private static partial Regex SlotPattern();

    /// <summary>Опасные схемы (javascript:, vbscript:, data: кроме картинок) превращаются в «#».</summary>
    private string Url(string url, bool image)
    {
        var trimmed = url.Trim();
        var scheme = UrlScheme().Match(trimmed);
        if (scheme.Success)
        {
            var name = scheme.Groups[1].Value.ToLowerInvariant();
            // «C:/путь» — это диск Windows, а не схема.
            var allowed = name.Length == 1 || name is "http" or "https" or "mailto" or "ftp" or "file" or "tel"
                          || image && name == "data" && trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
            return allowed ? trimmed : "#";
        }
        return _resolve is null || trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith('/') ? trimmed : _resolve(trimmed);
    }

    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9+.\-]*):")]
    private static partial Regex UrlScheme();

    private static string Resolve(string url, string baseDirectory)
    {
        try
        {
            var hash = url.IndexOf('#');
            var path = hash >= 0 ? url[..hash] : url;
            var fragment = hash >= 0 ? url[hash..] : "";
            var full = Path.GetFullPath(Path.Combine(baseDirectory, Uri.UnescapeDataString(path)));
            return new Uri(full).AbsoluteUri + fragment;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or UriFormatException or PathTooLongException)
        {
            return url;
        }
    }

    private static string Escape(string text) => WebUtility.HtmlEncode(text).Replace("&#39;", "'");

    private static string Attr(string text) => WebUtility.HtmlEncode(text);

    /// <summary>Самодостаточная страница: стили внутри, светлая и тёмная тема, печать без лишнего.</summary>
    public static string Page(string title, string body) => $$"""
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{Attr(title)}}</title>
        <style>
        :root { color-scheme: light dark; --fg: #1f2328; --muted: #59636e; --bg: #ffffff; --line: #d1d9e0; --soft: #f6f8fa; --link: #0969da; }
        @media (prefers-color-scheme: dark) { :root { --fg: #e6edf3; --muted: #9198a1; --bg: #0d1117; --line: #3d444d; --soft: #151b23; --link: #4493f8; } }
        html { background: var(--bg); }
        body { max-width: 860px; margin: 0 auto; padding: 32px 24px 64px; color: var(--fg); background: var(--bg);
          font: 16px/1.6 -apple-system, "Segoe UI", system-ui, Roboto, "Helvetica Neue", Arial, sans-serif; overflow-wrap: break-word; }
        h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 1.5em 0 0.6em; font-weight: 600; }
        h1 { font-size: 2em; padding-bottom: .3em; border-bottom: 1px solid var(--line); }
        h2 { font-size: 1.5em; padding-bottom: .3em; border-bottom: 1px solid var(--line); }
        h3 { font-size: 1.25em; } h4 { font-size: 1em; } h5 { font-size: .875em; } h6 { font-size: .85em; color: var(--muted); }
        body > :first-child { margin-top: 0; }
        p, ul, ol, blockquote, pre, table { margin: 0 0 1em; }
        a { color: var(--link); text-decoration: none; } a:hover { text-decoration: underline; }
        ul, ol { padding-left: 2em; } li + li { margin-top: .25em; }
        li > input[type=checkbox] { margin: 0 .4em 0 -1.3em; vertical-align: middle; }
        blockquote { padding: 0 1em; color: var(--muted); border-left: .25em solid var(--line); }
        code { font: .875em/1.45 ui-monospace, "Cascadia Mono", Consolas, "Liberation Mono", monospace;
          background: var(--soft); padding: .2em .4em; border-radius: 6px; }
        pre { background: var(--soft); padding: 16px; border-radius: 6px; overflow: auto; }
        pre code { background: none; padding: 0; font-size: .875em; white-space: pre; }
        table { border-collapse: collapse; display: block; overflow: auto; max-width: 100%; }
        th, td { border: 1px solid var(--line); padding: 6px 13px; }
        th { font-weight: 600; background: var(--soft); }
        tr:nth-child(2n) td { background: color-mix(in srgb, var(--soft) 60%, transparent); }
        hr { height: 2px; border: 0; background: var(--line); margin: 1.5em 0; }
        img { max-width: 100%; }
        del { color: var(--muted); }
        @media print { body { max-width: none; padding: 0; } pre { white-space: pre-wrap; } pre code { white-space: pre-wrap; }
          a { color: inherit; } h1, h2, h3 { break-after: avoid; } pre, table, img { break-inside: avoid; } }
        </style>
        </head>
        <body>
        {{body}}</body>
        </html>
        """;
}
