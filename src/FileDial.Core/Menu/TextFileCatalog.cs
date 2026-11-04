using FileDial.Core.Compression;
using FileDial.Core.Localization;
using FileDial.Core.TextFiles;

namespace FileDial.Core.Menu;

/// <summary>Круги для текстовых файлов, SVG и значков; подпись в центре круга для них же.</summary>
public static class TextFileCatalog
{
    /// <summary>JSON до этого размера проверяем при открытии круга: не сломан ли и есть ли что менять.</summary>
    public const long JsonCheckLimit = 5 * 1024 * 1024;

    public static IReadOnlyList<DialAction> ForText(string path) => ForText(path, TextAnalysis.TryRead(path));

    /// <param name="info">Разбор файла; null — не прочитался (тогда ничего не запрещаем, окно действия объяснит).</param>
    public static IReadOnlyList<DialAction> ForText(string path, TextAnalysis? info)
    {
        var binary = info is { Encoding: TextEncodingKind.Binary } ? Lang.T.TxtLooksBinary : null;

        string? utf8 = binary;
        if (utf8 is null && info is not null && info.Encoding.IsUtf8())
            utf8 = info.Encoding == TextEncodingKind.Ascii ? Lang.T.TxtAsciiIsUtf8 : Lang.T.TxtAlreadyUtf8(info.Encoding.Label());

        var style = info?.LineEndings.Style;
        string? EolReason(LineEndingStyle target) =>
            binary
            ?? (style == LineEndingStyle.None && info!.Complete ? Lang.T.TxtSingleLine : null)
            ?? (style == target && info!.Complete ? Lang.T.TxtAlreadyEol(LineEndingsJob.Label(target)) : null);

        var csv = TextFileJobs.IsCsv(path) ? binary : Lang.T.TxtOnlyCsv;
        var (pretty, minify) = TextFileJobs.IsJson(path) ? JsonReasons(path, info, binary) : (Lang.T.TxtOnlyJson, Lang.T.TxtOnlyJson);
        var markdown = TextFileJobs.IsMarkdown(path) ? binary : Lang.T.TxtOnlyMarkdown;
        var pdf = markdown ?? (EdgeBrowser.Find() is null ? Lang.T.TxtNeedsEdge : null);

        return
        [
            Action(ActionIds.TextToUtf8, Lang.T.TxtToUtf8, utf8),
            DialAction.Group(ActionIds.TextLineEndings, Lang.T.TxtLineEndings,
            [
                Action(ActionIds.TextToCrlf, "CRLF", EolReason(LineEndingStyle.Crlf)) with { Caption = Lang.T.TxtToEol("CRLF (Windows)") },
                Action(ActionIds.TextToLf, "LF", EolReason(LineEndingStyle.Lf)) with { Caption = Lang.T.TxtToEol("LF (Unix)") },
            ]),
            Action(ActionIds.CsvToXlsx, Lang.T.TxtToXlsx, csv),
            Action(ActionIds.JsonFormat, Lang.T.TxtJsonPretty, pretty),
            Action(ActionIds.JsonMinify, Lang.T.TxtJsonMinify, minify),
            Action(ActionIds.MarkdownToHtml, Lang.T.TxtToHtml, markdown),
            Action(ActionIds.MarkdownToPdf, Lang.T.ToPdf, pdf),
        ];
    }

