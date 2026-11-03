using System.ComponentModel;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Markup;
using FileDial.Core.Localization;

namespace FileDial.App.UI;

/// <summary>
/// Строки для XAML: <c>Text="{l:T SettingsTitle}"</c> — свойство <see cref="Texts"/> по имени.
/// Это привязка, поэтому при смене языка открытое окно переводится сразу.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = LocSource.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

public sealed class LocSource : INotifyPropertyChanged
{
    public static LocSource Instance { get; } = new();

    private LocSource() => Lang.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] =>
        typeof(Texts).GetProperty(key, BindingFlags.Public | BindingFlags.Instance)?.GetValue(Lang.T) as string
        ?? $"[{key}]";
}
