using System.Globalization;
using System.Windows.Media;
using System.Windows.Shapes;
using FileDial.Core.Menu;
using Path = System.Windows.Shapes.Path;

namespace FileDial.App.UI;

/// <summary>
/// Значки действий: линии в сетке 24×24 со скруглёнными концами — один стиль на все секторы.
/// Рисуются кодом (без файлов-картинок), цвет берут у подписи сектора, поэтому подсвечиваются вместе с ней.
/// </summary>
internal static class Icons
{
    private static string Circle(double cx, double cy, double r) => string.Create(CultureInfo.InvariantCulture,
        $"M{cx - r},{cy} A{r},{r} 0 1 0 {cx + r},{cy} A{r},{r} 0 1 0 {cx - r},{cy} Z ");

    private static readonly string Compress = "M4,4 L10,10 M10,5 V10 H5 M20,20 L14,14 M14,19 V14 H19";
    private static readonly string Shrink = "M14,10 L20,4 M14,4 V10 H20 M10,14 L4,20 M4,14 H10 V20";
    private static readonly string Scissors =
        Circle(6, 7, 2.6) + Circle(6, 17, 2.6) + "M8.2,8.5 L20,16 M8.2,15.5 L20,8";
    private static readonly string Convert = "M4,8 H18 M15,5 L18,8 L15,11 M20,16 H6 M9,13 L6,16 L9,19";
    private static readonly string Lines = "M4,6 H15 M4,10 H19 M4,14 H13 M4,18 H17";
    private static readonly string Folder = "M3,6.5 H9 L11,8.5 H21 V19 H3 Z";

    private static readonly Dictionary<string, string> Data = new()
    {
        // Видео
        [ActionIds.Compress25] = Compress,
        [ActionIds.Trim] = Scissors,
        [ActionIds.Gif] = "M4,5 H20 V19 H4 Z M8,5 V19 M16,5 V19 M4,9.5 H8 M4,14.5 H8 M16,9.5 H20 M16,14.5 H20",
        [ActionIds.ExtractAudio] = Circle(9, 17.5, 2.5) + Circle(17, 15.5, 2.5) + "M11.5,17.5 V5.5 L19.5,3.5 V15.5",
        [ActionIds.Frame] = "M4,5 H20 V19 H4 Z M4,16 L9,11 L13,15 L16,12 L20,16 " + Circle(15.5, 8.5, 1.4),
        [ActionIds.Subtitles] = "M3.5,5.5 H20.5 V16 H11 L7,19.5 V16 H3.5 Z M7,9.5 H13 M15,9.5 H17 M7,12.5 H10 M12,12.5 H17",
        [ActionIds.Mute] = "M4,9.5 H8 L13,5.5 V18.5 L8,14.5 H4 Z M16,9.5 L21,14.5 M21,9.5 L16,14.5",
        [ActionIds.Convert] = Convert,

        // Картинки
        [ActionIds.ImageConvert] = Convert,
        [ActionIds.Resize1920] = Shrink,
        [ActionIds.RotateRight] = "M19,12 A7,7 0 1 1 15.5,5.9 M15,2.5 L15.8,6 L12.3,6.8",
        [ActionIds.RotateLeft] = "M5,12 A7,7 0 1 0 8.5,5.9 M9,2.5 L8.2,6 L11.7,6.8",
        [ActionIds.Mirror] = "M12,3 V21 M9,6.5 L3.5,17.5 H9 Z M15,6.5 L20.5,17.5 H15 Z",
        [ActionIds.ImageCompress1Mb] = Compress,
        [ActionIds.RemoveBackground] = "M4,5 H20 V19 H4 Z M7.5,19 C7.5,14.5 16.5,14.5 16.5,19 " + Circle(12, 10.5, 2.6),
        [ActionIds.ImageToPdf] = "M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19 M9,13 H16 M9,17 H14",

        // Звук
        [ActionIds.AudioConvert] = Convert,
        [ActionIds.Normalize] = "M4,10 V14 M8,7 V17 M12,9.5 V14.5 M16,6 V18 M20,10 V14",
        [ActionIds.Denoise] = "M3,12 C5,6 7,6 9,12 S13,18 15,12 M17,12 H21 M3,19.5 H21",
        [ActionIds.SpeedUp] = "M3.5,6 L11.5,12 L3.5,18 Z M12.5,6 L20.5,12 L12.5,18 Z",
        [ActionIds.SlowDown] = "M6,6 L14,12 L6,18 Z M18,6 V18",
        [ActionIds.ToMono] = Circle(12, 12, 7.5) + Circle(12, 12, 2.2),
        [ActionIds.AudioTrim] = Scissors,
        [ActionIds.SpeechToText] = Lines,

        // Архивы
        [ActionIds.ExtractHere] = "M4,11 V20 H20 V11 M12,4 V15 M8,8 L12,4 L16,8",
        [ActionIds.ExtractToFolder] = Folder + "M12,10.5 V16.5 M9,13.5 L12,16.5 L15,13.5",
        [ActionIds.ExtractTo] = Folder + "M8,13.5 H8.01 M12,13.5 H12.01 M16,13.5 H16.01",
        [ActionIds.TestArchive] = Circle(12, 12, 8.5) + "M8,12.2 L11,15 L16.2,9.2",
        [ActionIds.ArchiveConvert] = Convert,
        [ActionIds.ArchiveContents] = "M9,6 H20 M9,12 H20 M9,18 H20 M4.5,6 H4.51 M4.5,12 H4.51 M4.5,18 H4.51",
        [ActionIds.ArchivePassword] = "M6,11 H18 V20.5 H6 Z M8.5,11 V8 A3.5,3.5 0 0 1 15.5,8 V11 M12,14.5 V17",
        [ActionIds.ArchiveSplit] = "M4,4 H10 V20 H4 Z M14,4 H20 V20 H14 Z",

        // Компактный круг
        [ActionIds.CompressToZip] = "M4,7.5 L12,3.5 L20,7.5 V16.5 L12,20.5 L4,16.5 Z M4,7.5 L12,11.5 L20,7.5 M12,11.5 V20.5",
        [ActionIds.JoinParts] = "M3.5,5 H9.5 V19 H3.5 Z M14.5,5 H20.5 V19 H14.5 Z M9.5,12 H14.5",
    };

    private static readonly Dictionary<string, Geometry> Cache = [];

    public static bool Has(string actionId) => Data.ContainsKey(actionId);

    /// <summary>Значок размером <paramref name="size"/> с цветом <paramref name="brush"/>; null — значка нет.</summary>
    public static Path? Create(string actionId, Brush brush, double size = 20)
    {
        if (!Data.TryGetValue(actionId, out var data))
            return null;
        if (!Cache.TryGetValue(actionId, out var geometry))
        {
            geometry = Geometry.Parse(data);
            geometry.Freeze();
            Cache[actionId] = geometry;
        }

        return new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.None,
            // Сетка 24×24 → нужный размер.
            RenderTransform = new ScaleTransform(size / 24, size / 24),
            IsHitTestVisible = false,
        };
    }
}
