using System.Runtime.CompilerServices;
using FileDial.Core.Localization;

namespace FileDial.Core.Tests;

internal static class TestSetup
{
    /// <summary>
    /// Тесты сверяют тексты на русском — язык фиксируем, чтобы они не зависели от языка Windows на машине сборки.
    /// Английский проверяют отдельные тесты, временно переключая язык (<see cref="EnglishScope"/>).
    /// </summary>
    [ModuleInitializer]
    internal static void Init() => Lang.Set("ru");
}

/// <summary>
/// Английский на время теста. Язык — общий для процесса, поэтому такие тесты собраны в одну коллекцию
/// без параллельного запуска (<see cref="LanguageCollection"/>).
/// </summary>
internal sealed class EnglishScope : IDisposable
{
    public EnglishScope() => Lang.Set("en");

    public void Dispose() => Lang.Set("ru");
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "Language";
}
