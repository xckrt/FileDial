using FileDial.Core.Jobs;

namespace FileDial.Core.Localization;

// Правка картинок: группа «Повернуть ›», «Кадрировать…» и её окно, чтение HEIC/AVIF.

public abstract partial class Texts
{
    // Группа «Повернуть ›»: короткие подписи — во внешнем кольце, полные — в заголовке окна и в истории.
    public abstract string ImgRotateGroup { get; }
    public abstract string ImgRight { get; }
    public abstract string ImgLeft { get; }
    public abstract string ImgHalfTurn { get; }
    public abstract string ImgRotate180 { get; }
    public abstract string ImgFlipHorizontalShort { get; }
    public abstract string ImgFlipHorizontal { get; }
    public abstract string ImgFlipVerticalShort { get; }
    public abstract string ImgFlipVertical { get; }
    public abstract string ImgSuffixRotated180 { get; }
    public abstract string ImgSuffixFlippedVertical { get; }

    // «Кадрировать…»
    public abstract string ImgCropLabel { get; }
    public abstract string ImgCropCaption { get; }
    public abstract string ImgCropSuffix { get; }
    public abstract string ImgCropNothing(int width, int height);
    public abstract string ImgCropWindowTitle { get; }
    public abstract string ImgCurrentSize(int width, int height);
    public abstract string ImgSizeUnknown { get; }
    public abstract string ImgAspect { get; }
    public abstract string ImgAspectFree { get; }
    public abstract string ImgAspectOriginal { get; }
    public abstract string ImgSwapOrientation { get; }
    public abstract string ImgPosition { get; }
    public abstract string ImgAnchor(CropAnchor anchor);
    public abstract string ImgCropWillBe(int width, int height);
    public abstract string ImgCropWhole { get; }
    public abstract string ImgCropEach(string aspect, CropAnchor anchor);
    public abstract string ImgCropBatchNote { get; }
    public abstract string ImgPreviewLoading { get; }
    public abstract string ImgPreviewFailed { get; }
    public abstract string ImgCropKeys { get; }
    public abstract string ImgCropOk { get; }

    // HEIC/AVIF
    public abstract string ImgHeifUnreadable { get; }
}

public sealed partial class RuTexts
{
    public override string ImgRotateGroup => "Повернуть";
    public override string ImgRight => "Вправо";
    public override string ImgLeft => "Влево";
    public override string ImgHalfTurn => "На 180°";
    public override string ImgRotate180 => "Повернуть на 180°";
    public override string ImgFlipHorizontalShort => "Отразить ↔";
    public override string ImgFlipHorizontal => "Отразить по горизонтали";
    public override string ImgFlipVerticalShort => "Отразить ↕";
    public override string ImgFlipVertical => "Отразить по вертикали";
    public override string ImgSuffixRotated180 => "_повернуто_180";
    public override string ImgSuffixFlippedVertical => "_отражено_по_вертикали";

    public override string ImgCropLabel => "Кадрировать…";
    public override string ImgCropCaption => "Кадрировать";
    public override string ImgCropSuffix => "_кадр";
    public override string ImgCropNothing(int width, int height) =>
        $"Кадр совпадает со всей картинкой ({width}×{height}) — отрезать нечего.";
    public override string ImgCropWindowTitle => "FileDial — кадрировать";
    public override string ImgCurrentSize(int width, int height) => $"Сейчас {width}×{height}";
    public override string ImgSizeUnknown => "Размер станет известен при обработке";
    public override string ImgAspect => "Пропорции";
    public override string ImgAspectFree => "Свободно";
    public override string ImgAspectOriginal => "Как у снимка";
    public override string ImgSwapOrientation => "Повернуть рамку: портрет ↔ альбом";
    public override string ImgPosition => "Какую часть оставить";
    public override string ImgAnchor(CropAnchor anchor) => anchor switch
    {
        CropAnchor.Top => "Верх",
        CropAnchor.Bottom => "Низ",
        CropAnchor.Left => "Левую",
        CropAnchor.Right => "Правую",
        _ => "Середину",
    };
    public override string ImgCropWillBe(int width, int height) => $"Получится {width}×{height}.";
    public override string ImgCropWhole => "Рамка во всю картинку — сдвиньте её углы или выберите пропорции.";
    public override string ImgCropEach(string aspect, CropAnchor anchor) => anchor switch
    {
        CropAnchor.Top => $"У каждой картинки останется наибольший кадр {aspect}, прижатый к верху.",
        CropAnchor.Bottom => $"У каждой картинки останется наибольший кадр {aspect}, прижатый к низу.",
        CropAnchor.Left => $"У каждой картинки останется наибольший кадр {aspect}, прижатый к левому краю.",
        CropAnchor.Right => $"У каждой картинки останется наибольший кадр {aspect}, прижатый к правому краю.",
        _ => $"У каждой картинки останется наибольший кадр {aspect} по центру.",
    };
    public override string ImgCropBatchNote => "Картинки, у которых уже такие пропорции, пропускаются.";
    public override string ImgPreviewLoading => "Готовлю превью…";
    public override string ImgPreviewFailed => "Превью не получилось, но кадр всё равно можно выбрать.";
    public override string ImgCropKeys => "Перетащите рамку или её углы. Стрелки сдвигают рамку, с Shift — быстрее.";
    public override string ImgCropOk => "Кадрировать";

