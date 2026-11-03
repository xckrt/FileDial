using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileDial.Core.Archives;
using FileDial.Core.Text;

namespace FileDial.App.UI;

/// <summary>Выбор размера части. Размеры не меньше самого файла неактивны — делить было бы нечего.</summary>
public partial class SplitWindow : Window
{
    private readonly long _fileSize;
    private long? _partSize;

    private SplitWindow(string title, long fileSize)
    {
        InitializeComponent();
        _fileSize = fileSize;
        FileText.Text = title;
        SizeText.Text = SizeFormatter.Format(fileSize);

        foreach (var (label, bytes) in FileParts.Presets)
        {
            var button = new RadioButton
            {
                Content = label,
                Tag = bytes,
                GroupName = "PartSize",
                Style = (Style)Application.Current.FindResource("SegmentRadio"),
                Margin = new Thickness(0, 0, 6, 6),
                IsEnabled = bytes < fileSize,
                ToolTip = bytes < fileSize ? null : "Файл меньше части — делить нечего",
            };
            button.Checked += (_, _) => Select(bytes);
            Presets.Children.Add(button);
        }

        // По умолчанию — 25 МБ (почта, мессенджеры), если подходит; иначе самый крупный подходящий.
        var initial = Presets.Children.OfType<RadioButton>().Where(b => b.IsEnabled)
            .OrderBy(b => (long)b.Tag == 25_000_000 ? 0 : 1).ThenByDescending(b => (long)b.Tag).FirstOrDefault();
        if (initial is null)
            ResultText.Text = "Файл меньше 10 МБ — делить его незачем.";
        else
            initial.IsChecked = true;
    }

    private void Select(long bytes)
    {
        _partSize = bytes;
        var count = FileParts.PartCount(_fileSize, bytes);
        ResultText.Text = count > 999
            ? "Получилось бы больше 999 частей — выберите размер побольше."
            : $"Получится {FileParts.PartsLabel(count)}.";
        OkButton.IsEnabled = count is > 1 and <= 999;
    }

    /// <summary>Спросить размер части; null — отказались.</summary>
    public static long? Ask(string title, long fileSize)
    {
        var dialog = new SplitWindow(title, fileSize);
        return dialog.ShowDialog() == true ? dialog._partSize : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
