using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;

namespace FileDial.App.UI;

/// <summary>
/// «Уменьшить…»: готовые размеры по длинной стороне, проценты или свой размер. Для одной картинки окно знает её
/// размер: показывает итог, гасит размеры не меньше картинки и при включённых пропорциях досчитывает вторую сторону.
/// </summary>
public partial class ResizeWindow : Window
{
    private readonly (int Width, int Height)? _size;
    private ResizeRequest? _request;
    // Пока окно само заполняет поля, правка полей не должна переключать режим на «свой размер».
    private bool _updating;

    private ResizeWindow(string title, (int Width, int Height)? size, ResizeRequest initial)
    {
        InitializeComponent();
        _size = size;
        FileText.Text = title;
        SizeText.Text = size is { } s ? Lang.T.RszCurrentSize(s.Width, s.Height) : Lang.T.RszSizeUnknown;
        NoteText.Text = size is null ? Lang.T.RszBatchNote : "";
        NoteText.Visibility = size is null ? Visibility.Visible : Visibility.Collapsed;

        foreach (var side in ResizeRequest.LongSides)
            LongSides.Children.Add(Preset($"{side} px", new ResizeRequest(ResizeBy.LongSide, side)));
        foreach (var percent in ResizeRequest.Percents)
            Percents.Children.Add(Preset($"{percent}%", new ResizeRequest(ResizeBy.Percent, percent)));

        Select(Usable(initial) ? initial : FirstUsablePreset());
        Loaded += (_, _) => (Presets.FirstOrDefault(p => p.IsChecked == true) as UIElement ?? WidthBox).Focus();
    }

    private IEnumerable<RadioButton> Presets =>
        LongSides.Children.OfType<RadioButton>().Concat(Percents.Children.OfType<RadioButton>());

    private RadioButton Preset(string label, ResizeRequest request)
    {
        // Размер, до которого картинку не уменьшить (она и так не больше), неактивен — как в «Разбить на части».
        var usable = Usable(request);
        var button = new RadioButton
        {
            Content = label,
            Tag = request,
            GroupName = "Resize",
            Style = (Style)Application.Current.FindResource("SegmentRadio"),
            Margin = new Thickness(0, 0, 6, 6),
            IsEnabled = usable,
            ToolTip = usable ? null : Lang.T.RszPresetTooBig,
        };
        button.Checked += (_, _) =>
        {
            if (!_updating)
                Select(request);
        };
        return button;
    }

    private bool Usable(ResizeRequest request) =>
        request.IsValid && (_size is not { } s || request.Shrinks(s.Width, s.Height));

    private ResizeRequest FirstUsablePreset() =>
        Presets.Select(p => (ResizeRequest)p.Tag).FirstOrDefault(Usable) ?? new ResizeRequest(ResizeBy.Percent, 50);

    /// <summary>Выбор готового размера или восстановление запомненного: отметка, поля и итог.</summary>
    private void Select(ResizeRequest request)
    {
        _updating = true;
        foreach (var preset in Presets)
            preset.IsChecked = Equals(preset.Tag, request);
        if (request.Mode == ResizeBy.Exact)
        {
            WidthBox.Text = Number(request.Width);
            HeightBox.Text = Number(request.Height);
            KeepAspectBox.IsChecked = request.KeepAspect;
        }
        else if (_size is { } s)
        {
            // Поля показывают, во что превратится готовый размер, — с них удобно начать свой.
            var (w, h) = request.Target(s.Width, s.Height);
            WidthBox.Text = Number(w);
            HeightBox.Text = Number(h);
            KeepAspectBox.IsChecked = true;
        }
        else
        {
            WidthBox.Text = HeightBox.Text = "";
        }
        _updating = false;
        Show(request);
    }

    private void OnCustomChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating)
            return;
        _updating = true;
        foreach (var preset in Presets)
            preset.IsChecked = false;
        // С пропорциями вторая сторона досчитывается по размеру картинки (для нескольких картинок — рамка, без этого).
        if (KeepAspectBox.IsChecked == true && _size is { } s && sender is TextBox edited && TryNumber(edited.Text, out var value))
        {
            if (edited == WidthBox)
                HeightBox.Text = Number(Math.Max(1, (int)Math.Round(value * (double)s.Height / s.Width)));
            else
                WidthBox.Text = Number(Math.Max(1, (int)Math.Round(value * (double)s.Width / s.Height)));
        }
        _updating = false;
        Show(Custom());
    }

    private void OnKeepAspectChanged(object sender, RoutedEventArgs e)
    {
        if (_updating || !IsLoaded)
            return;
        // Включили пропорции — подгоняем высоту под ширину.
        if (KeepAspectBox.IsChecked == true)
            OnCustomChanged(WidthBox, null!);
        else
            Show(Custom());
    }

    private ResizeRequest? Custom() =>
        TryNumber(WidthBox.Text, out var w) && TryNumber(HeightBox.Text, out var h)
            ? new ResizeRequest(ResizeBy.Exact, 0, w, h, KeepAspectBox.IsChecked == true)
            : null;

    private void Show(ResizeRequest? request)
    {
        _request = request is { IsValid: true } ? request : null;
        if (_request is null)
        {
            ResultText.Text = Lang.T.RszInvalid(ResizeRequest.MaxSide);
            ResultText.Foreground = Palette.Text;
            OkButton.IsEnabled = false;
            return;
        }

        ResultText.Foreground = Palette.Text;
        if (_size is { } s)
        {
            var shrinks = _request.Shrinks(s.Width, s.Height);
            var (w, h) = _request.Target(s.Width, s.Height);
            ResultText.Text = shrinks ? Lang.T.RszWillBe(w, h) : Lang.T.RszNothingToShrink;
            OkButton.IsEnabled = shrinks;
            return;
        }

        ResultText.Text = _request.Mode switch
        {
            ResizeBy.Percent => Lang.T.RszEachPercent(_request.Value),
            ResizeBy.Exact when _request.KeepAspect => Lang.T.RszEachBox(_request.Width, _request.Height),
            ResizeBy.Exact => Lang.T.RszEachExact(_request.Width, _request.Height),
            _ => Lang.T.RszEachLongSide(_request.Value),
        };
        OkButton.IsEnabled = true;
    }

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Спросить размер; null — отказались.</summary>
    /// <param name="size">Размер картинки, если она одна и он известен.</param>
    public static ResizeRequest? Ask(string title, (int Width, int Height)? size, ResizeRequest initial)
    {
        var dialog = new ResizeWindow(title, size, initial);
        return dialog.ShowDialog() == true ? dialog._request : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = _request is not null;

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
            DragMove();
    }
}
