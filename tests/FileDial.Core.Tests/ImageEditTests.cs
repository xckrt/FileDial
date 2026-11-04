using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;
using FileDial.Core.Menu;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

/// <summary>Группа «Повернуть ›», «Кадрировать…» и HEIC/AVIF в круге картинок.</summary>
public class ImageEditMenuTests
{
    private static ProbeInfo Picture(int w, int h) => new(0, true, false, w, h);

    [Fact]
    public void RotateGroup_HoldsTurnsAndFlips_KeepingOldIds()
    {
        var group = ActionCatalog.ForFile(@"C:\p\a.png", 1, Picture(10, 10)).Single(a => a.Id == ActionIds.ImageRotate);

        Assert.Equal("Повернуть", group.Label);
        Assert.Equal([ActionIds.RotateRight, ActionIds.RotateLeft, ActionIds.Rotate180, ActionIds.Mirror, ActionIds.MirrorVertical],
            group.Children!.Select(c => c.Id));
        Assert.Equal(["Вправо", "Влево", "На 180°", "Отразить ↔", "Отразить ↕"], group.Children!.Select(c => c.Label));
        // Полные названия — в заголовке окна и в истории; у прежних действий они те же, что раньше.
        Assert.Equal(["Повернуть вправо", "Повернуть влево", "Повернуть на 180°", "Отразить по горизонтали",
            "Отразить по вертикали"], group.Children!.Select(c => c.Title));
        Assert.All(group.Children!, c => Assert.True(c.IsEnabled && FileJobs.IsImplemented(c.Id), c.Id));
    }

    [Fact]
    public void Crop_AsksForFrame_AndDiffersFromVideoTrim()
    {
        var crop = ActionCatalog.ForFile(@"C:\p\a.jpg", 1, Picture(10, 10)).Single(a => a.Id == ActionIds.CropImage);
        Assert.Equal("Кадрировать…", crop.Label);
        Assert.Equal("Кадрировать", crop.Title);
        Assert.True(FileJobs.NeedsCropInput(crop.Id));
        Assert.True(FileJobs.IsImplemented(crop.Id));
        Assert.NotEqual(Lang.T.Trim, crop.Label.TrimEnd('…'));
    }

    [Theory]
    [InlineData(@"C:\p\IMG_0001.HEIC")]
    [InlineData(@"C:\p\a.heif")]
    [InlineData(@"C:\p\a.hif")]
    [InlineData(@"C:\p\a.avif")]
    public void Heif_GetsFullImageDial_FormatIsOutputOnly(string path)
    {
        var actions = ActionCatalog.ForFile(path, 3_000_000, Picture(4032, 3024));

        Assert.Equal(ActionCatalog.ForFile(@"C:\p\a.png", 3_000_000, Picture(4032, 3024)).Select(a => a.Id), actions.Select(a => a.Id));
        Assert.All(ActionCatalog.Flatten(actions).Where(a => a.Id != ActionIds.RemoveBackground),
            a => Assert.True(a.IsEnabled, a.Id));
        // В HEIC/AVIF не пишем: в «Формате» их нет, а свой формат не гасится — его там и не было.
        Assert.Equal(["JPG", "PNG", "WebP", "BMP", "TIFF", "GIF", "ICO"], actions[0].Children!.Select(c => c.Label));
        Assert.All(ActionCatalog.Flatten(actions), a => Assert.True(FileJobs.IsImplemented(a.Id), a.Id));
    }

    [Fact]
    public void Heif_FfprobeSeesNoPicture_EverythingDisabledWithHonestReason()
    {
        var actions = ActionCatalog.ForFile(@"C:\p\a.heic", 3_000_000, new ProbeInfo(0, false, false, 0, 0));

        Assert.All(ActionCatalog.Flatten(actions), a =>
        {
            Assert.False(a.IsEnabled, a.Id);
            Assert.Equal(Lang.T.ImgHeifUnreadable, a.DisabledReason);
            Assert.Null(a.FixActionId);
        });
        Assert.All(actions.Where(a => a.IsGroup), g => Assert.False(g.IsEnabled));
        // Без сведений ffprobe (нет ffmpeg, таймаут) — не запрещаем: окно действия объяснит.
        Assert.True(ActionCatalog.ForFile(@"C:\p\a.heic", 3_000_000).Single(a => a.Id == ActionIds.CropImage).IsEnabled);
    }

