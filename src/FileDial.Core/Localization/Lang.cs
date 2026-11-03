using System.Globalization;

namespace FileDial.Core.Localization;

/// <summary>
/// Язык интерфейса. Все строки, которые видит пользователь (включая имена файлов-результатов), берутся из
/// <see cref="T"/>: <c>Lang.T.Something</c>. Строки собраны в <see cref="Texts"/>, по классу на язык:
/// не переведённая на какой-то язык строка — ошибка сборки, а не пустое место в окне.
/// </summary>
public static class Lang
{
    /// <summary>Значения настройки: «auto» — по языку Windows.</summary>
    public static readonly IReadOnlyList<string> Settings = ["auto", "ru", "en"];

    private static readonly Dictionary<string, Func<Texts>> Languages = new()
    {
        ["ru"] = () => new RuTexts(),
        ["en"] = () => new EnTexts(),
    };

    public static Texts T { get; private set; } = Languages[Detect()]();

    public static CultureInfo Culture => T.Culture;

    /// <summary>Язык сменился — открытые окна обновляют подписи.</summary>
    public static event Action? Changed;

    /// <summary>Язык Windows: русский — для русско-, белорусско- и казахскоязычной Windows, иначе английский.</summary>
    public static string Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "be" or "kk" ? "ru" : "en";

    /// <summary>Код языка для значения настройки («auto», «ru», «en», мусор → по Windows).</summary>
    public static string Resolve(string? setting) =>
        setting is not null && Languages.ContainsKey(setting) ? setting : Detect();

    public static void Set(string? setting)
    {
        var code = Resolve(setting);
        if (code == T.Code)
            return;
        T = Languages[code]();
        Changed?.Invoke();
    }
}

/// <summary>
/// Все строки интерфейса. Члены объявляются по областям в файлах этой папки: в каждом файле — абстрактные
/// члены здесь и их перевод в <see cref="RuTexts"/> и <see cref="EnTexts"/>. Новый язык — ещё один наследник.
/// </summary>
public abstract partial class Texts
{
    public abstract string Code { get; }
    public abstract CultureInfo Culture { get; }
    /// <summary>Название языка на нём самом — для выбора языка.</summary>
    public abstract string NativeName { get; }
}

public sealed partial class RuTexts : Texts
{
    public override string Code => "ru";
    public override CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("ru-RU");
    public override string NativeName => "Русский";

    /// <summary>«5 картинок».</summary>
    private static string N(long n, string one, string few, string many) => Text.Plural.Count(n, one, few, many);
}

public sealed partial class EnTexts : Texts
{
    public override string Code => "en";
    public override CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("en-US");
    public override string NativeName => "English";

    /// <summary>«1 image», «5 images».</summary>
    private static string N(long n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
