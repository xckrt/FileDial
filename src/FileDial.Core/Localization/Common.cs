namespace FileDial.Core.Localization;

// Общее: единицы размера, выбор языка.

public abstract partial class Texts
{
    public abstract string Bytes { get; }
    public abstract string Kilobytes { get; }
    public abstract string Megabytes { get; }
    public abstract string Gigabytes { get; }

    /// <summary>«Как в Windows» — у темы и языка.</summary>
    public abstract string LikeWindows { get; }
    public abstract string LanguageTitle { get; }
}

public sealed partial class RuTexts
{
    public override string Bytes => "Б";
    public override string Kilobytes => "КБ";
    public override string Megabytes => "МБ";
    public override string Gigabytes => "ГБ";
    public override string LikeWindows => "Как в Windows";
    public override string LanguageTitle => "Язык";
}

public sealed partial class EnTexts
{
    public override string Bytes => "B";
    public override string Kilobytes => "KB";
    public override string Megabytes => "MB";
    public override string Gigabytes => "GB";
    public override string LikeWindows => "Match Windows";
    public override string LanguageTitle => "Language";
}
