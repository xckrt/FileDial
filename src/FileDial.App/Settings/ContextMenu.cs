using FileDial.Core.Localization;
using FileDial.Core.Setup;

namespace FileDial.App.Settings;

/// <summary>
/// Пункт «Открыть в FileDial…» в контекстном меню Проводника — в HKCU, без прав администратора.
/// Включённый пункт при каждом запуске переписывается на текущий exe (переезд между переносной и установленной копией).
/// </summary>
internal static class ContextMenu
{
    /// <summary>Включает (обновляет текст и путь) или убирает пункт. Возвращает текст ошибки или null.</summary>
    public static string? Set(bool enabled)
    {
        try
        {
            if (enabled)
                ContextMenuEntry.Register(ContextMenuEntry.DefaultClassesKey, ExePath(), Lang.T.CtxVerb);
            else
                ContextMenuEntry.Unregister(ContextMenuEntry.DefaultClassesKey);
            return null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            ErrorLog.Write("Контекстное меню", ex);
            return Lang.T.CtxFailed;
        }
    }

    /// <summary>Событие «открыть круг»: его ждёт запущенная копия (<see cref="App"/>), а ставит <c>FileDial.exe --dial</c>.</summary>
    public const string DialEventName = @"Local\FileDial.Dial";

    public static bool IsDialLaunch(IReadOnlyList<string> args) =>
        args.Any(a => string.Equals(a, ContextMenuEntry.DialArg, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Просит запущенную копию открыть круг. Право вывести окно вперёд Проводник дал этому процессу —
    /// передаём его дальше, иначе круг мог бы не получить фокус. false — копия не слушает (старая версия).
    /// </summary>
    public static bool RequestDial()
    {
        Interop.NativeMethods.AllowSetForegroundWindow(Interop.NativeMethods.ASFW_ANY);
        if (!EventWaitHandle.TryOpenExisting(DialEventName, out var dial))
            return false;
        using (dial)
            dial.Set();
        return true;
    }

    private static string ExePath() => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, Installer.ExeName);
}
