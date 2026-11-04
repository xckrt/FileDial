using FileDial.Core.Localization;
using FileDial.Core.Setup;

namespace FileDial.App.Settings;

/// <summary>
/// Пункт «Открыть в FileDial…» в контекстном меню Проводника — в HKCU, без прав администратора.
/// При запуске — <see cref="Sync"/> (ничей пункт забираем, чужой не трогаем); из настроек — <see cref="Set"/> (выбор пользователя).
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

    /// <summary>
    /// Сверка при запуске: включённый пункт ставим на этот exe, только если он ничей (нет пункта или его exe удалён)
    /// или уже наш (обновить текст). Пункт другой живой копии не перехватываем; выключенный — убираем лишь свой:
    /// иначе установленная и переносная копии отбирали бы пункт друг у друга при каждом запуске.
    /// </summary>
    public static void Sync(bool enabled)
    {
        try
        {
            var exe = ExePath();
            var target = ContextMenuEntry.TargetOf(ContextMenuEntry.DefaultClassesKey);
            if (!enabled)
                ContextMenuEntry.Unregister(ContextMenuEntry.DefaultClassesKey, exe);
            else if (target is null || !File.Exists(target) || Installer.SamePath(target, exe))
                ContextMenuEntry.Register(ContextMenuEntry.DefaultClassesKey, exe, Lang.T.CtxVerb);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            ErrorLog.Write("Контекстное меню", ex);
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
