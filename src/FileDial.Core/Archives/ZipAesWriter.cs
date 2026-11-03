using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FileDial.Core.Archives;

/// <summary>
/// ZIP с шифрованием AES-256 в формате WinZip AE-2 (его открывают 7-Zip, WinRAR, Проводник Windows 11, libarchive).
/// .NET такие архивы писать не умеет, поэтому формат собран здесь по спецификации WinZip:
///  • ключи — PBKDF2-HMAC-SHA1 (1000 итераций) из пароля и случайной соли 16 байт: 32 байта ключа AES,
///    32 байта ключа HMAC и 2 байта проверки пароля;
///  • данные сжимаются Deflate и шифруются AES-256 в режиме CTR (счётчик little-endian, начиная с 1);
///  • после данных — 10 байт HMAC-SHA1 зашифрованного потока (код подлинности);
///  • метод сжатия в заголовках — 99, настоящий (8, Deflate) — в дополнительном поле 0x9901; CRC = 0 (AE-2).
/// Имена файлов в ZIP не шифруются (так устроен формат) — их видно без пароля.
/// </summary>
public sealed class ZipAesWriter : IDisposable
{
    private const int SaltLength = 16;     // AES-256
    private const int KeyLength = 32;
    private const int Iterations = 1000;
    private const int AuthCodeLength = 10;
    private const ushort AesMethod = 99;
    private const ushort Deflate = 8;
    private const ushort Utf8Names = 1 << 11;
    private const ushort Encrypted = 1;

    private readonly Stream _output;
    private readonly byte[] _password;
    private readonly List<CentralRecord> _central = [];

    private sealed record CentralRecord(byte[] Name, bool IsDirectory, uint DosTime, long CompressedSize,
        long UncompressedSize, long Offset);

    public ZipAesWriter(Stream output, string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Пустой пароль.", nameof(password));
        _output = output;
        _password = Encoding.UTF8.GetBytes(password);
    }

    /// <summary>Пустая папка (без данных и без шифрования — как это делают WinZip и 7-Zip).</summary>
    public void AddDirectory(string entryName, DateTime modified)
    {
        var name = Encoding.UTF8.GetBytes(entryName.TrimEnd('/') + "/");
        var offset = _output.Position;
        var time = DosTime(modified);
        WriteLocalHeader(name, encrypted: false, time, 0, 0);
        _central.Add(new CentralRecord(name, true, time, 0, 0, offset));
    }

