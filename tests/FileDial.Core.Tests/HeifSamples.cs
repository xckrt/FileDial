using System.Buffers.Binary;
using System.Text;
using FileDial.Core.Compression;

namespace FileDial.Core.Tests;

/// <summary>
/// Образцы HEIC/AVIF для тестов. Мультиплексора HEIF в ffmpeg нет, поэтому контейнер собираем сами (ISO/IEC 23008-12):
/// картинки кодирует ffmpeg (libx265 или libaom) в MP4, оттуда берём hvcC/av1C и данные кадра, а сверху —
/// элементы как в снимках iPhone: сетка «grid» из плиток (итоговый размер меньше сетки), irot/imir, миниатюра
/// и элемент Exif со своим (противоречащим irot) Orientation.
/// </summary>
internal static class HeifSamples
{
    public enum Codec { Hevc, Av1 }

    /// <param name="source">Источник lavfi для всей картинки (до поворота), например «testsrc2=s=700x500».</param>
    /// <param name="tile">Сторона плитки; 0 — без сетки, одна картинка целиком.</param>
    /// <param name="rotation">irot: поворот против часовой, в четвертях (0–3).</param>
    /// <param name="mirror">imir: null — нет, иначе значение поля axis.</param>
    /// <param name="thumbnail">Добавить миниатюру 64 px (первым элементом — чтобы «первая дорожка» была не та).</param>
    /// <param name="exifOrientation">Добавить элемент Exif с этим Orientation (читатели HEIF должны его игнорировать).</param>
    public static async Task WriteAsync(FfmpegTools tools, string path, string source, int width, int height,
        Codec codec = Codec.Hevc, int tile = 0, int rotation = 0, int? mirror = null, bool tenBit = false,
        bool thumbnail = false, int? exifOrientation = null)
    {
        var work = Path.Combine(Path.GetTempPath(), "FileDialTests", "heif-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var picture = Path.Combine(work, "full.png");
            await Ffmpeg(tools, ["-f", "lavfi", "-i", source, "-frames:v", "1", "-pix_fmt", "rgb24", "-update", "1", picture]);

            var items = new List<Item>();
            var properties = new List<byte[]>();
            int Property(byte[] box)
            {
                properties.Add(box);
                return properties.Count;
            }

            string[] Encoder() => codec == Codec.Hevc
                ? ["-c:v", "libx265", "-x265-params", "log-level=error", "-crf", "12", "-tag:v", "hvc1",
                    "-pix_fmt", tenBit ? "yuv420p10le" : "yuv420p"]
                : ["-c:v", "libaom-av1", "-still-picture", "1", "-crf", "10", "-pix_fmt", tenBit ? "yuv420p10le" : "yuv420p"];
            var itemType = codec == Codec.Hevc ? "hvc1" : "av01";
            var configType = codec == Codec.Hevc ? "hvcC" : "av1C";

            var transforms = new List<int>();
            if (rotation != 0) transforms.Add(Property(Box("irot", [(byte)(rotation & 3)])));
            if (mirror is { } axis) transforms.Add(Property(Box("imir", [(byte)axis])));

            int? thumbId = null;
            if (thumbnail)
            {
                var mp4 = Path.Combine(work, "thumb.mp4");
                await Ffmpeg(tools, ["-i", picture, "-vf", "scale=64:64", "-frames:v", "1", .. Encoder(), "-f", "mp4", mp4]);
                var (config, data) = ReadSingleSample(File.ReadAllBytes(mp4), configType);
                var props = new List<int> { Property(config), Property(Ispe(64, 64)) };
                props.AddRange(transforms);
                items.Add(new Item(itemType, data, props, Hidden: false));
                thumbId = items.Count;
            }

            var tileW = tile > 0 ? tile : width;
            var tileH = tile > 0 ? tile : height;
            var columns = (width + tileW - 1) / tileW;
            var rows = (height + tileH - 1) / tileH;
            var tileIds = new List<int>();
            int? tileConfig = null;
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
            {
                // Край сетки дополняем чёрным: декодер должен обрезать лишнее по размеру сетки.
                var mp4 = Path.Combine(work, $"t{r}_{c}.mp4");
                var crop = $"pad={columns * tileW}:{rows * tileH}:0:0:black,crop={tileW}:{tileH}:{c * tileW}:{r * tileH}";
                await Ffmpeg(tools, ["-i", picture, "-vf", crop, "-frames:v", "1", .. Encoder(), "-f", "mp4", mp4]);
                var (config, data) = ReadSingleSample(File.ReadAllBytes(mp4), configType);
                tileConfig ??= Property(config);
                var props = new List<int> { tileConfig.Value, Property(Ispe(tileW, tileH)) };
                if (tile == 0)
                    props.AddRange(transforms);
                items.Add(new Item(itemType, data, props, Hidden: tile > 0));
                tileIds.Add(items.Count);
            }

            var refs = new List<(string Type, int From, List<int> To)>();
            int primary;
            if (tile > 0)
            {
                byte[] grid = [0, 0, (byte)(rows - 1), (byte)(columns - 1), .. U16(width), .. U16(height)];
                items.Add(new Item("grid", grid, [Property(Ispe(width, height)), .. transforms], Hidden: false));
                primary = items.Count;
                refs.Add(("dimg", primary, tileIds));
            }
            else
            {
                primary = tileIds[0];
            }
            if (thumbId is { } t)
                refs.Add(("thmb", t, [primary]));
            if (exifOrientation is { } orientation)
            {
                // Как у iPhone: смещение до заголовка TIFF, «Exif\0\0», TIFF с одним тегом Orientation.
                byte[] tiff = [0x4D, 0x4D, 0, 0x2A, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1,
                    0, (byte)orientation, 0, 0, 0, 0, 0, 0];
                items.Add(new Item("Exif", [0, 0, 0, 6, .. Ascii("Exif"), 0, 0, .. tiff], [], Hidden: true));
                refs.Add(("cdsc", items.Count, [primary]));
            }

            File.WriteAllBytes(path, Build(codec, items, properties, refs, primary));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    private sealed record Item(string Type, byte[] Data, List<int> Properties, bool Hidden);

    private static async Task Ffmpeg(FfmpegTools tools, string[] args)
    {
        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", .. args],
            null, captureOutput: false, CancellationToken.None);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("ffmpeg: " + result.ErrorTail);
    }

    /// <summary>Из MP4 с одним кадром: блок настроек декодера (целиком, с заголовком) и данные кадра.</summary>
    private static (byte[] Config, byte[] Data) ReadSingleSample(byte[] mp4, string configType)
    {
        var config = FindBox(mp4, configType) ?? throw new InvalidOperationException("нет " + configType);
        var mdat = FindTopBox(mp4, "mdat") ?? throw new InvalidOperationException("нет mdat");
        return (mp4[config.Start..config.End], mp4[(mdat.Start + 8)..mdat.End]);
    }

    private static (int Start, int End)? FindTopBox(byte[] data, string type)
    {
        for (var pos = 0; pos + 8 <= data.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            if (Encoding.ASCII.GetString(data, pos + 4, 4) == type)
                return (pos, pos + size);
            pos += size;
        }
        return null;
    }

    /// <summary>Поиск блока по всему файлу перебором (у stsd/hvc1 есть поля до вложенных блоков).</summary>
    private static (int Start, int End)? FindBox(byte[] data, string type)
    {
        var bytes = Encoding.ASCII.GetBytes(type);
        for (var pos = 4; pos + 4 <= data.Length; pos++)
        {
            if (!data.AsSpan(pos, 4).SequenceEqual(bytes))
                continue;
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos - 4));
            if (size >= 8 && pos - 4 + size <= data.Length)
                return (pos - 4, pos - 4 + size);
        }
        return null;
    }

    private static byte[] Build(Codec codec, List<Item> items, List<byte[]> properties,
        List<(string Type, int From, List<int> To)> refs, int primary)
    {
        // Сначала считаем с нулевым смещением, потом — с настоящим: размеры блоков от смещений не зависят.
        var ftyp = codec == Codec.Hevc
            ? Box("ftyp", [.. Ascii("heic"), 0, 0, 0, 0, .. Ascii("mif1"), .. Ascii("heic")])
            : Box("ftyp", [.. Ascii("avif"), 0, 0, 0, 0, .. Ascii("avif"), .. Ascii("mif1"), .. Ascii("miaf")]);
        var meta = Meta(items, properties, refs, primary, 0);
        meta = Meta(items, properties, refs, primary, ftyp.Length + meta.Length + 8);
        var mdat = Box("mdat", [.. items.SelectMany(i => i.Data)]);
        return [.. ftyp, .. meta, .. mdat];
    }

    private static byte[] Meta(List<Item> items, List<byte[]> properties,
        List<(string Type, int From, List<int> To)> refs, int primary, int dataStart)
    {
        var hdlr = FullBox("hdlr", 0, 0, [0, 0, 0, 0, .. Ascii("pict"), .. new byte[12], 0]);
        var pitm = FullBox("pitm", 0, 0, U16(primary));

        var iloc = new List<byte> { 0x44, 0x00 }; // offset_size=4, length_size=4, base_offset_size=0
        iloc.AddRange(U16(items.Count));
        var offset = dataStart;
        for (var i = 0; i < items.Count; i++)
        {
            iloc.AddRange([.. U16(i + 1), 0, 0, .. U16(1), .. U32(offset), .. U32(items[i].Data.Length)]);
            offset += items[i].Data.Length;
        }

        var iinf = new List<byte>();
        iinf.AddRange(U16(items.Count));
        for (var i = 0; i < items.Count; i++)
            iinf.AddRange(FullBox("infe", 2, items[i].Hidden ? 1 : 0, [.. U16(i + 1), 0, 0, .. Ascii(items[i].Type), 0]));

        var iref = new List<byte>();
        foreach (var (type, from, to) in refs)
            iref.AddRange(Box(type, [.. U16(from), .. U16(to.Count), .. to.SelectMany(U16)]));

        var ipma = new List<byte>();
        var withProps = items.Select((item, i) => (item, id: i + 1)).Where(x => x.item.Properties.Count > 0).ToList();
        ipma.AddRange(U32(withProps.Count));
        foreach (var (item, id) in withProps)
            ipma.AddRange([.. U16(id), (byte)item.Properties.Count, .. item.Properties.Select(p => (byte)(0x80 | p))]);
        var iprp = Box("iprp", [.. Box("ipco", [.. properties.SelectMany(p => p)]), .. FullBox("ipma", 0, 0, [.. ipma])]);

        return FullBox("meta", 0, 0, [.. hdlr, .. pitm, .. FullBox("iloc", 0, 0, [.. iloc]), .. FullBox("iinf", 0, 0, [.. iinf]),
            .. (refs.Count > 0 ? FullBox("iref", 0, 0, [.. iref]) : []), .. iprp]);
    }

    private static byte[] Ispe(int width, int height) => FullBox("ispe", 0, 0, [.. U32(width), .. U32(height)]);

    private static byte[] Box(string type, byte[] payload) => [.. U32(payload.Length + 8), .. Ascii(type), .. payload];

    private static byte[] FullBox(string type, byte version, int flags, byte[] payload) =>
        Box(type, [version, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags, .. payload]);

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] U16(int v) => [(byte)(v >> 8), (byte)v];

    private static byte[] U32(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
}
