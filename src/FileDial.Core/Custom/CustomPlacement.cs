using FileDial.Core.Localization;
using FileDial.Core.Menu;
using FileDial.Core.Selection;
using FileDial.Core.Text;

namespace FileDial.Core.Custom;

/// <summary>
/// Где в круге показать свои действия. Круг всегда из восьми секторов (клавиши 1–8), и у большинства типов он полон.
/// Правило:
/// <list type="bullet">
/// <item>есть свободные секторы — свои действия встают в них по порядку;</item>
/// <item>не помещаются — последний обычный (не группа) сектор становится группой «Ещё ›», в веере которой сначала
/// вытесненное действие, затем свои. Место «Ещё» у типа файла всегда одно и то же — привычка к клавишам не ломается;</item>
/// <item>в веере не больше <see cref="MaxInFan"/> секторов: во внешнем кольце цифры 1–9.</item>
/// </list>
/// Компактный круг (файлы без своего круга) с подходящими действиями становится обычным: «Сжать в ZIP» и свои действия.
/// </summary>
public static class CustomPlacement
{
    /// <summary>Сколько секторов во веере: цифры 1–9 во внешнем кольце (по углу поместилось бы 12).</summary>
    public const int MaxInFan = 9;

    /// <summary>Подпись сектора короче полного названия: в секторе две-три строки.</summary>
    public const int MaxLabelLength = 28;

    /// <summary>Добавляет к меню подходящие свои действия (подходящие для всех выбранных файлов).</summary>
    public static HotkeyOutcome Apply(HotkeyOutcome outcome, IReadOnlyList<CustomAction>? customs, IFileProbe probe)
    {
        if (customs is not { Count: > 0 } || outcome is not HotkeyOutcome.ShowMenu { Menu: var menu }
                                         || menu.SourcePaths.Count == 0)
            return outcome;
        var matching = customs.Where(c => menu.SourcePaths.All(c.Matches)).Select(ToDialAction).ToList();
        if (matching.Count == 0)
            return outcome;

        if (menu.Kind == DialMenuKind.Actions)
            return new HotkeyOutcome.ShowMenu(menu with { Actions = Place(menu.Actions, matching) });

        // Компактный круг: только там, где предлагается ZIP по файлам (не папки, не части .001).
        if (menu.QuickAction is not { Id: ActionIds.CompressToZip } zip
            || !menu.SourcePaths.All(p => probe.Probe(p).Kind == FileStateKind.File))
            return outcome;
        return new HotkeyOutcome.ShowMenu(menu with
        {
            Kind = DialMenuKind.Actions,
            Actions = Place([zip], matching),
            QuickAction = null,
        });
    }

    /// <summary>Раскладка встроенных и своих действий по восьми секторам.</summary>
    public static IReadOnlyList<DialAction> Place(IReadOnlyList<DialAction> builtIn, IReadOnlyList<DialAction> customs)
    {
        const int sectors = ActionCatalog.SectorCount;
        if (customs.Count == 0)
            return builtIn;
        if (builtIn.Count + customs.Count <= sectors)
            return [.. builtIn, .. customs];

        if (builtIn.Count < sectors)
        {
            // Свободные секторы, кроме последнего, — своим действиям напрямую; остальные — в «Ещё ›».
            var direct = sectors - 1 - builtIn.Count;
            return [.. builtIn, .. customs.Take(direct), More(customs.Skip(direct))];
        }

        // Круг полон: «Ещё ›» встаёт на место последнего обычного сектора (вложенных групп не бывает).
        var index = -1;
        for (var i = builtIn.Count - 1; i >= 0; i--)
        {
            if (!builtIn[i].IsGroup)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return builtIn;
        var result = builtIn.ToList();
        result[index] = More([builtIn[index], .. customs]);
        return result;
    }

    private static DialAction More(IEnumerable<DialAction> children) =>
        DialAction.Group(CustomActionIds.More, Lang.T.CstMore, children.Take(MaxInFan).ToList());

    /// <summary>Сектор для своего действия. Программы нет на диске — сектор неактивен с причиной.</summary>
    public static DialAction ToDialAction(CustomAction action)
    {
        var program = CustomCommand.ProgramText(action);
        var missing = !program.Equals(CustomCommand.FfmpegVariable, StringComparison.OrdinalIgnoreCase)
                      && ProgramLocator.Find(program) is null;
        return new DialAction(CustomActionIds.For(action), NameShortener.Middle(action.Name, MaxLabelLength), !missing,
            missing ? Lang.T.CstProgramNotFound(program) : null)
        {
            Caption = action.Name,
        };
    }
}
