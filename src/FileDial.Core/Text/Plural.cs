namespace FileDial.Core.Text;

public static class Plural
{
    /// <summary>Русское склонение по числу: 1 картинка, 2 картинки, 5 картинок, 11 картинок, 21 картинка.</summary>
    public static string Choose(long n, string one, string few, string many)
    {
        var abs = Math.Abs(n);
        var lastTwo = abs % 100;
        var last = abs % 10;
        if (lastTwo is >= 11 and <= 14) return many;
        return last switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    /// <summary>«5 картинок».</summary>
    public static string Count(long n, string one, string few, string many) => $"{n} {Choose(n, one, few, many)}";
}
