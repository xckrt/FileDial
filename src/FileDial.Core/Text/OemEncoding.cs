using System.Runtime.InteropServices;
using System.Text;

namespace FileDial.Core.Text;

/// <summary>
/// OEM-кодировка системы — та, в которой пишут консольные программы Windows и старые ZIP-архиваторы.
/// Берём у Windows (GetOEMCP), а не у CultureInfo.CurrentCulture: формат региона пользователя
/// (например, ru-RU) может не совпадать с языком системы для программ, не поддерживающих Юникод (en-US → 437).
/// </summary>
public static class OemEncoding
{
    /// <summary>Кодировка OEM или <paramref name="fallbackCodePage"/>, если её не удалось получить.</summary>
    public static Encoding Get(int fallbackCodePage)
    {
        try
        {
            var oem = OperatingSystem.IsWindows() ? (int)GetOEMCP() : 0;
            return Encoding.GetEncoding(oem is 0 or 1 ? fallbackCodePage : oem);
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(fallbackCodePage);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();
}
