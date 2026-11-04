namespace FileDial.Core.Localization;

// «Убрать фон»: нейросеть IS-Net, её загрузка в настройках, этапы задачи и имя результата.

public abstract partial class Texts
{
    // Круг
    /// <summary>Причина неактивного сектора: «Нужна модель для фона (179 МБ)».</summary>
    public abstract string BgNeedsModel(string size);

    // Задача
    public abstract string BgStageLoadingModel { get; }
    public abstract string BgStageReading { get; }
    public abstract string BgStageRemoving { get; }
    public abstract string BgStageSaving { get; }
    /// <summary>Часть имени файла: «фото_без_фона.png».</summary>
    public abstract string BgSuffix { get; }
    public abstract string BgDownscaled(int width, int height);
    public abstract string BgModelMissing { get; }
    public abstract string BgModelBroken { get; }

    // Настройки
    public abstract string BgSettingsTitle { get; }
    public abstract string BgSettingsInstalled(string name, string size);
    public abstract string BgSettingsMissing { get; }
    public abstract string BgSettingsDownload(string size);
    public abstract string BgSettingsDownloaded { get; }
    public abstract string BgSettingsDownloadTip { get; }
}

public sealed partial class RuTexts
{
    public override string BgNeedsModel(string size) => $"Нужна модель для фона ({size})";

    public override string BgStageLoadingModel => "Загрузка нейросети…";
    public override string BgStageReading => "Чтение картинки…";
    public override string BgStageRemoving => "Убираем фон…";
    public override string BgStageSaving => "Сохранение…";
    public override string BgSuffix => "_без_фона";
    public override string BgDownscaled(int width, int height) =>
        $"Картинка очень большая — результат уменьшен до {width}×{height}.";
    public override string BgModelMissing => "Модель для удаления фона не найдена — скачайте её в настройках.";
    public override string BgModelBroken => "Не удалось открыть модель для удаления фона — скачайте её заново в настройках.";

    public override string BgSettingsTitle => "Удаление фона";
    public override string BgSettingsInstalled(string name, string size) =>
        $"Используется {name} ({size}). Работает офлайн.";
    public override string BgSettingsMissing =>
        "Модель не установлена — скачайте её кнопкой ниже. Это нужно один раз, дальше всё работает офлайн.";
    public override string BgSettingsDownload(string size) => $"Скачать модель ({size})";
    public override string BgSettingsDownloaded => "Модель скачана";
    public override string BgSettingsDownloadTip => "Нейросеть IS-Net (лицензия Apache-2.0) из проекта rembg на GitHub";
}

public sealed partial class EnTexts
{
    public override string BgNeedsModel(string size) => $"Needs the background model ({size})";

    public override string BgStageLoadingModel => "Loading the neural network…";
    public override string BgStageReading => "Reading the image…";
    public override string BgStageRemoving => "Removing the background…";
    public override string BgStageSaving => "Saving…";
    public override string BgSuffix => "_no_bg";
    public override string BgDownscaled(int width, int height) =>
        $"The image is very large, so the result was reduced to {width}×{height}.";
    public override string BgModelMissing => "The background removal model was not found. Download it in Settings.";
    public override string BgModelBroken => "Could not open the background removal model. Download it again in Settings.";

    public override string BgSettingsTitle => "Background removal";
    public override string BgSettingsInstalled(string name, string size) =>
        $"Using {name} ({size}). Works offline.";
    public override string BgSettingsMissing =>
        "No model installed. Download it with the button below; you only need to do this once, then everything works offline.";
    public override string BgSettingsDownload(string size) => $"Download the model ({size})";
    public override string BgSettingsDownloaded => "Model downloaded";
    public override string BgSettingsDownloadTip => "IS-Net neural network (Apache-2.0 license) from the rembg project on GitHub";
}
