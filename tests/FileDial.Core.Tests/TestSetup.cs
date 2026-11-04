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

/// <summary>
/// Тест с кириллицей в путях через встроенный tar.exe или в старых zip: tar получает аргументы в ANSI-кодировке,
/// а имена в zip без флага UTF-8 — в OEM. Кириллицу они передают только в Windows с кириллической кодировкой
/// (1251/866). В английской Windows (например, на GitHub Actions) программа честно отвечает «не передаёт» —
/// там такие тесты пропускаем.
/// </summary>
public sealed class CyrillicWindowsFactAttribute : FactAttribute
{
    public CyrillicWindowsFactAttribute()
    {
        var text = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
        if (text.ANSICodePage != 1251 || text.OEMCodePage != 866)
            Skip = $"Нужна Windows с кириллической кодировкой (ANSI {text.ANSICodePage}, OEM {text.OEMCodePage})";
    }
}
