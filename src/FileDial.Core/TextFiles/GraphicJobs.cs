using System.Globalization;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Documents;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;

namespace FileDial.Core.TextFiles;

/// <summary>Отрисовка в PNG через ffmpeg: SVG — декодером librsvg сразу в нужном размере, картинки — масштабом.</summary>
internal static class Raster
{
    /// <summary>Самая длинная сторона «В PNG» у SVG.</summary>
    public const int SvgLongSide = 1024;

    public static readonly IReadOnlyList<int> SvgSetSizes = [16, 32, 48, 64, 128, 256, 512, 1024];

    public static bool IsSvg(string path) => Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>Вписать в квадрат <paramref name="side"/>: длинная сторона = side, без нулевой ширины у узких картинок.</summary>
    private static string Fit(int side)
    {
        var s = side.ToString(CultureInfo.InvariantCulture);
        return $"scale=w='if(gte(iw,ih),{s},max(1,round(iw*{s}/ih)))':h='if(gte(iw,ih),max(1,round(ih*{s}/iw)),{s})':flags=lanczos";
    }

    /// <param name="square">Дополнить прозрачными полями до квадрата (кадр значка).</param>
    public static async Task RenderAsync(FfmpegTools tools, string source, int side, bool square, string output, CancellationToken ct)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "error" };
        if (IsSvg(source))
        {
            if (!SvgSupport.DecoderAvailable(tools))
                throw new CompressionException(Lang.T.TxtSvgNoDecoder + ".");
            // Размер задаём декодеру: вектор рисуется сразу чётко, а не растягивается из маленькой картинки.
            var (w, h) = SvgSupport.Fit(SvgSupport.Dimensions(source), side);
            args.AddRange(["-f", "svg_pipe"]);
            if (SvgSupport.Dimensions(source) is not null)
                args.AddRange(["-width", w.ToString(CultureInfo.InvariantCulture), "-height", h.ToString(CultureInfo.InvariantCulture)]);
        }
        var filter = Fit(side) + ",format=rgba";
        if (square)
        {
            var s = side.ToString(CultureInfo.InvariantCulture);
            filter += $",pad={s}:{s}:(ow-iw)/2:(oh-ih)/2:color=black@0";
        }
        args.AddRange(["-i", source, "-map", "0:v:0", "-frames:v", "1", "-map_metadata", "-1", "-vf", filter,
            "-c:v", "png", "-f", "image2", "-update", "1", output]);

        var result = await ProcessRunner.RunAsync(tools.Ffmpeg, args, null, captureOutput: false, ct).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
            throw new CompressionException(IsSvg(source) ? Lang.T.TxtSvgRenderFailed + "." : Lang.T.JobCannotReadImage, result.ErrorTail);
    }

    /// <summary>
    /// Несколько размеров: исходник рисуется один раз в самом крупном, остальные уменьшаются из него.
    /// Так быстрее — каждый запуск librsvg заново загружает шрифты (около 0,7 с на SVG с текстом).
    /// </summary>
    public static async Task RenderSizesAsync(FfmpegTools tools, string source, IReadOnlyList<int> sizes, bool square,
        Func<int, string> output, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var largest = sizes.Max();
        progress?.Report(new JobProgress(Lang.T.TxtStageRendering(largest), 0));
        await RenderAsync(tools, source, largest, square, output(largest), ct).ConfigureAwait(false);
        var done = 1;
        foreach (var size in sizes.Where(s => s != largest).OrderByDescending(s => s))
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new JobProgress(Lang.T.TxtStageRendering(size), (double)done++ / sizes.Count));
            await RenderAsync(tools, output(largest), size, square, output(size), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Готовую папку с результатами — под свободное имя «имя (PNG)».</summary>
    public static string FinishFolder(string staging, string parent, string sourcePath)
    {
        var folder = FreeNames.Child(parent, Lang.T.TxtPngFolder(Path.GetFileNameWithoutExtension(sourcePath)), isDirectory: true);
        Directory.Move(staging, folder);
        return folder;
    }
}

/// <summary>SVG → один крупный PNG (длинная сторона 1024 px).</summary>
public sealed class SvgToPngJob(FfmpegTools tools, string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, 20_000_000);
        progress?.Report(new JobProgress(Lang.T.TxtStageRendering(Raster.SvgLongSide), null));
        var temp = DocumentOutput.TempNear(directory);
        try
        {
            await Raster.RenderAsync(tools, sourcePath, Raster.SvgLongSide, square: false, temp, ct).ConfigureAwait(false);
            var output = DocumentOutput.Finish(temp, sourcePath, "", ".png", directory);
            var (w, h) = Png.Size(File.ReadAllBytes(output));
            return TextOutput.Done(progress, $"{w}×{h}.", output);
        }
        finally
        {
            OutputFiles.TryDeleteFile(temp);
        }
    }
}

