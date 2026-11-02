using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FileDial.App.Interop;
using FileDial.Core.Geometry;
using FileDial.Core.Menu;
using Path = System.Windows.Shapes.Path;

namespace FileDial.App.UI;

/// <summary>Круговое меню: прозрачное окно без рамки поверх всех окон, не в панели задач.</summary>
internal sealed class RadialWindow : Window
{
    private const double FullDiameter = 380;
    // Для неподдерживаемых файлов — небольшой круг без секторов: пустые секторы выглядят как сломанное меню.
    private const double CompactDiameter = 220;
    private const double LabelWidth = 92;

    private readonly SectorGeometry _geometry = new(ActionCatalog.SectorCount, innerRadius: 72, outerRadius: 182, gapDegrees: 1.5);
    private readonly DialMenu _menu;
    private readonly double _diameter;
    private readonly WindowPlacement _placement;
    private readonly Path[] _sectors = new Path[ActionCatalog.SectorCount];
    private readonly TextBlock[] _labels = new TextBlock[ActionCatalog.SectorCount];
    private readonly TextBlock _hint;
    private int? _selected;
    private bool _closing;

    public RadialWindow(DialMenu menu, NativeMethods.POINT cursor)
    {
        _menu = menu;
        _diameter = HasSectors ? FullDiameter : CompactDiameter;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = true;
        Width = Height = _diameter;
        Title = "FileDial";
        UseLayoutRounding = true;

        var canvas = new Canvas { Width = _diameter, Height = _diameter };
        canvas.Children.Add(new Ellipse { Width = _diameter, Height = _diameter, Fill = Palette.Background });

        for (var i = 0; i < (HasSectors ? ActionCatalog.SectorCount : 0); i++)
        {
            _sectors[i] = new Path { Data = BuildSectorGeometry(i), Fill = Palette.Sector };
            canvas.Children.Add(_sectors[i]);

            var action = ActionAt(i);
            var label = new TextBlock
            {
                Width = LabelWidth,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                IsHitTestVisible = false,
            };
            if (action is not null)
            {
                label.Inlines.Add(new System.Windows.Documents.Run($"{i + 1}\n") { FontSize = 11 });
                label.Inlines.Add(new System.Windows.Documents.Run(action.Label));
            }
            _labels[i] = label;
            canvas.Children.Add(label);
            PlaceLabel(i);
        }

        _hint = new TextBlock
        {
            FontSize = 11,
            Foreground = Palette.TextMuted,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 30, // место под две строки, чтобы центр не прыгал при смене подсказки
            Margin = new Thickness(0, 6, 0, 0),
        };
        canvas.Children.Add(BuildCenter());
        Content = canvas;

        UpdateVisuals();

        _placement = new WindowPlacement(this, cursor, PlacementMode.CenterOnCursor, _diameter, _diameter);
        SourceInitialized += (_, _) =>
        {
            WindowPlacement.MakeToolWindow(this, noActivate: false);
            _placement.Apply();
        };
        Loaded += (_, _) =>
        {
            _placement.Apply();
            TakeFocus();
            // Если система не отдала фокус сразу, пробуем ещё раз; без фокуса круг закрываем, чтобы он не «висел».
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (!IsActive && !_closing)
                {
                    TakeFocus(forceAttach: true);
                    if (!IsActive)
                        SafeClose();
                }
            };
            timer.Start();
        };
        Deactivated += (_, _) => SafeClose();
    }

    /// <summary>Выбранное действие после закрытия окна (null — закрыто без действия).</summary>
    public DialAction? Chosen { get; private set; }

    private bool HasSectors => _menu.Kind == DialMenuKind.Actions;

    private double Center => _diameter / 2;

    private int? HitSector(Point p) => HasSectors ? _geometry.HitTest(p.X - Center, p.Y - Center) : null;

    private DialAction? ActionAt(int index) =>
        index < _menu.Actions.Count ? _menu.Actions[index] : null;

    private Geometry BuildSectorGeometry(int index)
    {
        var (start, end) = _geometry.DrawnAngles(index);
        Point P(double angle, double radius)
        {
            var p = SectorGeometry.PointAt(angle, radius);
            return new Point(Center + p.X, Center + p.Y);
        }

        var outer = _geometry.OuterRadius;
        var inner = _geometry.InnerRadius;
        var figure = new PathFigure { StartPoint = P(start, inner), IsClosed = true };
        figure.Segments.Add(new LineSegment(P(start, outer), true));
        figure.Segments.Add(new ArcSegment(P(end, outer), new Size(outer, outer), 0, false, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(P(end, inner), true));
        figure.Segments.Add(new ArcSegment(P(start, inner), new Size(inner, inner), 0, false, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private void PlaceLabel(int index)
    {
        var label = _labels[index];
        label.Measure(new Size(LabelWidth, double.PositiveInfinity));
        var p = _geometry.LabelPoint(index);
        Canvas.SetLeft(label, Center + p.X - LabelWidth / 2);
        Canvas.SetTop(label, Center + p.Y - label.DesiredSize.Height / 2);
    }

    private FrameworkElement BuildCenter()
    {
        var size = HasSectors ? _geometry.InnerRadius * 2 - 16 : _diameter - 48;
        var title = new TextBlock
        {
            Text = _menu.Title,
            Foreground = Palette.Text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 36,
        };
        var subtitle = new TextBlock
        {
            Text = _menu.Subtitle,
            Foreground = _menu.Kind == DialMenuKind.Unsupported ? Palette.Text : Palette.TextMuted,
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var panel = new StackPanel { Width = size, IsHitTestVisible = false };
        panel.Children.Add(title);
        panel.Children.Add(subtitle);
        panel.Children.Add(_hint);
        panel.Measure(new Size(size, double.PositiveInfinity));
        Canvas.SetLeft(panel, Center - size / 2);
        Canvas.SetTop(panel, Center - panel.DesiredSize.Height / 2);
        return panel;
    }

    private void UpdateVisuals()
    {
        for (var i = 0; i < _sectors.Length; i++)
        {
            if (_sectors[i] is null)
                continue;
            var action = ActionAt(i);
            var enabled = action?.IsEnabled == true;
            var selected = _selected == i && action is not null;

            _sectors[i].Fill = selected ? (enabled ? Palette.Accent : Palette.SectorDisabledHover) : Palette.Sector;
            _labels[i].Foreground = selected && enabled ? Palette.AccentText : enabled ? Palette.Text : Palette.TextMuted;
        }

        var current = _selected is { } s ? ActionAt(s) : null;
        _hint.Text = current switch
        {
            null => DefaultHint(),
            { IsEnabled: false } => current.DisabledReason ?? ActionCatalog.NotInPrototype,
            _ => "Enter — запустить",
        };
        _hint.Foreground = Palette.TextMuted;
    }

    /// <summary>Подсказка без выбранного сектора. Если запускать нечего, сразу объясняем почему.</summary>
    private string DefaultHint()
    {
        if (_menu.Kind == DialMenuKind.Unsupported)
            return "Esc — закрыть";
        if (_menu.Actions.Any(a => a.IsEnabled))
            return "1–8, Enter · Esc";
        return _menu.Actions.FirstOrDefault(a => a.DisabledReason is not null and not ActionCatalog.NotInPrototype)
                   ?.DisabledReason
               ?? "Нет доступных действий";
    }

    private void Select(int? index)
    {
        if (_selected == index)
            return;
        _selected = index;
        UpdateVisuals();
    }

    private void Invoke(int index)
    {
        var action = ActionAt(index);
        if (action is not { IsEnabled: true })
            return; // неактивный сектор: ничего не делаем, круг остаётся открытым
        Chosen = action;
        SafeClose();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Select(HitSector(e.GetPosition(this)));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var index = HitSector(e.GetPosition(this));
        if (index is { } i)
            Invoke(i);
        else
            SafeClose(); // клик в центре — вне секторов
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        SafeClose();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Пока зажаты Ctrl+Alt от горячей клавиши, WPF присылает Key.System.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;

        if (key == Key.Escape)
        {
            SafeClose();
            return;
        }
        if (key is Key.Enter or Key.Space)
        {
            if (_selected is { } s)
                Invoke(s);
            return;
        }

        int? digit = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => null,
        };
        if (HasSectors && digit is { } d && _geometry.IndexForDigit(d) is { } index)
            Select(index);
        else
            e.Handled = false;
    }

    private void TakeFocus(bool forceAttach = false)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        Activate();
        if (!NativeMethods.SetForegroundWindow(hwnd) || forceAttach)
        {
            // Запасной путь: временно присоединяем ввод к потоку активного окна.
            var foreground = NativeMethods.GetForegroundWindow();
            var foreignThread = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            var ownThread = NativeMethods.GetCurrentThreadId();
            if (foreignThread != 0 && foreignThread != ownThread &&
                NativeMethods.AttachThreadInput(ownThread, foreignThread, true))
            {
                NativeMethods.BringWindowToTop(hwnd);
                NativeMethods.SetForegroundWindow(hwnd);
                NativeMethods.AttachThreadInput(ownThread, foreignThread, false);
            }
        }
        Focus();
        Keyboard.Focus(this);
    }

    private void SafeClose()
    {
        if (_closing)
            return;
        _closing = true;
        // Закрываем асинхронно: Close внутри Deactivated/обработчиков ввода допустим, но так надёжнее.
        Dispatcher.BeginInvoke(Close);
    }
}
