using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FileDial.App.Interop;
using FileDial.Core.History;
using FileDial.Core.Localization;
using Path = System.IO.Path;

namespace FileDial.App.UI;

/// <summary>История выполненных действий: по дням, с переходом к результату и исходнику.</summary>
public partial class HistoryWindow : Window
{
    private readonly Func<IReadOnlyList<HistoryEntry>> _load;
    private readonly Action _clear;
    private readonly Func<bool> _enabled;
    private DispatcherTimer? _confirmTimer;

    /// <param name="load">Читает историю (от новых к старым).</param>
    /// <param name="clear">Удаляет историю.</param>
    /// <param name="enabled">Включена ли запись истории в настройках.</param>
    internal HistoryWindow(Func<IReadOnlyList<HistoryEntry>> load, Action clear, Func<bool> enabled,
        NativeMethods.POINT cursor)
    {
        InitializeComponent();
        _load = load;
        _clear = clear;
        _enabled = enabled;

        var placement = new WindowPlacement(this, cursor, PlacementMode.CenterOnCursor, Width, Height);
        SourceInitialized += (_, _) => placement.Apply();
        Opacity = 0;
        Loaded += (_, _) =>
        {
            placement.Apply();
            Motion.To(this, OpacityProperty, 1, Motion.Normal);
        };
        Refresh();
    }

    /// <summary>Перечитать историю (например, после нового действия).</summary>
    public void Refresh()
    {
        var entries = _load();
        ListPanel.Children.Clear();
        ClearButton.IsEnabled = entries.Count > 0;

        var done = entries.Count(e => e.Status == HistoryStatus.Done);
        SummaryText.Text = entries.Count == 0
            ? (_enabled() ? Lang.T.UiHistoryEmpty : Lang.T.UiHistoryDisabled)
            : Lang.T.UiHistorySummary(entries.Count, done) + (_enabled() ? "" : Lang.T.UiHistorySummaryDisabled);

        if (entries.Count == 0)
        {
            ListPanel.Children.Add(new TextBlock
            {
                Text = _enabled()
                    ? Lang.T.UiHistoryEmptyHint
                    : Lang.T.UiHistoryEnableHint,
                Foreground = Palette.TextMuted,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 24, 0, 0),
            });
            return;
        }

        foreach (var (label, group) in HistoryText.GroupByDay(entries, DateTime.Now))
        {
            ListPanel.Children.Add(new TextBlock
            {
                Text = label,
                Style = (Style)FindResource("SectionTitle"),
            });
            foreach (var entry in group)
                ListPanel.Children.Add(Card(entry));
        }
    }

    private FrameworkElement Card(HistoryEntry entry)
    {
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = entry.Status switch
            {
                HistoryStatus.Done => Palette.Accent,
                HistoryStatus.Failed => Palette.Text,
                _ => Palette.TextMuted,
            },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var title = new TextBlock
        {
            Text = entry.Action,
            Foreground = Palette.Text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var meta = new TextBlock
        {
            Text = $"{HistoryText.StatusLabel(entry.Status)} · {entry.Time.ToLocalTime():HH:mm}",
            Foreground = Palette.TextMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new DockPanel();
        DockPanel.SetDock(meta, Dock.Right);
        header.Children.Add(meta);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, title } });

        var file = new TextBlock
        {
            Text = Path.GetFileName(entry.SourcePath),
            ToolTip = entry.SourcePath,
            Foreground = Palette.TextMuted,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(16, 2, 0, 0),
        };
        var message = new TextBlock
        {
            Text = entry.Message,
            Foreground = entry.Status == HistoryStatus.Failed ? Palette.Text : Palette.TextMuted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 4, 0, 0),
        };

        var buttons = new WrapPanel { Margin = new Thickness(16, 8, 0, 0) };
        // Файлы могли удалить или переместить после действия — кнопки только для существующих.
        if (entry.OutputPath is { } output && File.Exists(output))
            buttons.Children.Add(SmallButton(Lang.T.UiShowResult, () => ShellActions.ShowInFolder(output)));
        else if (entry.Status == HistoryStatus.Done)
            buttons.Children.Add(new TextBlock { Text = Lang.T.UiResultGone, Foreground = Palette.TextMuted, FontSize = 12 });
        if (File.Exists(entry.SourcePath))
            buttons.Children.Add(SmallButton(Lang.T.UiShowSource, () => ShellActions.ShowInFolder(entry.SourcePath)));

        var body = new StackPanel { Children = { header, file, message } };
        if (buttons.Children.Count > 0)
            body.Children.Add(buttons);

        return new Border
        {
            BorderBrush = Palette.Sector,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            Child = body,
        };
    }

    private Button SmallButton(string text, Action click)
    {
        var button = new Button { Content = text, Style = (Style)FindResource("SmallButton") };
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Очистка в два нажатия: первое спрашивает, второе (в течение 4 с) удаляет.</summary>
    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (_confirmTimer is null)
        {
            ClearButton.Content = Lang.T.UiHistoryConfirmClear;
            _confirmTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _confirmTimer.Tick += (_, _) => ResetClearButton();
            _confirmTimer.Start();
            return;
        }

        ResetClearButton();
        _clear();
        Refresh();
    }

    private void ResetClearButton()
    {
        _confirmTimer?.Stop();
        _confirmTimer = null;
        ClearButton.Content = Lang.T.UiHistoryClear;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