    [Fact]
    public void Batch_OfHeic_CropAndRotateAvailable()
    {
        var actions = ActionCatalog.ForBatch(FileKind.Image, [@"C:\p\a.heic", @"C:\p\b.HEIC"], [1, 2]);

        Assert.True(actions.Single(a => a.Id == ActionIds.CropImage).IsEnabled);
        Assert.All(actions.Single(a => a.Id == ActionIds.ImageRotate).Children!, c => Assert.True(c.IsEnabled));
        Assert.All(actions[0].Children!, c => Assert.True(c.IsEnabled));
    }

    [Theory]
    [InlineData(ActionIds.Rotate180, "hflip,vflip", "_повернуто_180")]
    [InlineData(ActionIds.MirrorVertical, "vflip", "_отражено_по_вертикали")]
    [InlineData(ActionIds.Mirror, "hflip", "_отражено")]
    public void TurnsAndFlips_AreFilters_KeepingFormat(string id, string filter, string suffix)
    {
        var spec = ConversionSpecs.For(id)!;
        var args = spec.Arguments(@"C:\a.webp", Picture(10, 10)).ToList();
        Assert.Equal(filter, args[args.IndexOf("-vf") + 1]);
        Assert.Equal(suffix, spec.Suffix);
        Assert.Equal(".webp", spec.OutputExtension(@"C:\a.webp", Picture(10, 10)));
    }

    [Theory]
    [InlineData(@"C:\a.heic", ".jpg")]
    [InlineData(@"C:\a.HEIF", ".jpg")]
    [InlineData(@"C:\a.avif", ".jpg")]
    [InlineData(@"C:\a.bmp", ".png")]
    public void SameImageFormat_HeifBecomesJpg(string path, string expected) =>
        Assert.Equal(expected, ConversionSpecs.SameImageFormat(path));

    [Fact]
    public void TileGrid_IsTakenThroughFilterComplex_EvenWithoutFilter()
    {
        var grid = new ProbeInfo(0, true, false, 4032, 3024) { Picture = "0:g:0" };
        Assert.Equal(["-filter_complex", "[0:g:0]null[out]", "-map", "[out]"], grid.PictureArguments(null));
        Assert.Equal(["-filter_complex", "[0:g:0]vflip[out]", "-map", "[out]"], grid.PictureArguments("vflip"));

        var args = ConversionSpecs.For(ActionIds.ToPng)!.Arguments(@"C:\a.heic", grid).ToList();
        Assert.DoesNotContain("-vf", args);
        Assert.Equal("[out]", args[args.IndexOf("-map") + 1]);

        // В JPG — белый фон под сеткой, а не под первой плиткой.
        var jpg = ConversionSpecs.For(ActionIds.ToJpg)!.Arguments(@"C:\a.heic", grid).ToList();
        Assert.Contains("[0:g:0]format=rgba", jpg[jpg.IndexOf("-filter_complex") + 1]);
        // GIF: палитра по одному кадру — склеенная сетка не сообщает о конце потока.
        var gif = ConversionSpecs.For(ActionIds.ImageTo(".gif"))!.Arguments(@"C:\a.heic", grid).ToList();
        Assert.StartsWith("[0:g:0]split", gif[gif.IndexOf("-filter_complex") + 1]);
        Assert.Contains("stats_mode=single", gif[gif.IndexOf("-filter_complex") + 1]);
    }
}

public class ProbeInfoImageTests
{
    [Fact]
    public void Arguments_AskGroupsOnlyForHeif_AndFirstFrameForOtherImages()
    {
        var heic = string.Join(" ", ProbeInfo.Arguments(@"C:\a.heic"));
        Assert.Contains("stream_group", heic);
        Assert.DoesNotContain("read_intervals", heic);

        var jpg = ProbeInfo.Arguments(@"C:\a.jpg").ToList();
        Assert.Equal("%+#1", jpg[jpg.IndexOf("-read_intervals") + 1]);
        Assert.Contains("frame_side_data=rotation", jpg[jpg.IndexOf("-show_entries") + 1]);
        Assert.DoesNotContain("stream_group", string.Join(" ", jpg));

        var mp4 = string.Join(" ", ProbeInfo.Arguments(@"C:\a.mp4"));
        Assert.DoesNotContain("stream_group", mp4);
        Assert.DoesNotContain("read_intervals", mp4);
    }

    [Fact]
    public void Grid_SizeAndRotationFromGroup()
    {
        // Так ffprobe 9 описывает снимок iPhone: плитки 512×512 — дорожки, сетка — группа с поворотом.
        const string json = """
            {
              "stream_groups": [
                { "type": "Tile Grid", "disposition": { "default": 1 },
                  "components": [ { "width": 4032, "height": 3024, "side_data_list": [ { "rotation": -90 } ] } ] }
              ],
              "streams": [
                { "codec_name": "hevc", "codec_type": "video", "width": 512, "height": 512, "disposition": { "default": 0 } },
                { "codec_name": "hevc", "codec_type": "video", "width": 512, "height": 512, "disposition": { "default": 0 } }
              ],
              "format": {}
            }
            """;
        var info = ProbeInfo.Parse(json);

        Assert.True(info.HasVideo);
        Assert.True(info.IsTileGrid);
        Assert.Equal("0:g:0", info.Picture);
        Assert.Equal((3024, 4032), (info.Width, info.Height)); // портрет: повёрнуто на 90°
        Assert.Equal("hevc", info.VideoCodec);
    }

