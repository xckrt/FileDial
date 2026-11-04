using FileDial.Core.Compression;
using FileDial.Core.Localization;
using FileDial.Core.Selection;
using FileDial.Core.Text;

namespace FileDial.Core.Menu;

public enum DialMenuKind
{
    Actions,
    Unsupported,
}

/// <summary>Что показать в круге.</summary>
public sealed record DialMenu(
    DialMenuKind Kind,
    string Title,
    string Subtitle,
    IReadOnlyList<DialAction> Actions,
    string? SourcePath)
{
    /// <summary>Все выбранные файлы: один для обычного круга, несколько — для пакетной обработки.</summary>
    public IReadOnlyList<string> SourcePaths { get; init; } = SourcePath is null ? [] : [SourcePath];

    public bool IsBatch => SourcePaths.Count > 1;

    /// <summary>
    /// Единственное действие для компактного круга (у файлов без своего круга, папок, смешанного выделения):
    /// Enter или клик по кругу запускает его. null — компактный круг только сообщает «не поддерживается».
    /// </summary>
    public DialAction? QuickAction { get; init; }
}

/// <summary>Результат обработки нажатия: либо круг, либо короткое уведомление.</summary>
public abstract record HotkeyOutcome
{
    public sealed record ShowMenu(DialMenu Menu) : HotkeyOutcome;
    public sealed record ShowToast(string Message) : HotkeyOutcome;
}

/// <summary>Превращает результат чтения выделения в меню или уведомление.</summary>
public static class SelectionInterpreter
{
    public static string UnsupportedText => Lang.T.Unsupported;

    /// <summary>Длина имени в центре круга: две строки текста.</summary>
    public const int MaxTitleLength = 34;

    /// <summary>Больше — это уже не «выделил пару файлов», а работа для отдельного инструмента.</summary>
    public const int MaxBatch = 200;

    public static HotkeyOutcome Interpret(SelectionResult selection, IFileProbe probe, IMediaProbe? media = null,
        Settings.JobSettings? settings = null) =>
        // Свои действия пользователя добавляются поверх готового меню.
        Custom.CustomPlacement.Apply(InterpretBuiltIn(selection, probe, media, settings), settings?.CustomActions, probe);

    private static HotkeyOutcome InterpretBuiltIn(SelectionResult selection, IFileProbe probe, IMediaProbe? media,
        Settings.JobSettings? settings)
    {
        if (!selection.IsSuccess)
            return new HotkeyOutcome.ShowToast(selection.Error!);

        var paths = selection.Paths;
        if (paths.Count == 0)
            return new HotkeyOutcome.ShowToast(Lang.T.NothingSelected);

        if (paths.Count > 1)
            return InterpretBatch(paths, probe, settings);

        var path = paths[0];
        var state = probe.Probe(path);
        switch (state.Kind)
        {
            case FileStateKind.Missing:
                return new HotkeyOutcome.ShowToast(Lang.T.NotRegularFile);
            case FileStateKind.Unavailable:
                return new HotkeyOutcome.ShowToast(Lang.T.FileBusy);
            case FileStateKind.Directory:
                return Unsupported(Path.GetFileName(path.TrimEnd('\\', '/')), path, null, [path]);
        }

        var name = Path.GetFileName(path);
        var size = SizeFormatter.Format(state.Size);

        // Часть файла (.001, .002, …) — предлагаем собрать целиком.
        if (PartsMenu(path) is { } parts)
            return parts;

        var kind = FileKinds.Of(path);
        if (kind == FileKind.Other)
            return Unsupported(name, path, size, [path]);

        if (kind == FileKind.Archive)
        {
            // Число файлов — для подписи; tar на огромном tar.gz может думать долго, поэтому с таймаутом.
            var count = Archives.ArchiveSupport.CountFiles(path, TimeSpan.FromSeconds(2));
            return new HotkeyOutcome.ShowMenu(new DialMenu(DialMenuKind.Actions, NameShortener.Middle(name, MaxTitleLength),
                count is { } n ? $"{size} · {Archives.ArchiveSupport.FilesLabel(n)}" : size,
                ActionCatalog.ForFile(path, state.Size, null, settings), path));
        }

        if (kind == FileKind.Pdf)
        {
            // Число страниц — для подписи и чтобы не предлагать «Разделить» одностраничному файлу.
            var pages = Documents.PdfFiles.TryPageCount(path, TimeSpan.FromSeconds(2));
            return new HotkeyOutcome.ShowMenu(new DialMenu(DialMenuKind.Actions, NameShortener.Middle(name, MaxTitleLength),
                pages is { } p ? $"{size} · {Documents.PdfFiles.PagesLabel(p)}" : size,
                ActionCatalog.ForFile(path, state.Size, null, settings, pages), path));
        }
        if (kind == FileKind.Document)
            return new HotkeyOutcome.ShowMenu(new DialMenu(DialMenuKind.Actions, NameShortener.Middle(name, MaxTitleLength),
                size, ActionCatalog.ForFile(path, state.Size, null, settings), path));

        // Текст, SVG и значки: подпись — строки и кодировка, размер рисунка или число размеров значка.
        if (kind is FileKind.Text or FileKind.Svg or FileKind.Icon)
            return new HotkeyOutcome.ShowMenu(new DialMenu(DialMenuKind.Actions, NameShortener.Middle(name, MaxTitleLength),
                TextFileCatalog.Subtitle(kind, path, size), ActionCatalog.ForFile(path, state.Size, null, settings), path));

        // Длительность и размер кадра нужны, чтобы не предлагать заведомо ненужное или невозможное;
        // без ffprobe обходимся тем, что знаем по имени и размеру файла.
        var info = media?.TryProbe(path);
        var actions = ActionCatalog.ForFile(path, state.Size, info, settings);
        var details = kind switch
        {
            FileKind.Image when info is { Width: > 0, Height: > 0 } => $"{info.Width}×{info.Height}",
            FileKind.Video or FileKind.Audio when info is { DurationSeconds: > 0 } => DurationFormatter.Format(info.DurationSeconds),
            _ => null,
        };
        var subtitle = details is null ? size : $"{size} · {details}";

        return new HotkeyOutcome.ShowMenu(
            new DialMenu(DialMenuKind.Actions, NameShortener.Middle(name, MaxTitleLength), subtitle, actions, path));
    }

