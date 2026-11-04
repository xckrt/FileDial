using System.Text;
using System.Text.RegularExpressions;
using FileDial.Core.Compression;
using FileDial.Core.Localization;

namespace FileDial.Core.Custom;

/// <summary>Готовая к запуску команда: программа и отдельные аргументы (без склейки в строку для cmd.exe).</summary>
public sealed record CommandLine(string Program, IReadOnlyList<string> Arguments)
{
    /// <summary>Для показа человеку: аргументы с пробелами — в кавычках.</summary>
    public string Display => string.Join(" ", new[] { Program }.Concat(Arguments).Select(CustomCommand.QuoteForDisplay));
}

/// <summary>Разбор шаблона, подстановка переменных и проверка своего действия.</summary>
public static partial class CustomCommand
{
    public const string FfmpegVariable = "{ffmpeg}";

    /// <summary>Известные переменные в порядке, в котором их показывает подсказка.</summary>
    public static readonly IReadOnlyList<string> Variables = ["file", "dir", "name", "ext", "out", "ffmpeg"];

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex VariablePattern();

    /// <summary>
    /// Делит шаблон на аргументы по пробелам. Кавычки группируют и убираются; "" внутри кавычек — сама кавычка.
    /// Обратная косая черта не особая — это пути Windows.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? template)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var started = false; // «""» — пустой, но существующий аргумент
        var text = template ?? "";
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                started = true;
                continue;
            }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (started)
                    tokens.Add(current.ToString());
                current.Clear();
                started = false;
                continue;
            }
            current.Append(c);
            started = true;
        }
        if (started)
            tokens.Add(current.ToString());
        return tokens;
    }

    /// <summary>Есть ли незакрытая кавычка (тогда хвост шаблона — один аргумент, что почти наверняка опечатка).</summary>
    public static bool HasUnclosedQuote(string? template)
    {
        var inQuotes = false;
        var text = template ?? "";
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '"') continue;
            if (inQuotes && i + 1 < text.Length && text[i + 1] == '"') i++;
            else inQuotes = !inQuotes;
        }
        return inQuotes;
    }

    /// <summary>Значения переменных для файла.</summary>
    /// <param name="output">Путь результата для {out}; null — не используется.</param>
    /// <param name="ffmpeg">Найденный ffmpeg.exe; null — подставляется просто «ffmpeg.exe».</param>
    public static IReadOnlyDictionary<string, string> Values(string sourcePath, string? output, string? ffmpeg) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = sourcePath,
            ["dir"] = Path.GetDirectoryName(sourcePath) ?? "",
            ["name"] = Path.GetFileNameWithoutExtension(sourcePath),
            ["ext"] = Path.GetExtension(sourcePath).TrimStart('.'),
            ["out"] = output ?? "",
            ["ffmpeg"] = ffmpeg ?? "ffmpeg.exe",
        };

    /// <summary>Подстановка в один аргумент: значение с пробелами остаётся одним аргументом.</summary>
    public static string Expand(string token, IReadOnlyDictionary<string, string> values) =>
        VariablePattern().Replace(token, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    /// <summary>Переменные в фигурных скобках, которых мы не знаем (скорее всего опечатка).</summary>
    public static IReadOnlyList<string> UnknownVariables(string? template) =>
        VariablePattern().Matches(template ?? "")
            .Select(m => m.Groups[1].Value)
            .Where(v => !Variables.Contains(v, StringComparer.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

    private static bool Uses(CustomAction action, string variable) =>
        VariablePattern().Matches(action.Program + " " + action.Arguments)
            .Any(m => m.Groups[1].Value.Equals(variable, StringComparison.OrdinalIgnoreCase));

    /// <summary>Создаёт ли действие новый файл ({out}).</summary>
    public static bool UsesOutput(CustomAction action) => Uses(action, "out");

    public static bool UsesFfmpeg(CustomAction action) => Uses(action, "ffmpeg");

    /// <summary>Программа без кавычек вокруг пути.</summary>
    public static string ProgramText(CustomAction action) => action.Program.Trim().Trim('"').Trim();

    /// <summary>Расширение результата с точкой: из настроек или как у исходника.</summary>
    public static string OutputExtensionFor(CustomAction action, string sourcePath) =>
        action.OutputExtension.Trim().TrimStart('.') is { Length: > 0 } ext ? "." + ext : Path.GetExtension(sourcePath);

    /// <summary>Свободное имя результата рядом с исходником (или в папке из настроек): имя_суффикс.ext, имя_суффикс_2.ext…</summary>
    public static string OutputPath(CustomAction action, string sourcePath, string? directory, Func<string, bool> exists) =>
        OutputNameGenerator.Generate(sourcePath, action.OutputSuffix.Trim(), OutputExtensionFor(action, sourcePath), exists,
            directory);

    /// <summary>Команда для файла: программа и аргументы с подставленными значениями.</summary>
    public static CommandLine Build(CustomAction action, string sourcePath, string? output, string? ffmpeg)
    {
        var values = Values(sourcePath, output, ffmpeg);
        var program = Expand(ProgramText(action), values);
        return new CommandLine(program, Tokenize(action.Arguments).Select(t => Expand(t, values)).ToList());
    }

    /// <summary>Как будет выглядеть команда для примерного файла (в редакторе).</summary>
    public static string Preview(CustomAction action, string samplePath, string? ffmpeg)
    {
        var output = UsesOutput(action) ? OutputPath(action, samplePath, null, _ => false) : null;
        var command = Build(action, samplePath, output, ffmpeg);
        var program = ProgramLocator.Find(command.Program) ?? command.Program;
        return (command with { Program = program }).Display;
    }

    public static string QuoteForDisplay(string value) =>
        value.Length == 0 ? "\"\""
        : value.Any(c => char.IsWhiteSpace(c) || c == '"') ? "\"" + value.Replace("\"", "\"\"") + "\""
        : value;

    // --- Проверка ---

    private static readonly char[] BadNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Проблема, не зависящая от диска: пустые поля, не .exe, неизвестный тип файлов, плохие символы в имени
    /// результата. Такие записи отбрасываются при загрузке настроек.
    /// </summary>
    public static string? StructuralProblem(CustomAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Name))
            return Lang.T.CstNameRequired;
        if (action.Name.Trim().Length > CustomActions.MaxNameLength)
            return Lang.T.CstNameTooLong(CustomActions.MaxNameLength);
        if (!CustomActions.Kinds.Contains(action.AppliesTo))
            return Lang.T.CstChooseAppliesTo;
        if (action.AppliesTo == CustomActions.ByExtension && CustomActions.ParseExtensions(action.Extensions).Count == 0)
            return Lang.T.CstExtensionsRequired;
        var program = ProgramText(action);
        if (program.Length == 0)
            return Lang.T.CstProgramRequired;
        if (!program.Equals(FfmpegVariable, StringComparison.OrdinalIgnoreCase) && !ProgramLocator.IsRunnableName(program))
            return Lang.T.CstProgramNotExe;
        if ((action.OutputSuffix ?? "").IndexOfAny(BadNameChars) >= 0 || (action.OutputExtension ?? "").IndexOfAny(BadNameChars) >= 0)
            return Lang.T.CstBadOutputName;
        return null;
    }

    /// <summary>Полная проверка для редактора: ещё и программа на диске, опечатки в переменных, кавычки.</summary>
    public static string? Problem(CustomAction action)
    {
        if (StructuralProblem(action) is { } structural)
            return structural;
        var unknown = UnknownVariables(action.Program + " " + action.Arguments);
        if (unknown.Count > 0)
            return Lang.T.CstUnknownVariable("{" + unknown[0] + "}");
        if (HasUnclosedQuote(action.Arguments))
            return Lang.T.CstUnclosedQuote;
        var program = ProgramText(action);
        if (!program.Equals(FfmpegVariable, StringComparison.OrdinalIgnoreCase) && ProgramLocator.Find(program) is null)
            return Lang.T.CstProgramNotFound(program);
        return null;
    }
}

