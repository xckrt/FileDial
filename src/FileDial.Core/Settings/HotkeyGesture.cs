using FileDial.Core.Localization;

namespace FileDial.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    // Значения совпадают с MOD_* у RegisterHotKey.
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>Сочетание клавиш: модификаторы + код виртуальной клавиши Windows.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    public static readonly HotkeyGesture Default = new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 'K');

    public override string ToString() => Format();

    /// <summary>«Ctrl+Alt+K».</summary>
    public string Format()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey) ?? $"0x{VirtualKey:X2}");
        return string.Join("+", parts);
    }

    /// <summary>Причина, почему сочетание не подходит, или null.</summary>
    public string? Problem()
    {
        if (KeyName(VirtualKey) is null)
            return Lang.T.JobHotkeyUseKey;
        // Без Ctrl/Alt/Win сочетание будет перехватывать обычный ввод текста.
        if ((Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
            return Lang.T.JobHotkeyAddModifier;
        return null;
    }

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = Default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var modifiers = HotkeyModifiers.None;
        int? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": modifiers |= HotkeyModifiers.Ctrl; break;
                case "ALT": modifiers |= HotkeyModifiers.Alt; break;
                case "SHIFT": modifiers |= HotkeyModifiers.Shift; break;
                case "WIN": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key is not null || VirtualKeyOf(raw) is not { } vk)
                        return false;
                    key = vk;
                    break;
            }
        }

        if (key is null)
            return false;
        gesture = new HotkeyGesture(modifiers, key.Value);
        return true;
    }

    /// <summary>Имя клавиши для букв, цифр и F1–F24; для остальных — null (такие не поддерживаем).</summary>
    public static string? KeyName(int vk) => vk switch
    {
        >= 'A' and <= 'Z' => ((char)vk).ToString(),
        >= '0' and <= '9' => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",
        _ => null,
    };

    private static int? VirtualKeyOf(string name)
    {
        var upper = name.ToUpperInvariant();
        if (upper.Length == 1 && upper[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
            return upper[0];
        if (upper.Length >= 2 && upper[0] == 'F' && int.TryParse(upper[1..], out var n) && n is >= 1 and <= 24)
            return 0x70 + n - 1;
        return null;
    }
}
