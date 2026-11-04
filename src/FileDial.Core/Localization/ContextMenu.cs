namespace FileDial.Core.Localization;

// Пункт «FileDial…» в контекстном меню Проводника: сам пункт, галочки в настройках и установщике.

public abstract partial class Texts
{
    /// <summary>Текст пункта в контекстном меню Проводника.</summary>
    public abstract string CtxVerb { get; }
    /// <summary>Галочка в настройках («Общее»).</summary>
    public abstract string CtxSettingsOption { get; }
    /// <summary>Галочка в установщике («Дополнительно»): короткая — делит строку с автозапуском.</summary>
    public abstract string CtxSetupOption { get; }
    public abstract string CtxFailed { get; }
    /// <summary>Пункт меню при запущенной прежней версии, которая не слушает событие «открыть круг».</summary>
    public abstract string CtxOldCopyRunning { get; }
}

public sealed partial class RuTexts
{
    public override string CtxVerb => "Открыть в FileDial…";
    public override string CtxSettingsOption => "Пункт «Открыть в FileDial…» в меню Проводника";
    public override string CtxSetupOption => "Пункт в меню Проводника";
    public override string CtxFailed => "Не удалось изменить пункт в меню Проводника: нет доступа к реестру.";
    public override string CtxOldCopyRunning =>
        "Запущена прежняя версия FileDial — она не открывает круг из меню Проводника. Закройте её из трея и запустите FileDial снова.";
}

public sealed partial class EnTexts
{
    public override string CtxVerb => "Open in FileDial…";
    public override string CtxSettingsOption => "“Open in FileDial…” in the Explorer context menu";
    public override string CtxSetupOption => "Explorer context menu entry";
    public override string CtxFailed => "Could not change the Explorer context menu entry: no access to the registry.";
    public override string CtxOldCopyRunning =>
        "An older version of FileDial is running and can't open the dial from the Explorer menu. Quit it from the tray and start FileDial again.";
}