    public override string ImgHeifUnreadable =>
        "Не удалось прочитать HEIC/AVIF. Нужен ffmpeg 7.1 или новее — обновите его в настройках.";
}

public sealed partial class EnTexts
{
    public override string ImgRotateGroup => "Rotate";
    public override string ImgRight => "Right";
    public override string ImgLeft => "Left";
    public override string ImgHalfTurn => "180°";
    public override string ImgRotate180 => "Rotate 180°";
    public override string ImgFlipHorizontalShort => "Flip ↔";
    public override string ImgFlipHorizontal => "Flip horizontally";
    public override string ImgFlipVerticalShort => "Flip ↕";
    public override string ImgFlipVertical => "Flip vertically";
    public override string ImgSuffixRotated180 => "_rotated_180";
    public override string ImgSuffixFlippedVertical => "_flipped_vertically";

    public override string ImgCropLabel => "Crop…";
    public override string ImgCropCaption => "Crop";
    public override string ImgCropSuffix => "_crop";
    public override string ImgCropNothing(int width, int height) =>
        $"The frame covers the whole image ({width}×{height}), nothing to cut.";
    public override string ImgCropWindowTitle => "FileDial — crop";
    public override string ImgCurrentSize(int width, int height) => $"Now {width}×{height}";
    public override string ImgSizeUnknown => "The size will be read during processing";
    public override string ImgAspect => "Aspect ratio";
    public override string ImgAspectFree => "Free";
    public override string ImgAspectOriginal => "Original";
    public override string ImgSwapOrientation => "Turn the frame: portrait ↔ landscape";
    public override string ImgPosition => "Which part to keep";
    public override string ImgAnchor(CropAnchor anchor) => anchor switch
    {
        CropAnchor.Top => "Top",
        CropAnchor.Bottom => "Bottom",
        CropAnchor.Left => "Left",
        CropAnchor.Right => "Right",
        _ => "Center",
    };
    public override string ImgCropWillBe(int width, int height) => $"Result: {width}×{height}.";
    public override string ImgCropWhole => "The frame covers the whole image. Drag its corners or pick an aspect ratio.";
    public override string ImgCropEach(string aspect, CropAnchor anchor) => anchor switch
    {
        CropAnchor.Top => $"Each image keeps its largest {aspect} frame, aligned to the top.",
        CropAnchor.Bottom => $"Each image keeps its largest {aspect} frame, aligned to the bottom.",
        CropAnchor.Left => $"Each image keeps its largest {aspect} frame, aligned to the left.",
        CropAnchor.Right => $"Each image keeps its largest {aspect} frame, aligned to the right.",
        _ => $"Each image keeps its largest {aspect} frame, centered.",
    };
    public override string ImgCropBatchNote => "Images that already have this aspect ratio are skipped.";
    public override string ImgPreviewLoading => "Preparing preview…";
    public override string ImgPreviewFailed => "Couldn't show a preview, but you can still pick the frame.";
    public override string ImgCropKeys => "Drag the frame or its corners. Arrow keys move it, Shift moves faster.";
    public override string ImgCropOk => "Crop";

    public override string ImgHeifUnreadable =>
        "Couldn't read this HEIC/AVIF. It needs ffmpeg 7.1 or newer; update it in Settings.";
}
