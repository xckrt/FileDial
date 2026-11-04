namespace FileDial.Core.Localization;

// «Уменьшить…»: сектор картинки и окно выбора размера.

public abstract partial class Texts
{
    public abstract string RszLabel { get; }
    public abstract string RszCaption { get; }
    public abstract string RszAlreadySmall(int width, int height);
    public abstract string RszWindowTitle { get; }
    public abstract string RszCurrentSize(int width, int height);
    public abstract string RszSizeUnknown { get; }
    public abstract string RszByLongSide { get; }
    public abstract string RszByPercent { get; }
    public abstract string RszCustom { get; }
    public abstract string RszKeepAspect { get; }
    public abstract string RszWidth { get; }
    public abstract string RszHeight { get; }
    public abstract string RszWillBe(int width, int height);
    public abstract string RszNothingToShrink { get; }
    public abstract string RszInvalid(int max);
    public abstract string RszEachLongSide(int side);
    public abstract string RszEachPercent(int percent);
    public abstract string RszEachBox(int width, int height);
    public abstract string RszEachExact(int width, int height);
    public abstract string RszBatchNote { get; }
    public abstract string RszPresetTooBig { get; }
    public abstract string RszOk { get; }
}

public sealed partial class RuTexts
{
    public override string RszLabel => "Уменьшить…";
    public override string RszCaption => "Уменьшить размер";
    public override string RszAlreadySmall(int width, int height) => $"Картинка и так не больше ({width}×{height}) — уменьшать нечего.";
    public override string RszWindowTitle => "FileDial — уменьшить";
    public override string RszCurrentSize(int width, int height) => $"Сейчас {width}×{height}";
    public override string RszSizeUnknown => "Размер станет известен при обработке";
    public override string RszByLongSide => "По длинной стороне";
    public override string RszByPercent => "В процентах";
    public override string RszCustom => "Свой размер, px";
    public override string RszKeepAspect => "Сохранять пропорции";
    public override string RszWidth => "Ширина";
    public override string RszHeight => "Высота";
    public override string RszWillBe(int width, int height) => $"Получится {width}×{height}.";
    public override string RszNothingToShrink => "Картинка уже не больше — выберите размер поменьше.";
    public override string RszInvalid(int max) => $"Ширина и высота — от 1 до {max}.";
    public override string RszEachLongSide(int side) => $"Каждая картинка — не больше {side} px по длинной стороне.";
    public override string RszEachPercent(int percent) => $"Каждая картинка — {percent}% от своего размера.";
    public override string RszEachBox(int width, int height) => $"Каждая картинка впишется в {width}×{height} с сохранением пропорций.";
    public override string RszEachExact(int width, int height) => $"Каждая картинка станет ровно {width}×{height}.";
    public override string RszBatchNote => "Картинки, которые уже не больше, пропускаются.";
    public override string RszPresetTooBig => "Картинка и так не больше";
    public override string RszOk => "Уменьшить";
}

public sealed partial class EnTexts
{
    public override string RszLabel => "Resize…";
    public override string RszCaption => "Resize";
    public override string RszAlreadySmall(int width, int height) => $"The image is already that small ({width}×{height}), nothing to shrink.";
    public override string RszWindowTitle => "FileDial — resize";
    public override string RszCurrentSize(int width, int height) => $"Now {width}×{height}";
    public override string RszSizeUnknown => "The size will be read during processing";
    public override string RszByLongSide => "Longest side";
    public override string RszByPercent => "Percent";
    public override string RszCustom => "Custom size, px";
    public override string RszKeepAspect => "Keep proportions";
    public override string RszWidth => "Width";
    public override string RszHeight => "Height";
    public override string RszWillBe(int width, int height) => $"Result: {width}×{height}.";
    public override string RszNothingToShrink => "The image is already this small. Pick a smaller size.";
    public override string RszInvalid(int max) => $"Width and height must be between 1 and {max}.";
    public override string RszEachLongSide(int side) => $"Each image: longest side at most {side} px.";
    public override string RszEachPercent(int percent) => $"Each image: {percent}% of its size.";
    public override string RszEachBox(int width, int height) => $"Each image fits into {width}×{height}, proportions kept.";
    public override string RszEachExact(int width, int height) => $"Each image becomes exactly {width}×{height}.";
    public override string RszBatchNote => "Images that are already this small are skipped.";
    public override string RszPresetTooBig => "The image is already this small";
    public override string RszOk => "Resize";
}
