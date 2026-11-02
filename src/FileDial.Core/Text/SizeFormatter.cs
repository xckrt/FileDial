using System.Globalization;

namespace FileDial.Core.Text;

/// <summary>Размеры в десятичных единицах: 25 МБ = 25 000 000 байт, как и цель сжатия.</summary>
public static class SizeFormatter
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static string Format(long bytes)
    {
        if (bytes < 1_000)
            return $"{bytes} Б";
        if (bytes < 1_000_000)
            return (bytes / 1_000d).ToString("0.#", Russian) + " КБ";
        if (bytes < 1_000_000_000)
            return (bytes / 1_000_000d).ToString("0.#", Russian) + " МБ";
        return (bytes / 1_000_000_000d).ToString("0.##", Russian) + " ГБ";
    }
}
