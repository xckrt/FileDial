using System.Buffers.Binary;
using System.IO.Compression;

namespace FileDial.Core.TextFiles;

/// <summary>Один размер внутри значка: PNG как есть или DIB (BMP без заголовка файла) с маской прозрачности.</summary>
public sealed record IcoFrame(int Width, int Height, int BitCount, byte[] Data)
{
    public bool IsPng => Png.IsPng(Data);

    /// <summary>Кадр в PNG: PNG-кадр — без изменений, DIB — раскодируется.</summary>
    public byte[] ToPng() => IsPng ? Data : Dib.ToPng(Data);
}

/// <summary>Чтение и запись .ico. Записываем только PNG-кадры 32 бит (Windows Vista и новее).</summary>
public static class IcoFile
{
    public static readonly IReadOnlyList<int> StandardSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <exception cref="InvalidDataException">Не значок или файл повреждён.</exception>
    public static IReadOnlyList<IcoFrame> Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(bytes) != 0
                             || BinaryPrimitives.ReadUInt16LittleEndian(bytes[2..]) is not (1 or 2))
            throw new InvalidDataException("Not an ICO file");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        if (count == 0 || 6 + count * 16 > bytes.Length)
            throw new InvalidDataException("Bad ICO directory");

        var frames = new List<IcoFrame>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.Slice(6 + i * 16, 16);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (offset >= bytes.Length || size == 0 || size > bytes.Length - offset)
                throw new InvalidDataException("Bad ICO entry");
            var data = bytes.Slice((int)offset, (int)size).ToArray();
            int width = entry[0] == 0 ? 256 : entry[0], height = entry[1] == 0 ? 256 : entry[1];
            var bits = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
            // Настоящий размер — из заголовка самой картинки: каталог бывает неточным (и не умеет больше 256).
            if (Png.IsPng(data) && data.Length >= 24)
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16));
                height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20));
                if (bits == 0) bits = 32;
            }
            else if (data.Length >= 16)
            {
                width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
                height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8))) / 2;
                bits = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));
            }
            frames.Add(new IcoFrame(width, height, bits, data));
        }
        return frames;
    }

    /// <summary>Самый крупный кадр; при равных размерах — с большей глубиной цвета.</summary>
    public static IcoFrame Largest(IReadOnlyList<IcoFrame> frames) =>
        frames.OrderByDescending(f => (long)f.Width * f.Height).ThenByDescending(f => f.BitCount).First();

    /// <summary>По одному кадру на размер — лучший по глубине цвета, от меньшего к большему.</summary>
    public static IReadOnlyList<IcoFrame> BestPerSize(IReadOnlyList<IcoFrame> frames) =>
        frames.GroupBy(f => (f.Width, f.Height))
            .Select(g => g.OrderByDescending(f => f.BitCount).First())
            .OrderBy(f => (long)f.Width * f.Height)
            .ToList();

    /// <summary>Значок из готовых PNG (каждый — квадрат своего размера, до 256).</summary>
    public static byte[] Write(IReadOnlyList<byte[]> pngs)
    {
        var output = new MemoryStream();
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)pngs.Count);
        output.Write(header);

        var offset = 6 + 16 * pngs.Count;
        Span<byte> entry = stackalloc byte[16];
        foreach (var png in pngs)
        {
            var (width, height) = Png.Size(png);
            entry.Clear();
            entry[0] = (byte)(width >= 256 ? 0 : width);
            entry[1] = (byte)(height >= 256 ? 0 : height);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)png.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], (uint)offset);
            output.Write(entry);
            offset += png.Length;
        }
        foreach (var png in pngs)
            output.Write(png);
        return output.ToArray();
    }
}

/// <summary>Минимальный PNG: запись RGBA 8 бит и чтение размера.</summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsPng(ReadOnlySpan<byte> data) => data.StartsWith(Signature);

    public static (int Width, int Height) Size(ReadOnlySpan<byte> png)
    {
        if (!IsPng(png) || png.Length < 24)
            throw new InvalidDataException("Not a PNG");
        return (BinaryPrimitives.ReadInt32BigEndian(png[16..]), BinaryPrimitives.ReadInt32BigEndian(png[20..]));
    }

    /// <param name="rgba">Строки сверху вниз, 4 байта на точку.</param>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;  // бит на канал
        ihdr[9] = 6;  // RGBA
        Chunk(output, "IHDR", ihdr);

        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0); // без фильтра
                z.Write(rgba.Slice(y * stride, stride));
            }
        }
        Chunk(output, "IDAT", raw.GetBuffer().AsSpan(0, (int)raw.Length));
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        Span<byte> typeBytes = [(byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3]];
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32.Update(Crc32.Update(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        output.Write(buffer);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
        {
            var c = (uint)n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}

/// <summary>DIB-кадр значка: 1/4/8 бит с палитрой, 24 и 32 бита; прозрачность — из альфы или маски AND.</summary>
public static class Dib
{
    public static byte[] ToPng(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < 40)
            throw new InvalidDataException("Bad DIB");
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(dib[8..])) / 2;
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        if (width is <= 0 or > 1024 || height is <= 0 or > 1024 || headerSize < 40 || headerSize > dib.Length)
            throw new InvalidDataException("Bad DIB size");
        if (bits is not (1 or 4 or 8 or 24 or 32))
            throw new InvalidDataException($"Unsupported DIB depth {bits}");

        var paletteCount = bits <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bits) : 0;
        var palette = dib.Slice(headerSize, Math.Min(paletteCount * 4, dib.Length - headerSize));
        var pixels = headerSize + paletteCount * 4;
        var stride = (width * bits + 31) / 32 * 4;
        var maskStride = (width + 31) / 32 * 4;
        var maskStart = pixels + stride * height;
        if (pixels + stride * height > dib.Length)
            throw new InvalidDataException("Truncated DIB");
        var hasMask = maskStart + maskStride * height <= dib.Length;

        var rgba = new byte[width * height * 4];
        var anyAlpha = false;
        for (var y = 0; y < height; y++)
        {
            // Строки DIB — снизу вверх.
            var row = dib.Slice(pixels + (height - 1 - y) * stride, stride);
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                byte r, g, b, a = 255;
                if (bits >= 24)
                {
                    var p = x * (bits / 8);
                    b = row[p]; g = row[p + 1]; r = row[p + 2];
                    if (bits == 32) { a = row[p + 3]; anyAlpha |= a != 0; }
                }
                else
                {
                    var index = bits switch
                    {
                        8 => row[x],
                        4 => (row[x / 2] >> (x % 2 == 0 ? 4 : 0)) & 0x0F,
                        _ => (row[x / 8] >> (7 - x % 8)) & 1,
                    };
                    var p = index * 4;
                    if (p + 2 < palette.Length) { b = palette[p]; g = palette[p + 1]; r = palette[p + 2]; }
                    else { r = g = b = 0; }
                }
                rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = a;
            }
        }

        // Маска AND: 1 — прозрачно. У 32-битных кадров с настоящей альфой маска не нужна.
        if (hasMask && !(bits == 32 && anyAlpha))
        {
            for (var y = 0; y < height; y++)
            {
                var row = dib.Slice(maskStart + (height - 1 - y) * maskStride, maskStride);
                for (var x = 0; x < width; x++)
                {
                    var transparent = ((row[x / 8] >> (7 - x % 8)) & 1) != 0;
                    rgba[(y * width + x) * 4 + 3] = transparent ? (byte)0 : (byte)255;
                }
            }
        }
        return Png.Encode(width, height, rgba);
    }
}
