using FileDial.Core.Compression;
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
    public const string UnsupportedText = "Пока не поддерживается";

    /// <summary>Длина имени в центре круга: две строки текста.</summary>
    public const int MaxTitleLength = 34;

    /// <summary>Больше — это уже не «выделил пару файлов», а работа для отдельного инструмента.</summary>
    public const int MaxBatch = 200;

    public static HotkeyOutcome Interpret(SelectionResult selection, IFileProbe probe, IMediaProbe? media = null,
        Settings.JobSettings? settings = null)
    {
        if (!selection.IsSuccess)
            return new HotkeyOutcome.ShowToast(selection.Error!);

        var paths = selection.Paths;
        if (paths.Count == 0)
            return new HotkeyOutcome.ShowToast("Ничего не выделено");

        if (paths.Count > 1)
            return InterpretBatch(paths, probe, settings);

        var path = paths[0];
        var state = probe.Probe(path);
        switch (state.Kind)
        {
            case FileStateKind.Missing:
                return new HotkeyOutcome.ShowToast("Файл недоступен: это не обычный файл на диске");
            case FileStateKind.Unavailable:
                return new HotkeyOutcome.ShowToast("Файл недоступен: нет доступа или он занят");
            case FileStateKind.Directory:
                return Unsupported(Path.GetFileName(path.TrimEnd('\\', '/')), path);
        }

        var name = Path.GetFileName(path);
        var size = SizeFormatter.Format(state.Size);
        var kind = FileKinds.Of(path);
        if (kind == FileKind.Other)
            return Unsupported(name, path, size);

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
        var count = Plural.Count(paths.Count, "объект", "объекта", "объектов");
        if (paths.Count > MaxBatch)
            return Unsupported($"Выделено {count}", null, $"Не больше {MaxBatch} за раз");

        var sizes = new List<long>(paths.Count);
        foreach (var path in paths)
        {
            var state = probe.Probe(path);
            if (state.Kind == FileStateKind.Directory)
                return Unsupported($"Выделено {count}", null, "Среди выделенного есть папки");
            if (state.Kind != FileStateKind.File)
                return new HotkeyOutcome.ShowToast($"Файл недоступен: {Path.GetFileName(path)}");
            sizes.Add(state.Size);
        }

        var kinds = paths.Select(FileKinds.Of).Distinct().ToList();
        if (kinds.Count > 1)
            return Unsupported($"Выделено {count}", null, "Файлы разных типов");
        if (kinds[0] == FileKind.Other)
            return Unsupported($"Выделено {count}", null);

        var title = KindCount(kinds[0], paths.Count);
        var actions = ActionCatalog.ForBatch(kinds[0], paths, sizes, settings);
        return new HotkeyOutcome.ShowMenu(
            new DialMenu(DialMenuKind.Actions, title, SizeFormatter.Format(sizes.Sum()), actions, paths[0])
            {
                SourcePaths = paths,
            });
    }

    /// <summary>«5 картинок», «2 аудиофайла», «3 видео».</summary>
    public static string KindCount(FileKind kind, int n) => kind switch
    {
        FileKind.Video => $"{n} видео",
        FileKind.Image => Plural.Count(n, "картинка", "картинки", "картинок"),
        FileKind.Audio => Plural.Count(n, "аудиофайл", "аудиофайла", "аудиофайлов"),
        _ => Plural.Count(n, "файл", "файла", "файлов"),
    };

    private static HotkeyOutcome Unsupported(string title, string? path, string? details = null) =>
        new HotkeyOutcome.ShowMenu(new DialMenu(
            DialMenuKind.Unsupported,
            NameShortener.Middle(title, MaxTitleLength),
            details is null ? UnsupportedText : $"{details}\n{UnsupportedText}",
            [],
            path));
}
