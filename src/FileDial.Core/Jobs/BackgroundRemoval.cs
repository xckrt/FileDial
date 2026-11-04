using System.Diagnostics;
using System.Globalization;
using System.Text;
using FileDial.Core.Compression;
using FileDial.Core.Localization;
using FileDial.Core.Text;
using Microsoft.ML.OnnxRuntime;

namespace FileDial.Core.Jobs;

/// <summary>
/// Модель для «Убрать фон»: IS-Net general use (Apache-2.0) из релизов rembg. Лежит там же, где модели речи:
/// %LOCALAPPDATA%\FileDial\models или папка models рядом с программой.
/// </summary>
public static class BackgroundModels
{
    public const string FileName = "isnet-general-use.onnx";
    public const string Url = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/" + FileName;
    public const long SizeBytes = 178_648_008;
    public const string Sha256 = "60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a";

    /// <summary>Размер для подписи — целыми мегабайтами: «179 МБ».</summary>
    public static string SizeLabel => SizeFormatter.Format((SizeBytes + 500_000) / 1_000_000 * 1_000_000);

    public static string NeedsModel => Lang.T.BgNeedsModel(SizeLabel);

    /// <summary>Первый найденный файл модели в указанных папках или null.</summary>
    public static string? Find(params string[] folders)
    {
        foreach (var folder in folders)
        {
            var path = Path.Combine(folder, FileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>Модель рядом с программой или скачанная.</summary>
    public static string? FindDefault() => Find(SpeechModels.BundledFolder, SpeechModels.DataFolder);

    /// <summary>Скачивание с проверкой размера и SHA-256; уже скачанная модель не качается заново.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, string folder,
        IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        var target = Path.Combine(folder, FileName);
        if (File.Exists(target) && new FileInfo(target).Length == SizeBytes)
            return target;

        var part = target + ".part";
        await VerifiedDownload.DownloadAsync(http, Url, SizeBytes, Sha256, part, progress, ct).ConfigureAwait(false);
        File.Move(part, target, overwrite: true);
        return target;
    }
}

/// <summary>
/// Подготовка входа нейросети и применение маски. Как в rembg (сессия DIS / IS-Net): картинка растягивается до
/// 1024×1024 без сохранения пропорций, пиксели делятся на 255, из каналов вычитается среднее; выход сети
/// приводится к 0…1 и растягивается обратно до размера картинки — это и есть прозрачность.
/// </summary>
public static class BackgroundMask
{
    public const int InputSize = 1024;

    // Вычитаемое среднее (R, G, B); делитель — 1. Проверено на фотографиях: см. BackgroundTests.
    internal static readonly float[] Mean = [0.485f, 0.456f, 0.406f];

    /// <summary>
    /// Тензор 1×3×size×size (каналы по отдельности, R, G, B) из картинки RGBA.
    /// Прозрачность исходника не учитывается — как в rembg, где картинка переводится в RGB.
    /// </summary>
    public static float[] ToTensor(byte[] rgba, int width, int height, int size = InputSize)
    {
        var tensor = new float[3 * size * size];
        var rows = Taps.For(height, size);
        var cols = Taps.For(width, size);
        var plane = size * size;
        Parallel.For(0, size, () => new float[width * 3], (y, _, line) =>
        {
            // Сначала по вертикали — строка исходной ширины, затем по горизонтали — в тензор.
            Array.Clear(line);
            for (var k = 0; k < rows.Count[y]; k++)
            {
                var weight = rows.Weight(y, k);
                var source = (rows.Start[y] + k) * width * 4;
                for (var x = 0; x < width; x++)
                {
                    line[x * 3] += weight * rgba[source + x * 4];
                    line[x * 3 + 1] += weight * rgba[source + x * 4 + 1];
                    line[x * 3 + 2] += weight * rgba[source + x * 4 + 2];
                }
            }
            for (var x = 0; x < size; x++)
            {
                float r = 0, g = 0, b = 0;
                for (var k = 0; k < cols.Count[x]; k++)
                {
                    var weight = cols.Weight(x, k);
                    var i = (cols.Start[x] + k) * 3;
                    r += weight * line[i];
                    g += weight * line[i + 1];
                    b += weight * line[i + 2];
                }
                var at = y * size + x;
                tensor[at] = r / 255f - Mean[0];
                tensor[plane + at] = g / 255f - Mean[1];
                tensor[2 * plane + at] = b / 255f - Mean[2];
            }
            return line;
        }, _ => { });
        return tensor;
    }

    /// <summary>Приводит выход сети к 0…1 (минимум → 0, максимум → 1). Ровный выход — сеть ничего не нашла: оставляем всё.</summary>
    public static float[] Normalize(ReadOnlySpan<float> raw)
    {
        var result = new float[raw.Length];
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in raw)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }
        var range = max - min;
        if (!(range > 1e-6f))
        {
            Array.Fill(result, 1f);
            return result;
        }
        for (var i = 0; i < raw.Length; i++)
            result[i] = (raw[i] - min) / range;
        return result;
    }

