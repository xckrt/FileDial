using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Selection;

namespace FileDial.Core.Tests;

public class FileKindTests
{
    [Theory]
    [InlineData(@"C:\a.MP4", FileKind.Video)]
    [InlineData(@"C:\a.jpeg", FileKind.Image)]
    [InlineData(@"C:\a.WebP", FileKind.Image)]
    [InlineData(@"C:\a.gif", FileKind.Image)]
    [InlineData(@"C:\a.flac", FileKind.Audio)]
    [InlineData(@"C:\a.Opus", FileKind.Audio)]
    [InlineData(@"C:\a.pdf", FileKind.Pdf)]
    [InlineData(@"C:\a", FileKind.Other)]
    public void DetectsKindByExtension(string path, FileKind expected)
    {
        Assert.Equal(expected, FileKinds.Of(path));
    }
}

public class ImageActionTests
{
    private static ProbeInfo Picture(int w, int h) => new(0, true, false, w, h);

    private static DialAction Resize(string path, ProbeInfo? media) =>
        ActionCatalog.ForFile(path, 1, media).Single(a => a.Id == ActionIds.Resize1920);

    [Fact]
    public void Png_HasConvertGroupFirst_AndOtherSectors()
    {
        var actions = ActionCatalog.ForFile(@"C:\p\скрин.png", 500_000, Picture(3000, 2000));

        Assert.Equal(8, actions.Count);
        Assert.Equal(["Формат", "Уменьшить до 1920\u00A0px", "Повернуть вправо", "Повернуть влево", "Отразить",
            "Сжать до 1\u00A0МБ", "Убрать фон", "В PDF"], actions.Select(a => a.Label));
        Assert.True(actions[0].IsGroup);
        Assert.True(actions[0].IsEnabled);
        Assert.Equal("Файл уже меньше 1 МБ", actions[5].DisabledReason);
        Assert.Equal(ActionIds.SetupBackground, actions[6].FixActionId); // убрать фон: без модели — к её загрузке
        Assert.True(actions[7].IsEnabled); // В PDF
    }

    [Fact]
    public void ConvertGroup_ListsSevenFormats_OwnFormatDisabled()
    {
        var group = ActionCatalog.ForFile(@"C:\p\скрин.png", 1)[0];

        Assert.Equal(["JPG", "PNG", "WebP", "BMP", "TIFF", "GIF", "ICO"], group.Children!.Select(c => c.Label));
        var png = group.Children!.Single(c => c.Label == "PNG");
        Assert.False(png.IsEnabled);
        Assert.Equal("Файл уже в PNG", png.DisabledReason);
        Assert.Equal("В JPG", group.Children![0].Title);
        Assert.Equal(ActionIds.ToJpg, group.Children![0].Id);
    }

    [Theory]
    [InlineData(@"C:\p\a.jpg", "JPG")]
    [InlineData(@"C:\p\a.JPEG", "JPG")]
    [InlineData(@"C:\p\a.tif", "TIFF")]
    public void FormatSynonyms_DisableOwnFormat(string path, string label)
    {
        var child = ActionCatalog.ForFile(path, 1)[0].Children!.Single(c => c.Label == label);
        Assert.Equal($"Файл уже в {label}", child.DisabledReason);
    }

    [Fact]
    public void SmallImage_ResizeDisabledWithSize()
    {
        var resize = Resize(@"C:\p\a.png", Picture(1200, 800));
        Assert.False(resize.IsEnabled);
        Assert.Equal("Уже не больше 1920 px (1200×800)", resize.DisabledReason);
    }

    [Fact]
    public void ExactlyLimit_ResizeDisabled_OneMore_Enabled()
    {
        Assert.False(Resize(@"C:\p\a.png", Picture(1920, 1080)).IsEnabled);
        Assert.True(Resize(@"C:\p\a.png", Picture(1080, 1921)).IsEnabled);
    }

    [Fact]
    public void UnknownSize_ResizeStaysEnabled()
    {
        Assert.True(Resize(@"C:\p\a.png", null).IsEnabled);
    }
}

public class AudioActionTests
{
    [Fact]
    public void Mp3_HasConvertGroup_AndProcessingSectors()
    {
        var actions = ActionCatalog.ForFile(@"C:\m\песня.mp3", 5_000_000);

        Assert.Equal(["Формат", "Выровнять громкость", "Убрать шум", "Ускорить ×1,5", "Замедлить ×0,75",
            "В моно", "Обрезать", "В текст"], actions.Select(a => a.Label));
        Assert.Equal(["MP3", "M4A", "WAV", "FLAC", "OGG", "Opus"], actions[0].Children!.Select(c => c.Label));
        Assert.Equal("Файл уже в MP3", actions[0].Children![0].DisabledReason);
        Assert.DoesNotContain(actions, a => a.DisabledReason == ActionCatalog.NotInPrototype);
        Assert.Equal(Speech.NoModel, actions.Single(a => a.Id == ActionIds.SpeechToText).DisabledReason);
    }