    [Fact]
    public void Heic_ThumbnailFirst_PrimaryIsTheDefaultStream()
    {
        const string json = """
            { "streams": [
                { "codec_type": "video", "width": 64, "height": 64, "disposition": { "default": 0 } },
                { "codec_type": "video", "width": 700, "height": 500, "disposition": { "default": 1 },
                  "side_data_list": [ { "rotation": 90 } ] } ] }
            """;
        var info = ProbeInfo.Parse(json);
        Assert.Equal("0:v:1", info.Picture);
        Assert.False(info.IsTileGrid);
        Assert.Equal((500, 700), (info.Width, info.Height));
        Assert.Equal(["-map", "0:v:1", "-vf", "hflip"], info.PictureArguments("hflip"));
    }

    [Theory]
    [InlineData(-90, 20, 80)]
    [InlineData(90, 20, 80)]
    [InlineData(180, 80, 20)]
    [InlineData(-270, 20, 80)]
    public void Jpeg_ExifRotationInFirstFrame_SwapsSides(int rotation, int w, int h)
    {
        var json = $$"""
            { "frames": [ { "side_data_list": [ { "side_data_type": "3x3 displaymatrix", "rotation": {{rotation}} } ] } ],
              "streams": [ { "codec_type": "video", "codec_name": "mjpeg", "width": 80, "height": 20 } ] }
            """;
        var info = ProbeInfo.Parse(json);
        Assert.Equal((w, h), (info.Width, info.Height));
        Assert.Equal("0:v:0", info.Picture);
    }
}

public class CropGeometryTests
{
    private static CropRequest Aspect(int w, int h, CropAnchor anchor = CropAnchor.Center) => new(new CropAspect(w, h), anchor);

    [Theory]
    [InlineData(4032, 3024, 1, 1, 504, 0, 3024, 3024)] // альбом → квадрат по центру
    [InlineData(3024, 4032, 1, 1, 0, 504, 3024, 3024)] // портрет → квадрат по центру
    [InlineData(4000, 3000, 16, 9, 0, 375, 4000, 2250)]
    [InlineData(1000, 1000, 4, 5, 100, 0, 800, 1000)]
    [InlineData(3000, 4000, 9, 16, 375, 0, 2250, 4000)]
    [InlineData(1001, 1000, 16, 9, 0, 218, 1001, 563)] // 563,06 → 563
    [InlineData(1000, 1001, 3, 2, 0, 167, 1000, 667)] // 666,67 → 667
    [InlineData(6, 3, 1, 1, 1, 0, 3, 3)] // нечётный остаток: на полпикселя левее середины
    public void LargestCentered(int w, int h, int aw, int ah, int ex, int ey, int ew, int eh) =>
        Assert.Equal(new CropRect(ex, ey, ew, eh), Aspect(aw, ah).Rectangle(w, h));

    [Theory]
    [InlineData(CropAnchor.Top, 0, 0)]
    [InlineData(CropAnchor.Bottom, 0, 1000)]
    [InlineData(CropAnchor.Left, 0, 500)]
    [InlineData(CropAnchor.Right, 0, 500)]
    [InlineData(CropAnchor.Center, 0, 500)]
    public void Anchors_Portrait(CropAnchor anchor, int ex, int ey) =>
        Assert.Equal(new CropRect(ex, ey, 2000, 2000), Aspect(1, 1, anchor).Rectangle(2000, 3000));

    [Theory]
    [InlineData(CropAnchor.Left, 0, 0)]
    [InlineData(CropAnchor.Right, 1000, 0)]
    [InlineData(CropAnchor.Top, 500, 0)]
    [InlineData(CropAnchor.Bottom, 500, 0)]
    public void Anchors_Landscape(CropAnchor anchor, int ex, int ey) =>
        Assert.Equal(new CropRect(ex, ey, 2000, 2000), Aspect(1, 1, anchor).Rectangle(3000, 2000));

