using FileDial.Core.Compression;
using FileDial.Core.Menu;
using FileDial.Core.Settings;

namespace FileDial.Core.Jobs;

/// <summary>
/// Распознавание речи через фильтр whisper, встроенный в ffmpeg 8+ (whisper.cpp). Работает офлайн,
/// но нужен файл модели ggml-*.bin — его пользователь скачивает сам и указывает в настройках.
/// </summary>
public static class Speech
{
    public static readonly IReadOnlyList<(string Code, string Label)> Languages =
        [("auto", "Авто"), ("ru", "Русский"), ("en", "English")];

    /// <summary>Причина, почему распознавание недоступно, или null.</summary>
    public static string? WhyNot(JobSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.WhisperModel))
            return "Укажите модель whisper в настройках";
        if (!File.Exists(settings.WhisperModel))
            return "Файл модели whisper не найден";
        if (!settings.SpeechFilterAvailable)
            return "Ваш ffmpeg собран без whisper";
        return null;
    }

    /// <summary>Есть ли в ffmpeg фильтр whisper (по списку «ffmpeg -filters»).</summary>
    public static async Task<bool> FilterAvailableAsync(FfmpegTools tools, CancellationToken ct)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(tools.Ffmpeg, ["-hide_banner", "-filters"], null,
                captureOutput: true, ct).ConfigureAwait(false);
            return result.ExitCode == 0 && HasWhisper(result.StandardOutput);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>Строка списка фильтров вида « .. whisper   A->A   Transcribe audio…».</summary>
    public static bool HasWhisper(string filtersOutput) =>
        filtersOutput.Split('\n').Any(line =>
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && parts[1] == "whisper";
        });

    /// <summary>
    /// Значение-путь для опции фильтра. В графе фильтров «:» и «\» служебные, поэтому путь пишем с прямыми
    /// слешами, двоеточие экранируем и всё берём в одинарные кавычки: 'C\:/Модели/ggml-base.bin'.
    /// </summary>
    public static string QuotePath(string path)
    {
        var value = path.Replace('\\', '/').Replace(":", "\\:").Replace("'", "'\\''");
        return $"'{value}'";
    }

    public static ConversionSpec? For(string actionId, JobSettings settings)
    {
        var format = actionId switch
        {
            ActionIds.Subtitles => "srt",
            ActionIds.SpeechToText => "text",
            _ => null,
        };
        if (format is null || settings.WhisperModel is not { } model)
            return null;

        var language = Languages.Any(l => l.Code == settings.WhisperLanguage) ? settings.WhisperLanguage : "auto";
        return new ConversionSpec
        {
            Suffix = "",
            Needs = MediaNeeds.Sound,
            OutputExtension = (_, _) => format == "srt" ? ".srt" : ".txt",
            // Результат пишет сам фильтр (destination), ffmpeg ничего не выводит в файл.
            FilterArguments = (_, _, output) =>
            [
                "-map", "0:a:0", "-vn",
                // Очередь побольше — больше контекста для распознавания, точнее текст.
                "-af", $"whisper=model={QuotePath(model)}:language={language}:queue=15:" +
                       $"destination={QuotePath(output)}:format={format}",
                "-f", "null", "-",
            ],
            Arguments = (_, _) => [],
            EstimateBytes = (_, _) => 10_000_000,
            Timed = true,
            EmptyOutputMessage = "Речь не найдена — файл не создан.",
            PostProcess = format == "srt" ? RenumberSrtFile : null,
        };
    }

    /// <summary>
    /// whisper нумерует субтитры с 0, а по стандарту SRT — с 1; строгие плееры пропускают «нулевой».
    /// Перенумеровываем блоки по порядку, остальное не трогаем.
    /// </summary>
    public static string RenumberSrt(string srt)
    {
        var lines = srt.Replace("\r\n", "\n").Split('\n');
        var number = 0;
        for (var i = 0; i < lines.Length - 1; i++)
        {
            // Номер блока — строка из цифр, за которой идёт строка таймкодов.
            if (lines[i].Trim().All(char.IsAsciiDigit) && lines[i].Trim().Length > 0 && lines[i + 1].Contains("-->"))
                lines[i] = (++number).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return string.Join("\r\n", lines);
    }

    private static void RenumberSrtFile(string path) =>
        File.WriteAllText(path, RenumberSrt(File.ReadAllText(path)), new System.Text.UTF8Encoding(false));
}