    [Fact]
    public void MonoSource_ToMonoDisabled()
    {
        var mono = new ProbeInfo(10, false, true, 0, 0, AudioChannels: 1);
        Assert.Equal("Звук уже моно",
            ActionCatalog.ForFile(@"C:\m\a.wav", 1, mono).Single(a => a.Id == ActionIds.ToMono).DisabledReason);
        Assert.True(ActionCatalog.ForFile(@"C:\m\a.wav", 1).Single(a => a.Id == ActionIds.ToMono).IsEnabled);
    }

    [Fact]
    public void GroupWithEveryChildDisabled_IsDisabledWithReason()
    {
        var group = DialAction.Group("g", "Г", [new DialAction("a", "A", false, "причина")]);
        Assert.False(group.IsEnabled);
        Assert.Equal("причина", group.DisabledReason);
    }
}
public class InterpreterForImagesAndAudioTests
{
    private sealed class FakeFile(long size) : IFileProbe
    {
        public FileState Probe(string path) => new(FileStateKind.File, size);
    }

    private sealed class FakeMedia(ProbeInfo info) : IMediaProbe
    {
        public ProbeInfo? TryProbe(string path) => info;
    }

    private static DialMenu Menu(string path, long size, ProbeInfo info) =>
        Assert.IsType<HotkeyOutcome.ShowMenu>(SelectionInterpreter.Interpret(
            SelectionResult.Success([path]), new FakeFile(size), new FakeMedia(info))).Menu;

    [Fact]
    public void Image_ShowsResolution()
    {
        var menu = Menu(@"C:\p\фото.jpg", 3_400_000, new ProbeInfo(0, true, false, 4032, 3024));
        Assert.Equal(DialMenuKind.Actions, menu.Kind);
        Assert.Equal("3,4 МБ · 4032×3024", menu.Subtitle);
    }

    [Fact]
    public void Audio_ShowsDuration()
    {
        var menu = Menu(@"C:\m\трек.flac", 30_000_000, new ProbeInfo(245, false, true, 0, 0, 2, 44100));
        Assert.Equal("30 МБ · 4:05", menu.Subtitle);
        Assert.Equal(8, menu.Actions.Count);
    }
}

public class ConversionSpecTests
{
    private static readonly ProbeInfo Picture = new(0, true, false, 640, 480);
    private static readonly ProbeInfo Sound = new(10, false, true, 0, 0, 2, 44100);

    [Fact]
    public void EveryEnabledActionIsImplemented_StubsAreNot()
    {
        foreach (var path in new[] { @"C:\a.mp4", @"C:\a.bmp", @"C:\a.flac" })
        {
            foreach (var action in ActionCatalog.Flatten(ActionCatalog.ForFile(path, 80_000_000)))
            {
                if (action.DisabledReason == ActionCatalog.NotInPrototype)
                    Assert.False(FileJobs.IsImplemented(action.Id), action.Id);
                else
                    Assert.True(FileJobs.IsImplemented(action.Id), action.Id);
            }
        }
    }

    [Fact]
    public void ToPng_TakesFirstFrameOfFirstVideoStream()
    {
        var args = ConversionSpecs.For(ActionIds.ToPng)!.Arguments(@"C:\a.gif", Picture).ToList();
        Assert.Equal("0:v:0", args[args.IndexOf("-map") + 1]);
        Assert.Equal("1", args[args.IndexOf("-frames:v") + 1]);
        Assert.Equal("png", args[args.IndexOf("-c:v") + 1]);
    }

    [Fact]
    public void ToJpg_PutsWhiteBackgroundUnderTransparency()
    {
        var args = ConversionSpecs.For(ActionIds.ToJpg)!.Arguments(@"C:\a.png", Picture).ToList();
        var graph = args[args.IndexOf("-filter_complex") + 1];

        Assert.Contains("color=c=white:s=640x480", graph);
        Assert.EndsWith("[out]", graph);
        Assert.Equal("[out]", args[args.IndexOf("-map") + 1]);
        Assert.Single(args, a => a == "-map");
        Assert.Equal("mjpeg", args[args.IndexOf("-c:v") + 1]);
    }