    /// <summary>Сломанный JSON — оба действия неактивны с местом ошибки; уже нужного вида — неактивно одно.</summary>
    private static (string? Pretty, string? Minify) JsonReasons(string path, TextAnalysis? info, string? binary)
    {
        if (binary is not null) return (binary, binary);
        if (info is null || !info.Complete || new FileInfo(path).Length > JsonCheckLimit) return (null, null);
        string text;
        try
        {
            var bytes = File.ReadAllBytes(path);
            var bom = info.Encoding.BomLength();
            text = info.Encoding.Encoding().GetString(bytes, bom, bytes.Length - bom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
        var (valid, prettyText, minText, error) = JsonText.Inspect(text, JsonJob.NewLineFor(info.LineEndings.Style));
        if (!valid)
        {
            var reason = Lang.T.TxtJsonError(error!.Line, error.Column);
            return (reason, reason);
        }
        return (prettyText!.TrimEnd() == text.TrimEnd() ? Lang.T.TxtJsonAlreadyPretty : null,
            minText!.TrimEnd() == text.TrimEnd() ? Lang.T.TxtJsonAlreadyMinified : null);
    }

    public static IReadOnlyList<DialAction> ForSvg(string path)
    {
        var why = SvgSupport.DecoderAvailable(FfmpegLocator.LocateDefault()) ? null : Lang.T.TxtSvgNoDecoder;
        return
        [
            Action(ActionIds.SvgToPng, Lang.T.TxtToPng, why) with { Caption = Lang.T.TxtSvgToPngCaption(Raster.SvgLongSide) },
            Action(ActionIds.SvgPngSizes, Lang.T.TxtPngSizes, why),
            Action(ActionIds.SvgToIco, Lang.T.TxtToIco, why),
        ];
    }

    public static IReadOnlyList<DialAction> ForIcon(string path)
    {
        var frames = TryFrames(path);
        var broken = frames is { Count: 0 } ? Lang.T.TxtIcoUnreadable : null;
        var single = frames is not null && IcoFile.BestPerSize(frames).Count == 1 ? Lang.T.TxtIcoSingleSize : null;
        return
        [
            Action(ActionIds.IcoToPng, Lang.T.TxtToPng, broken) with { Caption = Lang.T.TxtIcoLargestCaption },
            Action(ActionIds.IcoAllPng, Lang.T.TxtAllSizesToPng, broken ?? single),
        ];
    }

    /// <summary>Кадры значка; пустой список — файл не значок; null — не прочитать.</summary>
    private static IReadOnlyList<IcoFrame>? TryFrames(string path)
    {
        try
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) return null;
            return IcoFile.Read(File.ReadAllBytes(path));
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Несколько файлов: действие активно, если нужно хоть одному. Неподходящие файлы задача пропускает сама
    /// («Не JSON», «Уже UTF-8»).
    /// </summary>
    public static IReadOnlyList<DialAction> ForBatch(FileKind kind, IReadOnlyList<string> paths)
    {
        if (kind == FileKind.Svg)
            return ForSvg(paths[0]);
        if (kind == FileKind.Icon)
            return ForIcon(paths[0]).Select(a => a with { IsEnabled = true, DisabledReason = null }).ToList();

        // Для кодировки и переводов строк хватает начала файла.
        var infos = paths.Select(p => TextAnalysis.TryRead(p, 256 * 1024)).ToList();
        var readable = infos.All(i => i is not null);
        var texts = infos.Where(i => i is { Encoding: not TextEncodingKind.Binary }).Select(i => i!).ToList();
        // Неактивно, только если точно известно про все файлы; двоичные задача пропустит.
        string? AllOr(Func<TextAnalysis, bool> done, string reason) =>
            !readable ? null : texts.Count == 0 ? Lang.T.TxtLooksBinary : texts.All(done) ? reason : null;

        var utf8 = AllOr(i => i.Encoding.IsUtf8(), Lang.T.TxtAllUtf8);
        string? Eol(LineEndingStyle target) =>
            AllOr(i => i.Complete && i.LineEndings.Style is var s && (s == target || s == LineEndingStyle.None),
                Lang.T.TxtAllAlreadyEol(LineEndingsJob.Label(target)));
        var csv = paths.Any(TextFileJobs.IsCsv) ? null : Lang.T.TxtOnlyCsv;
        var json = paths.Any(TextFileJobs.IsJson) ? null : Lang.T.TxtOnlyJson;
        var markdown = paths.Any(TextFileJobs.IsMarkdown) ? null : Lang.T.TxtOnlyMarkdown;
        var pdf = markdown ?? (EdgeBrowser.Find() is null ? Lang.T.TxtNeedsEdge : null);

        return
        [
            Action(ActionIds.TextToUtf8, Lang.T.TxtToUtf8, utf8),
            DialAction.Group(ActionIds.TextLineEndings, Lang.T.TxtLineEndings,
            [
                Action(ActionIds.TextToCrlf, "CRLF", Eol(LineEndingStyle.Crlf)) with { Caption = Lang.T.TxtToEol("CRLF (Windows)") },
                Action(ActionIds.TextToLf, "LF", Eol(LineEndingStyle.Lf)) with { Caption = Lang.T.TxtToEol("LF (Unix)") },
            ]),
            Action(ActionIds.CsvToXlsx, Lang.T.TxtToXlsx, csv),
            Action(ActionIds.JsonFormat, Lang.T.TxtJsonPretty, json),
            Action(ActionIds.JsonMinify, Lang.T.TxtJsonMinify, json),
            Action(ActionIds.MarkdownToHtml, Lang.T.TxtToHtml, markdown),
            Action(ActionIds.MarkdownToPdf, Lang.T.ToPdf, pdf),
        ];
    }

    /// <summary>Подпись в центре круга: «12 КБ · 340 строк · UTF-8», «4 КБ · 512×512», «15 КБ · 7 размеров, до 256×256».</summary>
    public static string Subtitle(FileKind kind, string path, string size)
    {
        switch (kind)
        {
            case FileKind.Text when TextAnalysis.TryRead(path) is { } info:
                if (info.Encoding == TextEncodingKind.Binary)
                    return size;
                return info.Complete
                    ? $"{size} · {Lang.T.TxtLines(info.Lines)} · {info.Encoding.Label()}"
                    : $"{size} · {info.Encoding.Label()}";
            case FileKind.Svg when SvgSupport.Dimensions(path) is { } d:
                return $"{size} · {Math.Round(d.Width)}×{Math.Round(d.Height)}";
            case FileKind.Icon when TryFrames(path) is { Count: > 0 } frames:
                var largest = IcoFile.Largest(frames);
                var sizes = IcoFile.BestPerSize(frames).Count;
                return $"{size} · {Lang.T.TxtIconSizes(sizes, $"{largest.Width}×{largest.Height}")}";
            default:
                return size;
        }
    }

    private static DialAction Action(string id, string label, string? whyNot) => new(id, label, whyNot is null, whyNot);
}
