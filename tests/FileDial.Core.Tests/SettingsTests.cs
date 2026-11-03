using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+K", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 'K')]
    [InlineData("ctrl + shift + 5", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, '5')]
    [InlineData("Win+F12", HotkeyModifiers.Win, 0x7B)]
    [InlineData("Alt+F1", HotkeyModifiers.Alt, 0x70)]
    public void Parses(string text, HotkeyModifiers modifiers, int vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(new HotkeyGesture(modifiers, vk), g);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+K+L")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Tab")]
    public void RejectsInvalid(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void Formats_InFixedModifierOrder()
    {
        var g = new HotkeyGesture(HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, 'Q');
        Assert.Equal("Ctrl+Alt+Shift+Win+Q", g.Format());
        Assert.True(HotkeyGesture.TryParse(g.Format(), out var back));
        Assert.Equal(g, back);
    }

    [Fact]
    public void Problem_RequiresRealModifier_AndSupportedKey()
    {
        Assert.Null(HotkeyGesture.Default.Problem());
        Assert.Equal("Добавьте Ctrl, Alt или Win.", new HotkeyGesture(HotkeyModifiers.Shift, 'K').Problem());
        Assert.Equal("Добавьте Ctrl, Alt или Win.", new HotkeyGesture(HotkeyModifiers.None, 'K').Problem());
        Assert.Equal("Используйте букву, цифру или F1–F24.", new HotkeyGesture(HotkeyModifiers.Ctrl, 0x20).Problem());
    }
}

public class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "Настройки");
    private string File_ => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var s = SettingsStore.Load(File_);
        Assert.Equal("Ctrl+Alt+K", s.Hotkey);
        Assert.True(s.Animations);
        Assert.Equal(25_000_000, s.VideoTargetBytes);
        Assert.Equal(1_000_000, s.ImageTargetBytes);
        Assert.Null(s.OutputFolder);
    }

    [Fact]
    public void RoundTrip_KeepsEverything_IncludingCyrillicPaths()
    {
        var original = new AppSettings
        {
            Hotkey = "Ctrl+Shift+D",
            Animations = false,
            OpenFolderWhenDone = true,
            FfmpegFolder = @"D:\Программы\ffmpeg\bin",
            VideoTargetBytes = 10_000_000,
            ImageTargetBytes = 500_000,
            OutputFolder = @"D:\Готовые файлы",
        };
        SettingsStore.Save(File_, original);

        Assert.Equal(original, SettingsStore.Load(File_));
        Assert.Contains("Готовые файлы", File.ReadAllText(File_)); // без \uXXXX — файл читаем руками
        Assert.False(File.Exists(File_ + ".tmp"));
    }

    [Theory]
    [InlineData("{ это не json")]
    [InlineData("")]
    [InlineData("null")]
    public void CorruptedFile_GivesDefaults(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, content);
        Assert.Equal(new AppSettings(), SettingsStore.Load(File_));
    }

    [Fact]
    public void OddValues_AreNormalized()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """
            { "Hotkey": "Shift+K", "VideoTargetBytes": 9000000, "ImageTargetBytes": -5, "OutputFolder": "  " }
            """);
        var s = SettingsStore.Load(File_);

        Assert.Equal("Ctrl+Alt+K", s.Hotkey); // без Ctrl/Alt/Win — возвращаем рабочее сочетание
        Assert.Equal(8_000_000, s.VideoTargetBytes); // ближайшее допустимое
        Assert.Equal(500_000, s.ImageTargetBytes);
        Assert.Null(s.OutputFolder);
    }

    [Theory]
    [InlineData("brass", "brass")]
    [InlineData("auto", "auto")]
    [InlineData("неон", "graphite")] // неизвестная тема — к теме по умолчанию
    [InlineData("", "graphite")]
    public void Theme_IsNormalized(string stored, string expected)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, $$"""{ "Theme": "{{stored}}" }""");
        Assert.Equal(expected, SettingsStore.Load(File_).Theme);
    }

    [Fact]
    public void UnknownFields_AreIgnored()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(File_, """{ "Animations": false, "ЧтоТоНовое": 42 }""");
        Assert.False(SettingsStore.Load(File_).Animations);
    }
}

public class JobSettingsTests
{
    [Theory]
    [InlineData(25_000_000, "_25мб", "25\u00A0МБ")]
    [InlineData(8_000_000, "_8мб", "8\u00A0МБ")]
    [InlineData(500_000, "_500кб", "500\u00A0КБ")]
    [InlineData(100_000_000, "_100мб", "100\u00A0МБ")]
    public void SuffixAndLabel(long bytes, string suffix, string label)
    {
        Assert.Equal(suffix, JobSettings.TargetSuffix(bytes));
        Assert.Equal(label, JobSettings.TargetLabel(bytes));
    }