    [Fact]
    public void ToJpg_WithoutSize_SkipsBackground()
    {
        var args = ConversionSpecs.For(ActionIds.ToJpg)!.Arguments(@"C:\a.png", new ProbeInfo(0, true, false, 0, 0));
        Assert.DoesNotContain("-filter_complex", args);
        Assert.Contains("0:v:0", args);
    }

    [Theory]
    [InlineData(@"C:\a.jpg", ".jpg")]
    [InlineData(@"C:\a.JPEG", ".jpeg")]
    [InlineData(@"C:\a.webp", ".webp")]
    [InlineData(@"C:\a.png", ".png")]
    [InlineData(@"C:\a.bmp", ".png")]
    [InlineData(@"C:\a.tiff", ".png")]
    public void ResizeAndRotate_KeepFormatWhenPossible(string path, string expected)
    {
        Assert.Equal(expected, ConversionSpecs.For(ActionIds.Resize1920)!.OutputExtension(path, Picture));
        Assert.Equal(expected, ConversionSpecs.For(ActionIds.RotateRight)!.OutputExtension(path, Picture));
    }

    [Fact]
    public void Resize_UsesLanczosLongSide()
    {
        var args = ConversionSpecs.For(ActionIds.Resize1920)!.Arguments(@"C:\a.png", Picture).ToList();
        Assert.Equal("scale='if(gte(iw,ih),1920,-2)':'if(gte(iw,ih),-2,1920)':flags=lanczos", args[args.IndexOf("-vf") + 1]);
    }

    [Fact]
    public void Audio_MapsOnlyFirstAudioStream_KeepsSampleRate()
    {
        var args = ConversionSpecs.For(ActionIds.ToMp3)!.Arguments(@"C:\a.flac", Sound).ToList();
        Assert.Equal("0:a:0", args[args.IndexOf("-map") + 1]);
        Assert.Contains("-vn", args);
        Assert.Equal("libmp3lame", args[args.IndexOf("-c:a") + 1]);
        Assert.Equal("44100", args[args.IndexOf("-ar") + 1]);
    }

    [Theory]
    [InlineData(".opus", 44100, "48000")] // Opus — только 48 кГц
    [InlineData(".mp3", 96000, "48000")]  // в MP3 выше 48 кГц нельзя
    [InlineData(".wav", 0, "48000")]      // неизвестная частота
    [InlineData(".flac", 96000, "96000")]
    public void AudioEncoder_SampleRate(string ext, int sourceRate, string expected)
    {
        var args = ConversionSpecs.AudioEncoder(ext, new ProbeInfo(10, false, true, 0, 0, 2, sourceRate)).ToList();
        Assert.Equal(expected, args[args.IndexOf("-ar") + 1]);
    }

    [Fact]
    public void Normalize_KeepsSourceFormat_AndUsesLoudnorm()
    {
        var spec = ConversionSpecs.For(ActionIds.Normalize)!;
        Assert.Equal(".ogg", spec.OutputExtension(@"C:\a.OGG", Sound));
        Assert.Equal("_выровнено", spec.Suffix);
        var args = spec.Arguments(@"C:\a.ogg", Sound).ToList();
        Assert.StartsWith("loudnorm", args[args.IndexOf("-af") + 1]);
        Assert.Equal("libvorbis", args[args.IndexOf("-c:a") + 1]);
    }

    [Fact]
    public void Wav_EstimateGrowsWithDuration()
    {
        var spec = ConversionSpecs.For(ActionIds.ToWav)!;
        Assert.True(spec.EstimateBytes(1_000_000, new ProbeInfo(600, false, true, 0, 0)) > 100_000_000);
    }
}

public class GenericOutputNameTests
{
    [Fact]
    public void FormatChange_KeepsStem()
    {
        Assert.Equal(@"C:\p\фото отпуска.png",
            OutputNameGenerator.Generate(@"C:\p\фото отпуска.jpg", "", ".png", _ => false));
    }

    [Fact]
    public void NeverEqualsSource_EvenWithEmptySuffixAndSameExtension()
    {
        Assert.Equal(@"C:\p\a_2.png", OutputNameGenerator.Generate(@"C:\p\a.png", "", ".png", _ => false));
    }

    [Fact]
    public void SuffixAndNumbering()
    {
        var taken = new HashSet<string> { @"C:\p\a_повернуто.jpg" };
        Assert.Equal(@"C:\p\a_повернуто_2.jpg",
            OutputNameGenerator.Generate(@"C:\p\a.jpg", "_повернуто", ".jpg", taken.Contains));
    }
}
