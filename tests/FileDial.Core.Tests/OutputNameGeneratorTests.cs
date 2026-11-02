using FileDial.Core.Compression;

namespace FileDial.Core.Tests;

public class OutputNameGeneratorTests
{
    [Fact]
    public void AddsSuffixAndMp4Extension()
    {
        var result = OutputNameGenerator.Generate(@"C:\Video\clip.mov", _ => false);
        Assert.Equal(@"C:\Video\clip_25мб.mp4", result);
    }

    [Fact]
    public void KeepsCyrillicAndSpaces()
    {
        var result = OutputNameGenerator.Generate(@"D:\Мои видео\Отпуск в горах 2024.mp4", _ => false);
        Assert.Equal(@"D:\Мои видео\Отпуск в горах 2024_25мб.mp4", result);
    }

    [Fact]
    public void AddsNumberWhenTaken()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\v\a_25мб.mp4",
            @"C:\v\a_25мб_2.mp4",
        };
        var result = OutputNameGenerator.Generate(@"C:\v\a.mp4", taken.Contains);
        Assert.Equal(@"C:\v\a_25мб_3.mp4", result);
    }

    [Fact]
    public void OnlyLastExtensionIsReplaced()
    {
        var result = OutputNameGenerator.Generate(@"C:\v\my.video.final.webm", _ => false);
        Assert.Equal(@"C:\v\my.video.final_25мб.mp4", result);
    }

    [Fact]
    public void NeverReturnsSourcePath()
    {
        const string source = @"C:\v\a_25мб.mp4";
        var result = OutputNameGenerator.Generate(source, p => p == source);
        Assert.NotEqual(source, result);
        Assert.Equal(@"C:\v\a_25мб_25мб.mp4", result);
    }

    [Fact]
    public void PathTaken_SeesRealFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "видео 1.mp4");
            File.WriteAllText(Path.Combine(dir, "видео 1_25мб.mp4"), "x");
            Directory.CreateDirectory(Path.Combine(dir, "видео 1_25мб_2.mp4"));

            var result = OutputNameGenerator.Generate(source, OutputNameGenerator.PathTaken);
            Assert.Equal(Path.Combine(dir, "видео 1_25мб_3.mp4"), result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
