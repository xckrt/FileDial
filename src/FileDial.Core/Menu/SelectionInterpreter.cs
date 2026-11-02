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
    string? SourcePath);

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

    public static HotkeyOutcome Interpret(SelectionResult selection, IFileProbe probe, IMediaProbe? media = null)
    {
        if (!selection.IsSuccess)
            return new HotkeyOutcome.ShowToast(selection.Error!);

        var paths = selection.Paths;
        if (paths.Count == 0)
            return new HotkeyOutcome.ShowToast("Ничего не выделено");

        if (paths.Count > 1)
            return Unsupported($"Выделено объектов: {paths.Count}", null);

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
        var actions = ActionCatalog.ForFile(path, state.Size, info);
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

    private static HotkeyOutcome Unsupported(string title, string? path, string? size = null) =>
        new HotkeyOutcome.ShowMenu(new DialMenu(
            DialMenuKind.Unsupported,
            NameShortener.Middle(title, MaxTitleLength),
            size is null ? UnsupportedText : $"{size}\n{UnsupportedText}",
            [],
            path));
}