    /// <summary>
    /// Несколько файлов одного типа — круг с теми же действиями, которые выполнятся для каждого файла.
    /// Папки, разные типы и недоступные файлы — «пока не поддерживается» с понятной причиной.
    /// </summary>
    private static HotkeyOutcome InterpretBatch(IReadOnlyList<string> paths, IFileProbe probe, Settings.JobSettings? settings)
    {
        var selected = Lang.T.SelectedCount(paths.Count);
        if (paths.Count > MaxBatch)
            return Unsupported(selected, null, Lang.T.AtMostAtOnce(MaxBatch));

        var sizes = new List<long>(paths.Count);
        foreach (var path in paths)
        {
            var state = probe.Probe(path);
            if (state.Kind == FileStateKind.Directory)
                return Unsupported(selected, null, Lang.T.HasFolders, paths);
            if (state.Kind != FileStateKind.File)
                return new HotkeyOutcome.ShowToast(Lang.T.FileUnavailable(Path.GetFileName(path)));
            sizes.Add(state.Size);
        }

        // Выделены части одного набора — это тоже «собрать».
        var bases = paths.Select(Archives.FileParts.BaseOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (bases is [{ } commonBase] && PartsMenu(commonBase + ".001") is { } partsMenu)
            return partsMenu;

        var kinds = paths.Select(FileKinds.Of).Distinct().ToList();
        if (kinds.Count > 1)
            return Unsupported(selected, null, Lang.T.MixedTypes, paths);
        if (kinds[0] == FileKind.Other)
            return Unsupported(selected, null, null, paths);

        var title = KindCount(kinds[0], paths.Count);
        var actions = ActionCatalog.ForBatch(kinds[0], paths, sizes, settings);
        return new HotkeyOutcome.ShowMenu(
            new DialMenu(DialMenuKind.Actions, title, SizeFormatter.Format(sizes.Sum()), actions, paths[0])
            {
                SourcePaths = paths,
            });
    }

    /// <summary>Компактный круг для частей: «имя.zip · 3 части · 52 МБ», Enter — собрать.</summary>
    private static HotkeyOutcome? PartsMenu(string path)
    {
        if (Archives.FileParts.BaseOf(path) is not { } basePath)
            return null;
        var parts = Archives.FileParts.Find(basePath);
        if (parts.Count < 2)
            return null;
        var total = parts.Sum(p => new FileInfo(p).Length);
        return new HotkeyOutcome.ShowMenu(new DialMenu(DialMenuKind.Unsupported,
            NameShortener.Middle(Path.GetFileName(basePath), MaxTitleLength),
            $"{Archives.FileParts.PartsLabel(parts.Count)} · {SizeFormatter.Format(total)}",
            [], parts[0])
        {
            SourcePaths = [parts[0]],
            QuickAction = new DialAction(ActionIds.JoinParts, Lang.T.JoinParts, true) { Caption = Lang.T.JoinParts },
        });
    }

    /// <summary>«5 картинок», «2 аудиофайла», «3 видео».</summary>
    public static string KindCount(FileKind kind, int n) => kind switch
    {
        FileKind.Video => Lang.T.Videos(n),
        FileKind.Image => Lang.T.Images(n),
        FileKind.Audio => Lang.T.AudioFiles(n),
        FileKind.Pdf => Lang.T.Pdfs(n),
        FileKind.Document => Lang.T.Documents(n),
        FileKind.Text => Lang.T.TxtTextFiles(n),
        FileKind.Svg => Lang.T.TxtSvgFiles(n),
        FileKind.Icon => Lang.T.TxtIconFiles(n),
        _ => Lang.T.Files(n),
    };

    /// <param name="zipPaths">Что можно сжать в ZIP прямо из компактного круга; null — ничего.</param>
    private static HotkeyOutcome Unsupported(string title, string? path, string? details = null,
        IReadOnlyList<string>? zipPaths = null) =>
        new HotkeyOutcome.ShowMenu(new DialMenu(
            DialMenuKind.Unsupported,
            NameShortener.Middle(title, MaxTitleLength),
            // Если можно хотя бы сжать в ZIP, «не поддерживается» было бы неправдой — оставляем размер или причину.
            zipPaths is { Count: > 0 }
                ? details ?? ""
                : details is null ? UnsupportedText : $"{details}\n{UnsupportedText}",
            [],
            path ?? zipPaths?.FirstOrDefault())
        {
            SourcePaths = zipPaths ?? (path is null ? [] : [path]),
            QuickAction = zipPaths is { Count: > 0 }
                ? new DialAction(ActionIds.CompressToZip, Lang.T.CompressToZip, true) { Caption = Lang.T.CompressToZip }
                : null,
        });
}
