using System.Globalization;

namespace FileDial.Core.Compression;

/// <summary>Аргументы двухпроходного кодирования H.264 + AAC. Каждый аргумент — отдельный элемент списка.</summary>
public static class FfmpegArguments
{
    private static readonly string[] Common = ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-nostats", "-progress", "pipe:1"];

    public static IReadOnlyList<string> FirstPass(string input, CompressionPlan plan, string passLogPrefix)
    {
        var args = new List<string>(Common) { "-i", input, "-map", "0:v:0" };
        AddVideo(args, plan, passLogPrefix, pass: 1);
        args.AddRange(["-an", "-f", "null", "-"]);
        return args;
    }

    /// <param name="downmixToStereo">Свести 5.1/7.1 в стерео: 128 кбит/с на шесть каналов звучат плохо.</param>
    public static IReadOnlyList<string> SecondPass(string input, CompressionPlan plan, string passLogPrefix, string output,
        bool downmixToStereo = false)
    {
        var args = new List<string>(Common) { "-i", input, "-map", "0:v:0" };
        AddVideo(args, plan, passLogPrefix, pass: 2);
        if (plan.AudioKbps > 0)
        {
            args.AddRange(["-map", "0:a:0", "-c:a", "aac", "-b:a", Kbps(plan.AudioKbps)]);
            if (downmixToStereo)
                args.AddRange(["-ac", "2"]);
        }
        else
            args.Add("-an");
        args.AddRange(["-map_metadata", "-1", "-movflags", "+faststart", "-f", "mp4", output]);
        return args;
    }

    /// <summary>Фильтр уменьшения: длинная сторона = limit, вторая считается с сохранением пропорций и чётностью.</summary>
    public static string ScaleFilter(int longSide)
    {
        var l = longSide.ToString(CultureInfo.InvariantCulture);
        return $"scale='if(gte(iw,ih),{l},-2)':'if(gte(iw,ih),-2,{l})'";
    }

    private static void AddVideo(List<string> args, CompressionPlan plan, string passLogPrefix, int pass)
    {
        if (plan.ScaleLongSide is { } side)
            args.AddRange(["-vf", ScaleFilter(side)]);
        args.AddRange([
            "-c:v", "libx264",
            "-preset", "medium",
            "-b:v", Kbps(plan.VideoKbps),
            "-pix_fmt", "yuv420p",
            "-pass", pass.ToString(CultureInfo.InvariantCulture),
            "-passlogfile", passLogPrefix,
        ]);
    }

    private static string Kbps(int value) => value.ToString(CultureInfo.InvariantCulture) + "k";
}
