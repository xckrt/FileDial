using FileDial.Core.Localization;

namespace FileDial.Core.Text;

/// <summary>Размеры в десятичных единицах: 25 МБ = 25 000 000 байт, как и цель сжатия. Единицы и запятая — по языку.</summary>
public static class SizeFormatter
{
    public static string Format(long bytes)
    {
        var t = Lang.T;
        if (bytes < 1_000)
            return $"{bytes} {t.Bytes}";
        if (bytes < 1_000_000)
            return (bytes / 1_000d).ToString("0.#", t.Culture) + " " + t.Kilobytes;
        if (bytes < 1_000_000_000)
            return (bytes / 1_000_000d).ToString("0.#", t.Culture) + " " + t.Megabytes;
        return (bytes / 1_000_000_000d).ToString("0.##", t.Culture) + " " + t.Gigabytes;
    }
}
