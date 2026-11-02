namespace FileDial.Core.Text;

public static class NameShortener
{
    /// <summary>Сокращает имя многоточием в середине, сохраняя начало и расширение: «d5911ee03…7e18a.mp4».</summary>
    public static string Middle(string name, int maxLength)
    {
        if (maxLength < 5) throw new ArgumentOutOfRangeException(nameof(maxLength));
        if (name.Length <= maxLength)
            return name;

        var budget = maxLength - 1; // одно место под «…»
        var tail = budget / 2;
        var head = budget - tail;
        return string.Concat(name.AsSpan(0, head), "…", name.AsSpan(name.Length - tail));
    }
}

public static class DurationFormatter
{
    /// <summary>0:42, 12:05, 1:02:03.</summary>
    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(Math.Max(seconds, 0)));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }
}
