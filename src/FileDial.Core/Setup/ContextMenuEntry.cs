using System.Runtime.Versioning;
using Microsoft.Win32;

namespace FileDial.Core.Setup;

/// <summary>
/// Пункт «FileDial…» в контекстном меню Проводника — для файлов (<c>*</c>) и папок (<c>Directory</c>), только в HKCU.
/// Команда <c>"exe" --dial</c> открывает круг у курсора; MultiSelectModel=Player — один запуск на всё выделение.
/// В Windows 11 такие пункты видны в «Показать дополнительные параметры».
/// </summary>
[SupportedOSPlatform("windows")]
public static class ContextMenuEntry
{
    /// <summary>Раздел классов текущего пользователя (в тестах — свой).</summary>
    public const string DefaultClassesKey = @"Software\Classes";
    public const string VerbName = "FileDial";
    public const string DialArg = "--dial";

    private static readonly string[] Classes = ["*", "Directory"];

    private static string VerbKey(string classesKey, string cls) => $@"{classesKey}\{cls}\shell\{VerbName}";

    public static string Command(string exe) => $"\"{exe}\" {DialArg}";

    /// <summary>Создаёт или обновляет пункт (текст, значок, путь к exe). Повторный вызов ничего не портит.</summary>
    public static void Register(string classesKey, string exe, string text)
    {
        foreach (var cls in Classes)
        {
            using var verb = Registry.CurrentUser.CreateSubKey(VerbKey(classesKey, cls), writable: true);
            verb.SetValue("", text);
            verb.SetValue("Icon", $"\"{exe}\",0");
            verb.SetValue("MultiSelectModel", "Player");
            using var command = verb.CreateSubKey("command", writable: true);
            command.SetValue("", Command(exe));
        }
    }

    /// <summary>Exe, на который указывает пункт; null — пункта нет.</summary>
    public static string? TargetOf(string classesKey)
    {
        using var command = Registry.CurrentUser.OpenSubKey(VerbKey(classesKey, "*") + @"\command");
        if (command?.GetValue("") is not string value || !value.StartsWith('"'))
            return null;
        var end = value.IndexOf('"', 1);
        return end > 1 ? value[1..end] : null;
    }

    public static bool PointsTo(string classesKey, string exe) =>
        TargetOf(classesKey) is { } target && Installer.SamePath(target, exe);

    /// <summary>
    /// Убирает пункт (раздел «FileDial» — только наш, в том числе от переносной или переименованной копии).
    /// С <paramref name="exe"/> — лишь если пункт указывает на него: удаление одной копии не трогает другую.
    /// </summary>
    public static void Unregister(string classesKey, string? exe = null)
    {
        if (exe is not null && TargetOf(classesKey) is { } target && !Installer.SamePath(target, exe))
            return;
        foreach (var cls in Classes)
            Registry.CurrentUser.DeleteSubKeyTree(VerbKey(classesKey, cls), throwOnMissingSubKey: false);
    }
}