    /// <summary>Растягивает маску size×size до размера картинки и умножает на неё прозрачность (исходная прозрачность сохраняется).</summary>
    public static void Apply(byte[] rgba, int width, int height, float[] mask, int size)
    {
        var rows = Taps.For(size, height);
        var cols = Taps.For(size, width);
        Parallel.For(0, height, () => new float[size], (y, _, line) =>
        {
            Array.Clear(line);
            for (var k = 0; k < rows.Count[y]; k++)
            {
                var weight = rows.Weight(y, k);
                var source = (rows.Start[y] + k) * size;
                for (var x = 0; x < size; x++)
                    line[x] += weight * mask[source + x];
            }
            var row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                float m = 0;
                for (var k = 0; k < cols.Count[x]; k++)
                    m += cols.Weight(x, k) * line[cols.Start[x] + k];
                var alpha = rgba[row + x * 4 + 3] * Math.Clamp(m, 0f, 1f);
                rgba[row + x * 4 + 3] = (byte)MathF.Round(alpha);
            }
            return line;
        }, _ => { });
    }

    /// <summary>
    /// Веса линейной интерполяции по одной оси. При уменьшении окно расширяется на коэффициент уменьшения
    /// (как BILINEAR в Pillow): пиксели усредняются, а не выбрасываются — без «лесенки» на мелких деталях.
    /// </summary>
    internal sealed class Taps
    {
        public required int[] Start { get; init; }
        public required int[] Count { get; init; }
        public required float[] Weights { get; init; }
        public required int Stride { get; init; }

        public float Weight(int index, int k) => Weights[index * Stride + k];

        public static Taps For(int source, int target)
        {
            var scale = (double)source / target;
            var filterScale = Math.Max(scale, 1.0);
            var support = filterScale; // треугольный фильтр радиуса 1, растянутый при уменьшении
            var stride = (int)Math.Ceiling(support) * 2 + 1;
            var start = new int[target];
            var count = new int[target];
            var weights = new float[target * stride];
            for (var i = 0; i < target; i++)
            {
                var center = (i + 0.5) * scale;
                var from = Math.Max(0, (int)Math.Floor(center - support + 0.5));
                var to = Math.Min(source, (int)Math.Floor(center + support + 0.5));
                double sum = 0;
                var n = 0;
                for (var x = from; x < to && n < stride; x++, n++)
                {
                    var w = Math.Max(0, 1 - Math.Abs((x + 0.5 - center) / filterScale));
                    weights[i * stride + n] = (float)w;
                    sum += w;
                }
                if (sum <= 0)
                {
                    // На краю окно может не задеть ни одного центра — берём ближайший пиксель.
                    from = Math.Clamp((int)center, 0, source - 1);
                    n = 1;
                    weights[i * stride] = 1;
                    sum = 1;
                }
                for (var k = 0; k < n; k++)
                    weights[i * stride + k] = (float)(weights[i * stride + k] / sum);
                start[i] = from;
                count[i] = n;
            }
            return new Taps { Start = start, Count = count, Weights = weights, Stride = stride };
        }
    }
}

/// <summary>
/// Открытая нейросеть держится ещё минуту после последнего использования: пакет из нескольких картинок
/// загружает модель один раз, а программа в трее не занимает память постоянно.
/// </summary>
internal static class BackgroundSessions
{
    private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(60);
    private static readonly Lock Gate = new();
    private static InferenceSession? _session;
    private static string? _path;
    private static int _users;
    private static Timer? _release;

    public sealed class Lease(InferenceSession session, bool shared) : IDisposable
    {
        public InferenceSession Session => session;

        public void Dispose()
        {
            if (shared)
                Release();
            else
                session.Dispose();
        }
    }

    public static Lease Acquire(string modelPath)
    {
        lock (Gate)
        {
            _release?.Dispose();
            _release = null;
            if (_session is not null && _path != modelPath && _users == 0)
            {
                _session.Dispose();
                _session = null;
            }
            if (_session is null)
            {
                _session = Open(modelPath);
                _path = modelPath;
            }
            else if (_path != modelPath)
            {
                // Другая модель, пока первой кто-то пользуется, — отдельная сессия только для этой задачи.
                return new Lease(Open(modelPath), shared: false);
            }
            _users++;
            return new Lease(_session, shared: true);
        }
    }

    private static InferenceSession Open(string modelPath)
    {
        try
        {
            using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            return new InferenceSession(modelPath, options);
        }
        catch (OnnxRuntimeException ex)
        {
            throw new CompressionException(Lang.T.BgModelBroken, ex.Message);
        }
    }