    public void AddFile(string sourcePath, string entryName, CancellationToken ct)
    {
        var info = new FileInfo(sourcePath);
        // Без Zip64 размеры ограничены 4 ГБ — честно отказываем, чем писать битый архив.
        if (info.Length >= uint.MaxValue)
            throw new IOException($"Файл {info.Name} больше 4 ГБ — для архива с паролем это пока не поддерживается.");

        var name = Encoding.UTF8.GetBytes(entryName);
        var offset = _output.Position;
        var time = DosTime(info.LastWriteTime);
        WriteLocalHeader(name, encrypted: true, time, 0, 0); // размеры допишем, когда узнаем

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var derived = Rfc2898DeriveBytes.Pbkdf2(_password, salt, Iterations, HashAlgorithmName.SHA1, 2 * KeyLength + 2);
        var dataStart = _output.Position;
        _output.Write(salt);
        _output.Write(derived, 2 * KeyLength, 2); // проверка пароля

        using (var hmac = new HMACSHA1(derived[KeyLength..(2 * KeyLength)]))
        using (var aes = Aes.Create())
        {
            aes.Key = derived[..KeyLength];
            using var encryptor = new CtrEncryptStream(_output, aes, hmac);
            using (var deflate = new DeflateStream(encryptor, CompressionLevel.Optimal, leaveOpen: true))
            using (var input = info.OpenRead())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    deflate.Write(buffer, 0, read);
                }
            }
            encryptor.FlushFinal();
            hmac.TransformFinalBlock([], 0, 0);
            _output.Write(hmac.Hash!, 0, AuthCodeLength);
        }

        var compressed = _output.Position - dataStart;
        // Возвращаемся к локальному заголовку и вписываем размеры.
        var end = _output.Position;
        _output.Position = offset + 18;
        Span<byte> sizes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(sizes, (uint)compressed);
        BinaryPrimitives.WriteUInt32LittleEndian(sizes[4..], (uint)info.Length);
        _output.Write(sizes);
        _output.Position = end;

        _central.Add(new CentralRecord(name, false, time, compressed, info.Length, offset));
    }

    /// <summary>Центральный каталог и конец архива.</summary>
    public void Finish()
    {
        var start = _output.Position;
        foreach (var r in _central)
        {
            using var w = new BinaryWriter(_output, Encoding.UTF8, leaveOpen: true);
            w.Write(0x02014b50u);
            w.Write((ushort)(r.IsDirectory ? 20 : 51)); // кем создан
            w.Write((ushort)(r.IsDirectory ? 20 : 51)); // нужная версия: 5.1 — AES
            w.Write((ushort)(Utf8Names | (r.IsDirectory ? 0 : Encrypted)));
            w.Write(r.IsDirectory ? (ushort)0 : AesMethod);
            w.Write(r.DosTime);
            w.Write(0u); // CRC: в AE-2 не используется
            w.Write((uint)r.CompressedSize);
            w.Write((uint)r.UncompressedSize);
            w.Write((ushort)r.Name.Length);
            w.Write((ushort)(r.IsDirectory ? 0 : 11));
            w.Write((ushort)0); // комментарий
            w.Write((ushort)0); // номер диска
            w.Write((ushort)0); // внутренние атрибуты
            w.Write(r.IsDirectory ? 0x10u : 0x20u); // атрибуты Windows: папка / архивный
            w.Write((uint)r.Offset);
            w.Write(r.Name);
            if (!r.IsDirectory)
                WriteAesExtra(w);
        }
        var size = _output.Position - start;
        using (var w = new BinaryWriter(_output, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(0x06054b50u);
            w.Write((ushort)0);
            w.Write((ushort)0);
            w.Write((ushort)_central.Count);
            w.Write((ushort)_central.Count);
            w.Write((uint)size);
            w.Write((uint)start);
            w.Write((ushort)0);
        }
        _output.Flush();
    }

    private void WriteLocalHeader(byte[] name, bool encrypted, uint time, uint compressed, uint uncompressed)
    {
        using var w = new BinaryWriter(_output, Encoding.UTF8, leaveOpen: true);
        w.Write(0x04034b50u);
        w.Write((ushort)(encrypted ? 51 : 20));
        w.Write((ushort)(Utf8Names | (encrypted ? Encrypted : 0)));
        w.Write(encrypted ? AesMethod : (ushort)0);
        w.Write(time);
        w.Write(0u); // CRC
        w.Write(compressed);
        w.Write(uncompressed);
        w.Write((ushort)name.Length);
        w.Write((ushort)(encrypted ? 11 : 0));
        w.Write(name);
        if (encrypted)
            WriteAesExtra(w);
    }

    /// <summary>Поле 0x9901: версия AE-2, «AE», сила 3 (AES-256), настоящий метод — Deflate.</summary>
    private static void WriteAesExtra(BinaryWriter w)
    {
        w.Write((ushort)0x9901);
        w.Write((ushort)7);
        w.Write((ushort)2);
        w.Write((byte)'A');
        w.Write((byte)'E');
        w.Write((byte)3);
        w.Write(Deflate);
    }

    /// <summary>Время в формате MS-DOS (точность 2 с; до 1980 года — 1 января 1980).</summary>
    public static uint DosTime(DateTime t)
    {
        if (t.Year < 1980) t = new DateTime(1980, 1, 1);
        var time = (t.Hour << 11) | (t.Minute << 5) | (t.Second / 2);
        var date = ((t.Year - 1980) << 9) | (t.Month << 5) | t.Day;
        return (uint)(date << 16 | time);
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// AES-CTR поверх потока: шифрует всё, что в него пишут, и одновременно считает HMAC шифртекста.
    /// Счётчик — 16 байт little-endian, начиная с 1 (так у WinZip; не путать с NIST CTR).
    /// </summary>
    private sealed class CtrEncryptStream(Stream output, Aes aes, HMACSHA1 hmac) : Stream
    {
        private readonly byte[] _counter = new byte[16];
        private readonly byte[] _keystream = new byte[16];
        private int _keystreamUsed = 16;

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            var encrypted = new byte[data.Length];
            for (var i = 0; i < data.Length; i++)
            {
                if (_keystreamUsed == 16)
                    NextBlock();
                encrypted[i] = (byte)(data[i] ^ _keystream[_keystreamUsed++]);
            }
            hmac.TransformBlock(encrypted, 0, encrypted.Length, null, 0);
            output.Write(encrypted);
        }

        private void NextBlock()
        {
            for (var i = 0; i < 16 && ++_counter[i] == 0; i++) { }
            aes.EncryptEcb(_counter, _keystream, PaddingMode.None);
            _keystreamUsed = 0;
        }

        public void FlushFinal() => output.Flush();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