    [Fact]
    public void Free_WithoutRegion_IsWholeImage_NothingToCrop()
    {
        var free = new CropRequest(CropAspect.Free);
        Assert.Equal(new CropRect(0, 0, 640, 480), free.Rectangle(640, 480));
        Assert.False(free.Crops(640, 480));
        Assert.False(Aspect(4, 3).Crops(640, 480)); // те же пропорции — отрезать нечего
        Assert.True(Aspect(4, 3).Crops(641, 480));
    }

    [Fact]
    public void Region_Free_RoundsAndClamps()
    {
        var r = new CropRequest(CropAspect.Free, Region: new CropRegion(0.25, 0.5, 0.5, 0.5)).Rectangle(200, 101);
        Assert.Equal(new CropRect(50, 51, 100, 50), r);

        // За краем и отрицательные доли — внутрь картинки, не меньше пикселя.
        var outside = new CropRequest(CropAspect.Free, Region: new CropRegion(-0.5, 0.9, 2, 0.5)).Rectangle(100, 100);
        Assert.Equal(new CropRect(0, 90, 100, 10), outside);
        var tiny = new CropRequest(CropAspect.Free, Region: new CropRegion(1, 1, 0, 0)).Rectangle(100, 100);
        Assert.Equal(new CropRect(99, 99, 1, 1), tiny);
    }

    [Fact]
    public void Region_WithAspect_ExactRatio_SameCenter()
    {
        // Рамка из окна чуть не квадратная (дробные пиксели) — результат ровно квадрат с тем же центром.
        var r = new CropRequest(new CropAspect(1, 1), Region: new CropRegion(0.1, 0.1, 0.5003, 0.4999)).Rectangle(1000, 1000);
        Assert.Equal(r.Width, r.Height);
        Assert.Equal(500, r.Width);
        Assert.Equal(new CropRect(100, 100, 500, 500), r); // центр (350, 350), как у рамки

        var portrait = new CropRequest(new CropAspect(4, 5), Region: new CropRegion(0, 0, 0.4, 1)).Rectangle(1000, 500);
        Assert.Equal(new CropRect(0, 0, 400, 500), portrait);
    }

    [Fact]
    public void Suffix_AspectOrPixelSize()
    {
        Assert.Equal("_кадр_1x1", Aspect(1, 1).SuffixFor(4032, 3024));
        Assert.Equal("_кадр_9x16", Aspect(9, 16).SuffixFor(4032, 3024));
        Assert.Equal("_кадр_100x50",
            new CropRequest(CropAspect.Free, Region: new CropRegion(0, 0, 0.5, 0.5)).SuffixFor(200, 100));
    }

    [Fact]
    public void AspectOf_Reduces()
    {
        Assert.Equal(new CropAspect(4, 3), CropAspect.Of(4032, 3024));
        Assert.Equal(new CropAspect(3, 4), CropAspect.Of(4032, 3024).Swapped);
        Assert.True(CropAspect.Of(0, 10).IsFree);
        Assert.Equal("16:9", new CropAspect(16, 9).ToString());
    }

    [Theory]
    [InlineData("free", "free")]
    [InlineData("original", "original")]
    [InlineData("4:5", "4:5")]
    [InlineData("16:9@top", "16:9@top")]
    [InlineData("1:1@center", "1:1")]
    [InlineData("1:1@RIGHT", "1:1@right")]
    [InlineData(null, "free")]
    [InlineData("", "free")]
    [InlineData("0:5", "free")]
    [InlineData("1:1@2", "free")]
    [InlineData("1:1@middle", "free")]
    [InlineData("квадрат", "free")]
    public void Choice_ParseFormat(string? text, string expected) =>
        Assert.Equal(expected, CropChoice.Parse(text).Format());

    [Fact]
    public void Settings_RememberCropChoice_NormalizeGarbage()
    {
        Assert.Equal("4:5@bottom", new AppSettings { Crop = "4:5@bottom" }.Normalized().Crop);
        Assert.Equal("free", new AppSettings { Crop = "мусор" }.Normalized().Crop);
        Assert.Equal("free", new AppSettings().Crop);
    }
}

[Collection(LanguageCollection.Name)]
public class ImageEditEnglishTests
{
    [Fact]
    public void English_LabelsAndSuffix()
    {
        using var _ = new EnglishScope();
        var actions = ActionCatalog.ForFile(@"C:\p\a.heic", 1, new ProbeInfo(0, true, false, 10, 10));
        Assert.Equal(["Format", "Resize…", "Rotate", "Crop…"], actions.Take(4).Select(a => a.Label));
        Assert.Equal(["Right", "Left", "180°", "Flip ↔", "Flip ↕"], actions[2].Children!.Select(c => c.Label));
        Assert.Equal("_crop_4x5", new CropRequest(new CropAspect(4, 5)).SuffixFor(100, 100));
    }
}