/// <summary>Поиск программы: полный путь или имя в системных папках и PATH. Запускаем только .exe и .com.</summary>
public static class ProgramLocator
{
    private static readonly string[] Runnable = [".exe", ".com"];

    /// <summary>
    /// Можно ли запустить напрямую. .bat и .cmd Windows передаёт cmd.exe, который заново разбирает строку —
    /// имя файла с &amp; или % превратилось бы в команду. Поэтому скрипты — только через их интерпретатор.
    /// </summary>
    public static bool IsRunnableName(string program)
    {
        var ext = Path.GetExtension(program.Trim('"'));
        return ext.Length == 0 || Runnable.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Полный путь к программе или null.</summary>
    public static string? Find(string program, string? pathVariable = null)
    {
        program = program.Trim().Trim('"');
        if (program.Length == 0 || !IsRunnableName(program))
            return null;
        var names = Path.HasExtension(program) ? [program] : Runnable.Select(e => program + e).ToArray();
        try
        {
            if (program.IndexOfAny(['\\', '/']) >= 0)
            {
                // Относительный путь зависел бы от текущей папки программы — принимаем только полный.
                if (!Path.IsPathFullyQualified(program))
                    return null;
                return names.FirstOrDefault(File.Exists) is { } full ? Path.GetFullPath(full) : null;
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        var directories = new List<string> { Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        directories.AddRange((pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim('"')));
        foreach (var directory in directories)
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(Environment.ExpandEnvironmentVariables(directory), name);
                    if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // Битый элемент PATH — пропускаем.
                }
            }
        }
        return null;
    }
}