    private static void Release()
    {
        lock (Gate)
        {
            if (--_users > 0)
                return;
            _release?.Dispose();
            _release = new Timer(_ =>
            {
                lock (Gate)
                {
                    if (_users > 0 || _session is null)
                        return;
                    _session.Dispose();
                    _session = null;
                    _path = null;
                }
            }, null, KeepAlive, Timeout.InfiniteTimeSpan);
        }
    }
}

/// <summary>
/// «Убрать фон»: PNG с прозрачным фоном рядом с исходником («фото_без_фона.png»). Картинку читает и пишет ffmpeg
/// (любой формат, поворот по EXIF), маску считает нейросеть IS-Net через ONNX Runtime на процессоре.
/// </summary>
public sealed class RemoveBackgroundJob(FfmpegTools tools, string? modelPath, string? outputDirectory = null) : IFileJob
{
    /// <summary>Больше — уменьшаем: память на картинку растёт быстро (40 Мп ≈ 160 МБ в RGBA).</summary>
    public const long MaxPixels = 40_000_000;

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        if (modelPath is null || !File.Exists(modelPath))
            throw new CompressionException(Lang.T.BgModelMissing);

        var sourceSize = new FileInfo(sourcePath).Length;
        progress?.Report(new JobProgress(Lang.T.JobStageAnalyzing, null));
        var info = await ProbeInfo.RunAsync(tools, sourcePath, ct).ConfigureAwait(false);
        if (!info.HasVideo)
            throw new CompressionException(Lang.T.JobCannotReadImage);

        var directory = outputDirectory ?? Path.GetDirectoryName(sourcePath)!;
        var pixels = (long)Math.Max(info.Width, 1) * Math.Max(info.Height, 1);
        if (OutputFolderCheck.WhyNot(directory, Math.Min(pixels, MaxPixels) * 4 + 10_000_000) is { } folderProblem)
            throw new CompressionException(folderProblem);

        progress?.Report(new JobProgress(Lang.T.BgStageReading, null));
        var scale = pixels > MaxPixels ? Math.Sqrt((double)MaxPixels / pixels) : 1.0;
        var (width, height, rgba) = await DecodeAsync(sourcePath, info, scale, ct).ConfigureAwait(false);

