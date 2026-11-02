namespace FileDial.Core.Selection;

/// <summary>Результат чтения выделения из Проводника или рабочего стола.</summary>
public sealed record SelectionResult
{
    private SelectionResult(IReadOnlyList<string> paths, string? error)
    {
        Paths = paths;
        Error = error;
    }

    public IReadOnlyList<string> Paths { get; }

    /// <summary>Текст для пользователя, если выделение прочитать не удалось.</summary>
    public string? Error { get; }

    public bool IsSuccess => Error is null;

    public static SelectionResult Success(IReadOnlyList<string> paths) => new(paths, null);

    public static SelectionResult Fail(string message) => new([], message);
}
