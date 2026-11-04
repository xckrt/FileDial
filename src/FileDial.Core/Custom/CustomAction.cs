using FileDial.Core.Menu;

namespace FileDial.Core.Custom;

/// <summary>
/// Своё действие пользователя: внешняя программа с шаблоном аргументов.
/// Хранится в настройках (JSON); испорченные записи отбрасывает <see cref="CustomActions.Normalize"/>.
/// </summary>
public sealed record CustomAction
{
    /// <summary>Постоянный id: по нему круг находит действие после выбора сектора.</summary>
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>Для каких файлов: any, video, image, audio, pdf, document, archive или extensions.</summary>
    public string AppliesTo { get; init; } = CustomActions.Any;

    /// <summary>Список расширений для AppliesTo = extensions: «txt, md, tar.gz».</summary>
    public string Extensions { get; init; } = "";

    /// <summary>Путь к .exe, имя из PATH или {ffmpeg}.</summary>
    public string Program { get; init; } = "";

    /// <summary>Шаблон аргументов с переменными {file}, {dir}, {name}, {ext}, {out}, {ffmpeg}.</summary>
    public string Arguments { get; init; } = "";

    /// <summary>Добавка к имени результата для {out}.</summary>
    public string OutputSuffix { get; init; } = "";

    /// <summary>Расширение результата без точки; пусто — как у исходника.</summary>
    public string OutputExtension { get; init; } = "";

    /// <summary>Показывать окно программы (консоль или её собственное окно).</summary>
    public bool ShowWindow { get; init; }

    /// <summary>Подходит ли файл под «для каких файлов».</summary>
    public bool Matches(string path)
    {
        if (AppliesTo == CustomActions.Any)
            return true;
        if (AppliesTo == CustomActions.ByExtension)
        {
            var name = Path.GetFileName(path);
            return CustomActions.ParseExtensions(Extensions)
                .Any(e => name.Length > e.Length && name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        }
        return CustomActions.KindOf(AppliesTo) is { } kind && FileKinds.Of(path) == kind;
    }
}

public static class CustomActions
{
    public const string Any = "any";
    public const string ByExtension = "extensions";

    /// <summary>Больше в настройках не храним: круг всё равно покажет не больше восьми на тип файла.</summary>
    public const int MaxCount = 50;

    public const int MaxNameLength = 60;

    public static readonly IReadOnlyList<string> Kinds =
        [Any, "video", "image", "audio", "pdf", "document", "archive", ByExtension];

    public static FileKind? KindOf(string appliesTo) => appliesTo switch
    {
        "video" => FileKind.Video,
        "image" => FileKind.Image,
        "audio" => FileKind.Audio,
        "pdf" => FileKind.Pdf,
        "document" => FileKind.Document,
        "archive" => FileKind.Archive,
        _ => null,
    };

    /// <summary>«txt, *.MD; .log» → [.txt, .md, .log].</summary>
    public static IReadOnlyList<string> ParseExtensions(string? text) =>
        (text ?? "").Split([',', ';', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.TrimStart('*').Trim('.').ToLowerInvariant())
            .Where(e => e.Length > 0)
            .Select(e => "." + e)
            .Distinct()
            .ToList();

    /// <summary>Действие для id сектора или null.</summary>
    public static CustomAction? Find(IEnumerable<CustomAction>? actions, string actionId) =>
        CustomActionIds.IsCustom(actionId)
            ? actions?.FirstOrDefault(a => CustomActionIds.For(a) == actionId)
            : null;

    /// <summary>
    /// Чистка списка из настроек: обрезка пробелов, id для записей без него, отбрасывание неполных
    /// и опасных записей (без названия или программы, .bat/.cmd, неизвестный тип файлов).
    /// </summary>
    public static CustomActionList Normalize(IEnumerable<CustomAction?>? actions)
    {
        var result = new List<CustomAction>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in actions ?? [])
        {
            if (raw is null)
                continue;
            var action = raw with
            {
                Id = (raw.Id ?? "").Trim(),
                Name = (raw.Name ?? "").Trim(),
                AppliesTo = (raw.AppliesTo ?? "").Trim().ToLowerInvariant(),
                Extensions = string.Join(", ", ParseExtensions(raw.Extensions).Select(e => e[1..])),
                Program = (raw.Program ?? "").Trim(),
                Arguments = (raw.Arguments ?? "").Trim(),
                OutputSuffix = (raw.OutputSuffix ?? "").Trim(),
                OutputExtension = (raw.OutputExtension ?? "").Trim().TrimStart('.'),
            };
            if (CustomCommand.StructuralProblem(action) is not null)
                continue;
            if (action.Id.Length == 0 || !ids.Add(action.Id))
            {
                action = action with { Id = Guid.NewGuid().ToString("N") };
                ids.Add(action.Id);
            }
            result.Add(action);
            if (result.Count == MaxCount)
                break;
        }
        return new CustomActionList(result);
    }
}

/// <summary>Id секторов своих действий.</summary>
public static class CustomActionIds
{
    public const string Prefix = "custom:";

    /// <summary>Группа «Ещё ›»: вытесненное встроенное действие и свои действия.</summary>
    public const string More = "custom-more";

    public static string For(CustomAction action) => Prefix + action.Id;

    public static bool IsCustom(string actionId) => actionId.StartsWith(Prefix, StringComparison.Ordinal);
}

/// <summary>
/// Список своих действий, равный другому по содержимому: настройки — record, и два одинаковых набора настроек
/// должны быть равны (обычный список сравнивался бы по ссылке).
/// </summary>
public sealed class CustomActionList(IEnumerable<CustomAction> items) : IReadOnlyList<CustomAction>
{
    private readonly CustomAction[] _items = [.. items];

    public static CustomActionList Empty { get; } = new([]);

    public CustomAction this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<CustomAction> GetEnumerator() => ((IEnumerable<CustomAction>)_items).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public override bool Equals(object? obj) =>
        obj is IReadOnlyList<CustomAction> other && _items.SequenceEqual(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
            hash.Add(item);
        return hash.ToHashCode();
    }
}
