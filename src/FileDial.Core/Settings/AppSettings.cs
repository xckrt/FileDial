using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileDial.Core.Settings;

/// <summary>Все настройки программы. Хранятся в JSON; неизвестные или испорченные значения заменяются значениями по умолчанию.</summary>
public sealed record AppSettings
{
    /// <summary>Допустимые цели сжатия — те же, что предлагает окно настроек.</summary>
    public static readonly IReadOnlyList<long> VideoTargets = [8_000_000, 10_000_000, 25_000_000, 50_000_000, 100_000_000];
    public static readonly IReadOnlyList<long> ImageTargets = [500_000, 1_000_000, 2_000_000, 5_000_000];

    public string Hotkey { get; init; } = HotkeyGesture.Default.Format();

    public bool Animations { get; init; } = true;

    /// <summary>Оформление: graphite (по умолчанию, палитра брифа), brass, paper или auto (по теме Windows).</summary>
    public string Theme { get; init; } = "graphite";

    public static readonly IReadOnlyList<string> Themes = ["auto", "graphite", "brass", "paper"];

    /// <summary>Язык интерфейса: auto (по языку Windows), ru или en.</summary>
    public string Language { get; init; } = "auto";

    public bool OpenFolderWhenDone { get; init; }

    /// <summary>Последний выбранный размер в «Уменьшить…» (см. <see cref="FileDial.Core.Jobs.ResizeRequest.Format"/>).</summary>
    public string Resize { get; init; } = FileDial.Core.Jobs.ResizeRequest.Default.Format();

    /// <summary>Записывать выполненные действия в историю (хранится только на этом компьютере).</summary>
    public bool KeepHistory { get; init; } = true;

    /// <summary>Пункт «FileDial…» в контекстном меню Проводника (для тех, кто не помнит сочетание клавиш).</summary>
    public bool ContextMenu { get; init; } = true;
    /// <summary>Раз в неделю проверять новую версию на GitHub (если в сборке задан репозиторий).</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Папка с ffmpeg.exe и ffprobe.exe; пусто — искать рядом с программой и в PATH.</summary>
    public string? FfmpegFolder { get; init; }

    public long VideoTargetBytes { get; init; } = JobSettings.DefaultVideoTarget;

    public long ImageTargetBytes { get; init; } = JobSettings.DefaultImageTarget;

    /// <summary>Папка для результатов; пусто — рядом с исходным файлом.</summary>
    public string? OutputFolder { get; init; }

    /// <summary>Файл модели whisper.cpp для субтитров и текста из речи.</summary>
    public string? WhisperModel { get; init; }

    /// <summary>Язык речи: auto, ru или en.</summary>
    public string WhisperLanguage { get; init; } = "auto";

    [JsonIgnore]
    public HotkeyGesture HotkeyGesture =>
        HotkeyGesture.TryParse(Hotkey, out var g) && g.Problem() is null ? g : HotkeyGesture.Default;

    [JsonIgnore]
    public JobSettings Jobs => new()
    {
        VideoTargetBytes = VideoTargetBytes,
        ImageTargetBytes = ImageTargetBytes,
        OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder,
        WhisperModel = string.IsNullOrWhiteSpace(WhisperModel) ? null : WhisperModel,
        WhisperLanguage = WhisperLanguage,
        CustomActions = CustomActions,
        FfmpegFolder = string.IsNullOrWhiteSpace(FfmpegFolder) ? null : FfmpegFolder,
    };

    /// <summary>Приводит значения к допустимым: цели — к ближайшим из списка, сочетание — к рабочему.</summary>
    public AppSettings Normalized() => this with
    {
        Hotkey = HotkeyGesture.Format(),
        Theme = Themes.Contains(Theme) ? Theme : "graphite",
        Resize = FileDial.Core.Jobs.ResizeRequest.Parse(Resize).Format(),
        Language = Localization.Lang.Settings.Contains(Language) ? Language : "auto",
        VideoTargetBytes = Nearest(VideoTargets, VideoTargetBytes),
        ImageTargetBytes = Nearest(ImageTargets, ImageTargetBytes),
        FfmpegFolder = string.IsNullOrWhiteSpace(FfmpegFolder) ? null : FfmpegFolder.Trim(),
        OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder.Trim(),
        WhisperModel = string.IsNullOrWhiteSpace(WhisperModel) ? null : WhisperModel.Trim(),
        WhisperLanguage = WhisperLanguage is "ru" or "en" ? WhisperLanguage : "auto",
        CustomActions = Custom.CustomActions.Normalize(CustomActions),
    };

    private static long Nearest(IReadOnlyList<long> allowed, long value) =>
        allowed.OrderBy(a => Math.Abs(a - value)).First();

    // --- Свои действия ---

    /// <summary>Свои действия пользователя: программа с шаблоном аргументов (см. <see cref="Custom.CustomAction"/>).</summary>
    public IReadOnlyList<Custom.CustomAction> CustomActions { get; init; } = Custom.CustomActionList.Empty;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Пути с кириллицей пишем как есть, без \uXXXX — файл удобно читать и править руками.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileDial", "settings.json");

    /// <summary>Загружает настройки; нет файла или он испорчен — значения по умолчанию (программа не падает).</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppSettings();
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json);
            return (loaded ?? new AppSettings()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Сохраняет атомарно: сначала во временный файл, потом замена — при сбое старый файл цел.</summary>
    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings.Normalized(), Json));
        File.Move(temp, path, overwrite: true);
    }
}
