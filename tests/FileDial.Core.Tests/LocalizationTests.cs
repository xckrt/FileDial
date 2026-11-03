using System.Reflection;
using System.Text.RegularExpressions;
using FileDial.Core.Localization;
using FileDial.Core.Menu;

namespace FileDial.Core.Tests;

[Collection(LanguageCollection.Name)]
public class LocalizationTests
{
    /// <summary>Все строки каталога: свойства и методы (методы — с пробными аргументами).</summary>
    private static IEnumerable<(string Member, string Value)> AllStrings(Texts texts)
    {
        foreach (var member in typeof(Texts).GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            switch (member)
            {
                case PropertyInfo { PropertyType: var t } p when t == typeof(string):
                    yield return (p.Name, (string)p.GetValue(texts)!);
                    break;
                case MethodInfo { ReturnType: var t, IsSpecialName: false } m when t == typeof(string) && m.DeclaringType == typeof(Texts):
                    var args = m.GetParameters().Select(p => Sample(p.ParameterType)).ToArray();
                    // Числа 1, 2 и 5 — разные формы множественного числа.
                    foreach (var n in new object[] { 1, 2, 5 })
                        yield return ($"{m.Name}({n})", (string)m.Invoke(texts, args.Select(a => a is int ? n : a is long ? Convert.ToInt64(n) : a).ToArray())!);
                    break;
            }
        }
    }

    private static object Sample(Type type) =>
        type == typeof(string) ? "X" : type == typeof(int) ? 2 : type == typeof(long) ? 2L : type == typeof(double) ? 2.5
        : type == typeof(bool) ? true : type.IsValueType ? Activator.CreateInstance(type)! : throw new NotSupportedException(type.Name);

    [Fact]
    public void English_HasNoCyrillic_AndNoEmptyStrings()
    {
        var problems = AllStrings(new EnTexts())
            .Where(s => s.Member != nameof(Texts.NativeName))
            .Where(s => s.Value.Length == 0 || Regex.IsMatch(s.Value, "[А-Яа-яЁё]"))
            .Select(s => $"{s.Member}: «{s.Value}»")
            .ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Russian_HasNoEmptyStrings()
    {
        Assert.DoesNotContain(AllStrings(new RuTexts()), s => s.Value.Length == 0);
    }

    [Fact]
    public void Plurals()
    {
        var ru = new RuTexts();
        Assert.Equal(["1 картинка", "2 картинки", "5 картинок", "21 картинка", "11 картинок"],
            new[] { 1, 2, 5, 21, 11 }.Select(ru.Images));
        var en = new EnTexts();
        Assert.Equal(["1 image", "2 images", "21 images"], new[] { 1, 2, 21 }.Select(en.Images));
    }

    [Theory]
    [InlineData("ru", "ru")]
    [InlineData("en", "en")]
    [InlineData("de", null)] // неизвестный — по языку Windows
    [InlineData("auto", null)]
    public void Resolve(string setting, string? expected) =>
        Assert.Equal(expected ?? Lang.Detect(), Lang.Resolve(setting));

    [Fact]
    public void Menu_InEnglish()
    {
        using var _ = new EnglishScope();
        var actions = ActionCatalog.ForFile(@"C:\v\clip.mkv", 1_000_000_000);
        Assert.Equal("Compress to 25\u00A0MB", actions[0].Label);
        Assert.Equal("Format", actions[7].Label);
        Assert.Equal("Already MKV", actions[7].Children!.Single(c => c.Label == "MKV").DisabledReason);
        Assert.Equal("To WebM", actions[7].Children!.Single(c => c.Label == "WebM").Title);
        Assert.Equal("3 images", SelectionInterpreter.KindCount(FileKind.Image, 3));
        Assert.Equal("1.5 MB", Text.SizeFormatter.Format(1_500_000));
    }

    /// <summary>Каждый ключ <c>{l:T Key}</c> в XAML — свойство каталога (опечатка иначе видна только в окне).</summary>
    [Fact]
    public void Xaml_KeysExist()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "FileDial.slnx")))
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Не найден корень репозитория.");
        var keys = Directory.GetFiles(Path.Combine(root, "src", "FileDial.App"), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"\{l:T\s+(\w+)\}").Select(m => (File: Path.GetFileName(f), Key: m.Groups[1].Value)))
            .ToList();
        var missing = keys.Where(k => typeof(Texts).GetProperty(k.Key) is not { PropertyType: var t } || t != typeof(string))
            .Select(k => $"{k.File}: {k.Key}").ToList();
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }
}
