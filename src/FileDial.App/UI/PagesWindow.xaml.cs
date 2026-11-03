using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileDial.Core.Documents;

namespace FileDial.App.UI;

/// <summary>Какие страницы взять из PDF: запись «1-3, 5, 8-» с проверкой на лету.</summary>
public partial class PagesWindow : Window
{
    private readonly int? _pageCount;
    private IReadOnlyList<int>? _pages;

    private PagesWindow(string fileName, int? pageCount)
    {
        InitializeComponent();
        _pageCount = pageCount;
        FileText.Text = fileName;
        CountText.Text = pageCount is { } n ? $"В документе {PdfFiles.PagesLabel(n)}" : "Число страниц станет известно после открытия";
        Loaded += (_, _) => RangeBox.Focus();
    }

    private void OnChanged(object sender, TextChangedEventArgs e)
    {
        // Без числа страниц (PDF под паролем) проверяем только запись; лишние номера отсечёт сама задача.
        _pages = FileDial.Core.Documents.PageRange.TryParse(RangeBox.Text, _pageCount ?? 99_999, out var error);
        OkButton.IsEnabled = _pages is { Count: > 0 };
        ResultText.Text = RangeBox.Text.Length == 0
            ? "Например: 1-3, 5, 8- (до конца). Порядок сохранится как записан."
            : _pages is { } pages ? $"Получится {PdfFiles.PagesLabel(pages.Count)}." : error;
        ResultText.Foreground = _pages is null && RangeBox.Text.Length > 0 ? Palette.Text : Palette.TextMuted;
    }

    /// <summary>Спросить страницы; null — отказались.</summary>
    public static IReadOnlyList<int>? Ask(string fileName, int? pageCount)
    {
        var dialog = new PagesWindow(fileName, pageCount);
        return dialog.ShowDialog() == true ? dialog._pages : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = _pages is { Count: > 0 };

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
            DragMove();
    }
}