    [Fact]
    public void OutputDirectory_DefaultsToSourceFolder()
    {
        Assert.Equal(@"C:\v", JobSettings.Default.OutputDirectoryFor(@"C:\v\a.mp4"));
        Assert.Equal(@"D:\out", new JobSettings { OutputFolder = @"D:\out" }.OutputDirectoryFor(@"C:\v\a.mp4"));
    }

    [Fact]
    public void Catalog_UsesTargetsFromSettings()
    {
        var ten = new JobSettings { VideoTargetBytes = 10_000_000, ImageTargetBytes = 2_000_000 };

        var compress = ActionCatalog.ForFile(@"C:\v.mp4", 15_000_000, null, ten).Single(a => a.Id == ActionIds.Compress25);
        Assert.Equal("Сжать до 10\u00A0МБ", compress.Label);
        Assert.True(compress.IsEnabled); // 15 МБ > 10 МБ — сжимать есть что, хотя меньше 25 МБ

        var small = ActionCatalog.ForFile(@"C:\v.mp4", 9_000_000, null, ten).Single(a => a.Id == ActionIds.Compress25);
        Assert.Equal("Файл уже меньше 10 МБ", small.DisabledReason);

        var image = ActionCatalog.ForFile(@"C:\p.png", 1_500_000, null, ten).Single(a => a.Id == ActionIds.ImageCompress1Mb);
        Assert.Equal("Сжать до 2\u00A0МБ", image.Label);
        Assert.Equal("Файл уже меньше 2 МБ", image.DisabledReason);
    }

    [Fact]
    public void LongVideoMessage_UsesTarget()
    {
        // 100 МБ × 8 × 0,97 / (50 + 128 кбит/с) ≈ 4360 с ≈ 72 мин.
        var info = new ProbeInfo(90 * 60, true, true, 1920, 1080);
        Assert.Equal("Видео длиннее 72 мин в 100 МБ не уместить", CompressionCheck.WhyNot(900_000_000, info, 100_000_000));
    }

    [Fact]
    public void OutputName_InCustomFolder()
    {
        var name = OutputNameGenerator.Generate(@"C:\src\клип.mp4", "_10мб", ".mp4", _ => false, @"D:\Готово");
        Assert.Equal(@"D:\Готово\клип_10мб.mp4", name);
    }
}

public class FfmpegLocatorPreferredTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void PreferredFolderWins_WhenItHasBothFiles()
    {
        var dir = Path.Combine(_root, "мой ffmpeg");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ffmpeg.exe"), "");
        File.WriteAllText(Path.Combine(dir, "ffprobe.exe"), "");

        Assert.Equal(Path.Combine(dir, "ffmpeg.exe"), FfmpegLocator.LocateDefault(dir)!.Ffmpeg);
    }

    [Fact]
    public void EmptyPreferredFolder_FallsBack()
    {
        var dir = Path.Combine(_root, "пусто");
        Directory.CreateDirectory(dir);
        Assert.Equal(FfmpegLocator.LocateDefault()?.Ffmpeg, FfmpegLocator.LocateDefault(dir)?.Ffmpeg);
    }
}

/// <summary>Цели и папка результатов из настроек — на настоящем ffmpeg.</summary>
public class SettingsIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static FfmpegTools Tools => FfmpegLocator.LocateDefault()!;

    [FfmpegFact]
    public async Task Results_GoToCustomFolder_SourceFolderStaysClean()
    {
        var source = Path.Combine(_root, "Исходники");
        var output = Path.Combine(_root, "Готовые файлы");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(output);
        var png = Path.Combine(source, "схема.png");
        var gen = await ProcessRunner.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=64x48", "-frames:v", "1", png],
            null, false, CancellationToken.None);
        Assert.Equal(0, gen.ExitCode);

        var settings = new JobSettings { OutputFolder = output };
        var result = await FileJobs.Create(ActionIds.ToJpg, Tools, settings)!.RunAsync(png, null, CancellationToken.None);

        Assert.Equal(Path.Combine(output, "схема.jpg"), result.OutputPath);
        Assert.Equal([png], Directory.GetFiles(source)); // ни результата, ни временных файлов рядом с исходником
        Assert.Single(Directory.GetFiles(output));
    }

    [FfmpegFact]
    public async Task VideoTarget_FromSettings_DrivesSizeAndName()
    {
        Directory.CreateDirectory(_root);
        var mp4 = Path.Combine(_root, "ролик.mp4");
        var gen = await ProcessRunner.RunAsync(Tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30",
                "-t", "6", "-c:v", "libx264", "-preset", "ultrafast", "-b:v", "6M", mp4],
            null, false, CancellationToken.None);
        Assert.Equal(0, gen.ExitCode);

        var settings = new JobSettings { VideoTargetBytes = 2_000_000 };
        var result = await FileJobs.Create(ActionIds.Compress25, Tools, settings)!.RunAsync(mp4, null, CancellationToken.None);

        Assert.Equal(Path.Combine(_root, "ролик_2мб.mp4"), result.OutputPath);
        Assert.True(new FileInfo(result.OutputPath!).Length <= 2_000_000);
    }
}