        progress?.Report(new JobProgress(Lang.T.BgStageLoadingModel, null));
        await Task.Run(() =>
        {
            using var lease = BackgroundSessions.Acquire(modelPath);
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress(Lang.T.BgStageRemoving, null));
            var mask = Predict(lease.Session, BackgroundMask.ToTensor(rgba, width, height), out var maskSize, ct);
            BackgroundMask.Apply(rgba, width, height, mask, maskSize);
        }, ct).ConfigureAwait(false);

        progress?.Report(new JobProgress(Lang.T.BgStageSaving, null));
        var tempOutput = Path.Combine(directory, $".filedial-{Guid.NewGuid():N}.tmp");
        try
        {
            await EncodePngAsync(rgba, width, height, tempOutput, ct).ConfigureAwait(false);
            var finalPath = OutputFiles.MoveToFreeName(tempOutput,
                () => OutputNameGenerator.Generate(sourcePath, Lang.T.BgSuffix, ".png", OutputNameGenerator.PathTaken, directory));
            progress?.Report(new JobProgress(Lang.T.JobStageDone, 1));
            var message = FileJobs.DoneMessage(sourceSize, finalPath);
            if (scale < 1)
                message += " " + Lang.T.BgDownscaled(width, height);
            return new JobResult(JobStatus.Done, message, finalPath);
        }
        finally
        {
            OutputFiles.TryDeleteFile(tempOutput);
        }
    }

    /// <summary>Прогон сети: тензор 1×3×N×N → маска N×N в 0…1 (первый выход сети).</summary>
    internal static float[] Predict(InferenceSession session, float[] tensor, out int maskSize, CancellationToken ct)
    {
        var size = BackgroundMask.InputSize;
        using var input = OrtValue.CreateTensorValueFromMemory(tensor, [1, 3, size, size]);
        using var run = new RunOptions();
        using var stop = ct.Register(() => run.Terminate = true);
        try
        {
            using var outputs = session.Run(run, [session.InputNames[0]], [input], [session.OutputNames[0]]);
            var output = outputs[0];
            var shape = output.GetTensorTypeAndShape().Shape;
            maskSize = (int)shape[^1];
            if (shape[^2] != maskSize)
                throw new CompressionException(Lang.T.BgModelBroken, $"shape {string.Join("x", shape)}");
            return BackgroundMask.Normalize(output.GetTensorDataAsSpan<float>()[..(maskSize * maskSize)]);
        }
        catch (OnnxRuntimeException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (OnnxRuntimeException ex)
        {
            throw new CompressionException(Lang.T.BgModelBroken, ex.Message);
        }
    }

    /// <summary>Первый кадр как RGBA через PAM (у него в заголовке размер — уже после поворота по EXIF).</summary>
    private async Task<(int Width, int Height, byte[] Rgba)> DecodeAsync(string source, ProbeInfo info, double scale, CancellationToken ct)
    {
        var k = scale.ToString("0.######", CultureInfo.InvariantCulture);
        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-i", source };
        args.AddRange(info.PictureArguments(scale < 1 ? $"scale=trunc(iw*{k}):trunc(ih*{k}):flags=area" : null));
        args.AddRange(["-frames:v", "1", "-pix_fmt", "rgba", "-c:v", "pam", "-f", "image2pipe", "-"]);

        (int, int, byte[])? image = null;
        var (exit, errors) = await FfmpegPipe.RunAsync(tools.Ffmpeg, args, null,
            async stdout => image = await Pam.ReadAsync(stdout, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        if (exit != 0 || image is null)
            throw new CompressionException(Lang.T.JobCannotReadImage, errors);
        return image.Value;
    }

    private async Task EncodePngAsync(byte[] rgba, int width, int height, string output, CancellationToken ct)
    {
        IReadOnlyList<string> args =
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{width}x{height}", "-i", "-",
            "-frames:v", "1", "-c:v", "png", "-f", "image2", "-update", "1", output,
        ];
        var (exit, errors) = await FfmpegPipe.RunAsync(tools.Ffmpeg, args,
            stdin => stdin.WriteAsync(rgba, ct).AsTask(), null, ct).ConfigureAwait(false);
        if (exit != 0 || !File.Exists(output))
            throw new CompressionException(Lang.T.JobFfmpegFailed(exit), errors);
    }
}

/// <summary>Чтение PAM (P7) из потока ffmpeg: заголовок с размером и пиксели RGBA.</summary>
internal static class Pam
{
    public static async Task<(int Width, int Height, byte[] Rgba)> ReadAsync(Stream input, CancellationToken ct)
    {
        int width = 0, height = 0, depth = 0, maxValue = 0;
        var header = new StringBuilder();
        var one = new byte[1];
        while (true)
        {
            if (await input.ReadAsync(one, ct).ConfigureAwait(false) == 0)
                throw new InvalidDataException("PAM: нет заголовка");
            if (one[0] != '\n')
            {
                if (header.Length > 200)
                    throw new InvalidDataException("PAM: слишком длинная строка заголовка");
                header.Append((char)one[0]);
                continue;
            }
            var line = header.ToString().Trim();
            header.Clear();
            if (line == "ENDHDR")
                break;
            var parts = line.Split(' ', 2);
            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                continue; // P7, TUPLTYPE
            switch (parts[0])
            {
                case "WIDTH": width = value; break;
                case "HEIGHT": height = value; break;
                case "DEPTH": depth = value; break;
                case "MAXVAL": maxValue = value; break;
            }
        }
        if (width <= 0 || height <= 0 || depth != 4 || maxValue != 255 || (long)width * height > int.MaxValue / 4)
            throw new InvalidDataException($"PAM: {width}x{height}, depth {depth}, max {maxValue}");

        var rgba = new byte[width * height * 4];
        await input.ReadExactlyAsync(rgba, ct).ConfigureAwait(false);
        return (width, height, rgba);
    }
}

/// <summary>ffmpeg с двоичными данными во входе или выходе (картинка целиком через канал, без временных файлов).</summary>
internal static class FfmpegPipe
{
    public static async Task<(int ExitCode, string Errors)> RunAsync(string ffmpeg, IReadOnlyList<string> args,
        Func<Stream, Task>? writeInput, Func<Stream, Task>? readOutput, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = writeInput is not null,
            RedirectStandardOutput = readOutput is not null,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        ct.ThrowIfCancellationRequested();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg не запустился");
        await using var kill = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });

        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        Exception? pipeError = null;
        try
        {
            if (writeInput is not null)
            {
                await using var stdin = process.StandardInput.BaseStream;
                await writeInput(stdin).ConfigureAwait(false);
            }
            if (readOutput is not null)
                await readOutput(process.StandardOutput.BaseStream).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            // ffmpeg завершился раньше времени — причину скажут код выхода и его сообщения.
            pipeError = ex;
        }
        if (readOutput is not null)
            await process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None).ConfigureAwait(false);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var text = (await errors.ConfigureAwait(false)).Trim();
        var tail = string.Join(Environment.NewLine, text.Split('\n').TakeLast(15).Select(l => l.TrimEnd('\r')));
        if (pipeError is not null && process.ExitCode == 0)
            return (-1, pipeError.Message);
        return (process.ExitCode, tail);
    }
}
