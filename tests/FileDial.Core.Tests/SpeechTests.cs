using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;
using FileDial.Core.Settings;

namespace FileDial.Core.Tests;

public class SpeechTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string FakeModel()
    {
        var dir = Path.Combine(_root, "Модели whisper");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ggml-base.bin");
        File.WriteAllText(path, "not a model");
        return path;
    }

    [Fact]
    public void WhyNot_ExplainsEachMissingPiece()
    {
        Assert.Equal(Speech.NoModel, Speech.WhyNot(JobSettings.Default));
        Assert.Equal("Файл модели whisper не найден",
            Speech.WhyNot(new JobSettings { WhisperModel = Path.Combine(_root, "нет.bin"), SpeechFilterAvailable = true }));
        Assert.Equal("Ваш ffmpeg собран без whisper", Speech.WhyNot(new JobSettings { WhisperModel = FakeModel() }));
        Assert.Null(Speech.WhyNot(new JobSettings { WhisperModel = FakeModel(), SpeechFilterAvailable = true }));
    }

    [Fact]
    public void Catalog_SubtitlesAndText_FollowSpeechSupport()
    {
        var off = JobSettings.Default;
        var on = new JobSettings { WhisperModel = FakeModel(), SpeechFilterAvailable = true };

        var subtitlesOff = ActionCatalog.ForFile(@"C:\v.mp4", 1, null, off).Single(a => a.Id == ActionIds.Subtitles);
        Assert.Equal(Speech.NoModel, subtitlesOff.DisabledReason);
        Assert.True(ActionCatalog.ForFile(@"C:\v.mp4", 1, null, on).Single(a => a.Id == ActionIds.Subtitles).IsEnabled);
        Assert.True(ActionCatalog.ForFile(@"C:\a.mp3", 1, null, on).Single(a => a.Id == ActionIds.SpeechToText).IsEnabled);

        var silent = new ProbeInfo(10, true, false, 640, 360);
        Assert.Equal("В видео нет звука",
            ActionCatalog.ForFile(@"C:\v.mp4", 1, silent, on).Single(a => a.Id == ActionIds.Subtitles).DisabledReason);
    }

    [Fact]
    public void HasWhisper_ParsesFilterList()
    {
        const string list = """
            Filters:
              T.. = Timeline support
             ... volume            A->A       Change input volume.
             .. whisper           A->A       Transcribe audio using whisper.cpp.
            """;
        Assert.True(Speech.HasWhisper(list));
        Assert.False(Speech.HasWhisper(" ... volume  A->A  Change input volume.\n ... whisperlike A->A x"));
    }

    [Theory]
    [InlineData(@"C:\models\ggml-base.bin", @"'C\:/models/ggml-base.bin'")]
    [InlineData(@"D:\Мои модели\ggml base.bin", @"'D\:/Мои модели/ggml base.bin'")]
    [InlineData(@"C:\it's\m.bin", @"'C\:/it'\''s/m.bin'")]
    public void QuotePath_EscapesForFilterGraph(string path, string expected)
    {
        Assert.Equal(expected, Speech.QuotePath(path));
    }

    [Fact]
    public void Spec_WritesThroughFilterDestination()
    {
        var settings = new JobSettings { WhisperModel = @"C:\m\ggml-base.bin", WhisperLanguage = "ru" };
        var spec = Speech.For(ActionIds.Subtitles, settings)!;
        var args = spec.FilterArguments!(@"C:\v.mp4", new ProbeInfo(10, true, true, 1, 1), @"D:\out\.filedial-1.tmp").ToList();

        Assert.Equal(".srt", spec.OutputExtension(@"C:\v.mp4", new ProbeInfo(10, true, true, 1, 1)));
        var filter = args[args.IndexOf("-af") + 1];
        Assert.Contains(@"model='C\:/m/ggml-base.bin'", filter);
        Assert.Contains("language=ru", filter);
        Assert.Contains(@"destination='D\:/out/.filedial-1.tmp'", filter);
        Assert.Contains("format=srt", filter);
        Assert.Equal(["-f", "null", "-"], args.TakeLast(3));

        Assert.Equal(".txt", Speech.For(ActionIds.SpeechToText, settings)!.OutputExtension(@"C:\a.mp3", new ProbeInfo(1, false, true, 0, 0)));
        Assert.Contains("format=text", Speech.For(ActionIds.SpeechToText, settings)!
            .FilterArguments!(@"C:\a.mp3", new ProbeInfo(1, false, true, 0, 0), @"C:\t.tmp")[4]);
    }

    [Fact]
    public void Srt_IsRenumberedFromOne_TextUntouched()
    {
        const string whisper = "0\n00:00:00,000 --> 00:00:01,000\nПривет.\n\n1\n00:00:01,000 --> 00:00:06,580\n12345\n\n";
        var fixedSrt = Speech.RenumberSrt(whisper);
        Assert.Equal("1\r\n00:00:00,000 --> 00:00:01,000\r\nПривет.\r\n\r\n2\r\n00:00:01,000 --> 00:00:06,580\r\n12345\r\n\r\n", fixedSrt);
    }

    [Fact]
    public void UnknownLanguage_FallsBackToAuto()
    {
        var spec = Speech.For(ActionIds.SpeechToText, new JobSettings { WhisperModel = @"C:\m.bin", WhisperLanguage = "xx" })!;
        Assert.Contains("language=auto", spec.FilterArguments!(@"C:\a.mp3", new ProbeInfo(1, false, true, 0, 0), @"C:\t")[4]);
    }

    [Fact]
    public void Settings_NormalizeLanguage_AndKeepModel()
    {
        var s = new AppSettings { WhisperLanguage = "de", WhisperModel = @"  D:\m.bin " }.Normalized();
        Assert.Equal("auto", s.WhisperLanguage);
        Assert.Equal(@"D:\m.bin", s.WhisperModel);
        Assert.Equal(@"D:\m.bin", s.Jobs.WhisperModel);
    }

    [FfmpegFact]
    public async Task RealFfmpeg_HasWhisperDetection_AndReceivesCyrillicModelPath()
    {
        var tools = FfmpegLocator.LocateDefault()!;
        if (!await Speech.FilterAvailableAsync(tools, CancellationToken.None))
            return; // ffmpeg без whisper — проверять нечего

        // Поддельная модель в папке с кириллицей и пробелами: ffmpeg должен дойти до загрузки именно этого файла
        // (то есть путь пережил экранирование графа фильтров) и аккуратно упасть.
        var model = FakeModel();
        var wav = Path.Combine(_root, "речь.wav");
        var gen = await ProcessRunner.RunAsync(tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "sine=sample_rate=16000", "-t", "1", wav],
            null, false, CancellationToken.None);
        Assert.Equal(0, gen.ExitCode);

        var job = FileJobs.Create(ActionIds.SpeechToText, tools, new JobSettings { WhisperModel = model })!;
        var ex = await Assert.ThrowsAsync<CompressionException>(() => job.RunAsync(wav, null, CancellationToken.None));
        Assert.Contains("ggml-base.bin", ex.Details ?? "");
        Assert.Empty(Directory.GetFiles(_root, ".filedial-*"));
    }

    /// <summary>Настоящее распознавание — если модель лежит по пути из FILEDIAL_WHISPER_MODEL.</summary>
    [FfmpegFact]
    public async Task RealModel_ProducesSrt_WhenAvailable()
    {
        var model = Environment.GetEnvironmentVariable("FILEDIAL_WHISPER_MODEL");
        if (string.IsNullOrEmpty(model) || !File.Exists(model))
            return;
        var tools = FfmpegLocator.LocateDefault()!;
        Directory.CreateDirectory(_root);
        var wav = Path.Combine(_root, "тишина.wav");
        await ProcessRunner.RunAsync(tools.Ffmpeg,
            ["-hide_banner", "-nostdin", "-y", "-loglevel", "error", "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono", "-t", "3", wav],
            null, false, CancellationToken.None);

        var result = await FileJobs.Create(ActionIds.Subtitles, tools, new JobSettings { WhisperModel = model })!
            .RunAsync(wav, null, CancellationToken.None);
        // В тишине речи нет: либо пустой результат («речь не найдена»), либо пустые субтитры — но без ошибки.
        Assert.True(result.Status is JobStatus.NothingToDo or JobStatus.Done);
    }
}