/// <summary>SVG → набор PNG 16…1024 px в папке «имя (PNG)».</summary>
public sealed class SvgPngSizesJob(FfmpegTools tools, string? outputDirectory = null) : IFileJob
{
    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var parent = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(parent, 30_000_000);
        var staging = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            var stem = Path.GetFileNameWithoutExtension(sourcePath);
            var sizes = Raster.SvgSetSizes;
            await Raster.RenderSizesAsync(tools, sourcePath, sizes, square: false,
                size => Path.Combine(staging, $"{stem}_{size}px.png"), progress, ct).ConfigureAwait(false);
            var folder = Raster.FinishFolder(staging, parent, sourcePath);
            progress?.Report(new JobProgress(Lang.T.JobStageDone, 1));
            return new JobResult(JobStatus.Done, Lang.T.TxtPngSetDone(sizes.Count, Path.GetFileName(folder)), folder);
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(staging);
        }
    }
}

/// <summary>Картинка или SVG → .ico с размерами 16…256 (PNG внутри). Картинку не растягиваем сильнее, чем до 48 px.</summary>
public sealed class ToIcoJob(FfmpegTools tools, string? outputDirectory = null) : IFileJob
{
    /// <summary>Размеры значка для картинки с длинной стороной <paramref name="longSide"/> (0 — неизвестна).</summary>
    public static IReadOnlyList<int> SizesFor(int longSide) =>
        longSide <= 0 ? IcoFile.StandardSizes : IcoFile.StandardSizes.Where(s => s <= Math.Max(longSide, 48)).ToList();

    public async Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        var directory = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(directory, 10_000_000);

        IReadOnlyList<int> sizes = IcoFile.StandardSizes;
        if (!Raster.IsSvg(sourcePath))
        {
            progress?.Report(new JobProgress(Lang.T.JobStageAnalyzing, null));
            var info = await ProbeInfo.RunAsync(tools, sourcePath, ct).ConfigureAwait(false);
            if (!info.HasVideo)
                throw new CompressionException(Lang.T.JobCannotReadImage);
            sizes = SizesFor(Math.Max(info.Width, info.Height));
        }

        var work = DocumentOutput.WorkDirectory();
        try
        {
            string Frame(int size) => Path.Combine(work, $"{size}.png");
            await Raster.RenderSizesAsync(tools, sourcePath, sizes, square: true, Frame, progress, ct).ConfigureAwait(false);
            var pngs = new List<byte[]>();
            foreach (var size in sizes)
                pngs.Add(await File.ReadAllBytesAsync(Frame(size), ct).ConfigureAwait(false));
            var output = TextOutput.Save(sourcePath, outputDirectory, "", ".ico", IcoFile.Write(pngs));
            return TextOutput.Done(progress, Lang.T.TxtIcoDone(string.Join(", ", sizes)), output);
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(work);
        }
    }
}

/// <summary>Значок → PNG самого крупного размера (без ffmpeg).</summary>
public sealed class IcoToPngJob(string? outputDirectory = null) : IFileJob
{
    internal static IReadOnlyList<IcoFrame> ReadFrames(string path)
    {
        try
        {
            return IcoFile.Read(File.ReadAllBytes(path));
        }
        catch (InvalidDataException ex)
        {
            throw new CompressionException(Lang.T.TxtIcoUnreadable + ".", ex.Message);
        }
    }

    internal static byte[] ToPng(IcoFrame frame)
    {
        try
        {
            return frame.ToPng();
        }
        catch (InvalidDataException ex)
        {
            throw new CompressionException(Lang.T.TxtIcoUnreadable + ".", ex.Message);
        }
    }

    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.TxtStageConverting, null));
        var largest = IcoFile.Largest(ReadFrames(sourcePath));
        var output = TextOutput.Save(sourcePath, outputDirectory, "", ".png", ToPng(largest));
        return Task.FromResult(TextOutput.Done(progress, $"{largest.Width}×{largest.Height}.", output));
    }
}

/// <summary>Значок → все его размеры в PNG, в папке «имя (PNG)».</summary>
public sealed class IcoAllPngJob(string? outputDirectory = null) : IFileJob
{
    public Task<JobResult> RunAsync(string sourcePath, IProgress<JobProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new JobProgress(Lang.T.TxtStageConverting, null));
        var frames = IcoFile.BestPerSize(IcoToPngJob.ReadFrames(sourcePath));
        var parent = DocumentOutput.Directory(sourcePath, outputDirectory);
        DocumentOutput.CheckSpace(parent, new FileInfo(sourcePath).Length * 4 + 1_000_000);
        var staging = Path.Combine(parent, $".filedial-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            var stem = Path.GetFileNameWithoutExtension(sourcePath);
            foreach (var frame in frames)
            {
                ct.ThrowIfCancellationRequested();
                File.WriteAllBytes(Path.Combine(staging, $"{stem}_{frame.Width}x{frame.Height}.png"), IcoToPngJob.ToPng(frame));
            }
            var folder = Raster.FinishFolder(staging, parent, sourcePath);
            progress?.Report(new JobProgress(Lang.T.JobStageDone, 1));
            return Task.FromResult(new JobResult(JobStatus.Done, Lang.T.TxtPngSetDone(frames.Count, Path.GetFileName(folder)), folder));
        }
        finally
        {
            OutputFiles.TryDeleteDirectory(staging);
        }
    }
}
