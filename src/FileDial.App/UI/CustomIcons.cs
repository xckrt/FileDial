using FileDial.Core.Custom;

namespace FileDial.App.UI;

/// <summary>Значки своих действий в той же сетке 24×24, что и <see cref="Icons"/>.</summary>
internal static class CustomIcons
{
    /// <summary>Окно консоли с приглашением «>_».</summary>
    private const string Console = "M3.5,5 H20.5 V19 H3.5 Z M7,9.5 L10,12 L7,14.5 M12,15 H16.5";

    /// <summary>Три точки — «ещё».</summary>
    private const string More = "M3.6,12 A1.4,1.4 0 1 0 6.4,12 A1.4,1.4 0 1 0 3.6,12 Z " +
                                "M10.6,12 A1.4,1.4 0 1 0 13.4,12 A1.4,1.4 0 1 0 10.6,12 Z " +
                                "M17.6,12 A1.4,1.4 0 1 0 20.4,12 A1.4,1.4 0 1 0 17.6,12 Z";

    public static string? DataFor(string actionId) =>
        actionId == CustomActionIds.More ? More
        : CustomActionIds.IsCustom(actionId) ? Console
        : null;
}
