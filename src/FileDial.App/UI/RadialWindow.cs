using System.Globalization;
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

/// <summary>
/// Круговое меню: прозрачное окно без рамки поверх всех окон, не в панели задач.
/// Выглядит как поворотная ручка: шкала делений по ободу и указатель, который переезжает к выбранному сектору.
/// Сектор-группа (например, «Конвертировать») при наведении раскрывает снаружи круга веер вложенных секторов.
/// </summary>
internal sealed class RadialWindow : Window
{
    private const double MainDiameter = 380;
    // Для неподдерживаемых файлов — небольшой круг без секторов: пустые секторы выглядят как сломанное меню.
    private const double CompactDiameter = 220;
    private const double LabelWidth = 92;
    private const double ChildLabelWidth = 64;
    /// <summary>На сколько выбранный сектор выдвигается наружу.</summary>
    private const double PopDistance = 5;

    // Шкала делений по ободу: между краем секторов и краем круга.
    private const double TickMinor = 4;
    private const double TickMajor = 7;

    private const int OpenDuration = 260;

    // Внешнее кольцо с вложенными секторами.
    private const double ChildInner = 196;
    private const double ChildOuter = 264;
    private const double ChildSweep = 30;
    private const double ChildGap = 1.5;
    /// <summary>Окно с запасом под внешнее кольцо и выдвижение сектора.</summary>
    private const double WithSubmenuSize = 2 * (ChildOuter + PopDistance + 6);

    private readonly SectorGeometry _geometry = new(ActionCatalog.SectorCount, innerRadius: 72, outerRadius: 176, gapDegrees: 1.5);
    private readonly DialMenu _menu;
    private readonly double _size;
    private readonly WindowPlacement _placement;
    private readonly Path[] _sectors = new Path[ActionCatalog.SectorCount];
    // Свои (незамороженные) кисти и сдвиги у каждого сектора — чтобы их можно было анимировать.
    private readonly SolidColorBrush[] _fills = new SolidColorBrush[ActionCatalog.SectorCount];
    private readonly SolidColorBrush[] _labelBrushes = new SolidColorBrush[ActionCatalog.SectorCount];
    private readonly TranslateTransform[] _shifts = new TranslateTransform[ActionCatalog.SectorCount];
    // Сектор со своей подписью, значком и номером — одна группа, чтобы появляться по очереди.
    private readonly Canvas[] _groups = new Canvas[ActionCatalog.SectorCount];
    private readonly Path?[] _markers = new Path?[ActionCatalog.SectorCount];
    private readonly Canvas _canvas;
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly RotateTransform _spin = new();
    private readonly TextBlock _hint;

    // Указатель на ободе: переезжает к выбранному сектору.
    private Path? _pointer;
    private readonly RotateTransform _pointerTurn = new();
    private readonly SolidColorBrush _pointerFill = new(Palette.Accent.Color);
    private double _pointerAngle;
    private bool _pointerShown;

    // Внешнее кольцо: строится при раскрытии группы.
    private readonly Canvas _subLayer;
    private readonly ScaleTransform _subScale = new(1, 1);
    private SubmenuGeometry? _sub;
    private readonly List<(Path Sector, SolidColorBrush Fill, SolidColorBrush Text, TranslateTransform Shift)> _children = [];

    private int? _selected;
    private int? _expanded;
    private int? _selectedChild;
    private bool _pressed;
    private bool _closing;
    private bool _isClosed;

    public RadialWindow(DialMenu menu, NativeMethods.POINT cursor)
    {
        _menu = menu;
        _size = !HasSectors ? CompactDiameter : menu.Actions.Any(a => a.IsGroup) ? WithSubmenuSize : MainDiameter;
        var circle = HasSectors ? MainDiameter : CompactDiameter;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = true;
        Width = Height = _size;
        Title = "FileDial";
        UseLayoutRounding = true;

        var canvas = _canvas = new Canvas
        {
            Width = _size,
            Height = _size,
            RenderTransform = new TransformGroup { Children = { _scale, _spin } },
            RenderTransformOrigin = new Point(0.5, 0.5),
        };

        // Слой веера — под основным кругом, чтобы выдвинутый сектор группы рисовался поверх.
        _subLayer = new Canvas
        {
            Width = _size,
            Height = _size,
            RenderTransform = _subScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Visibility = Visibility.Collapsed,
        };
        canvas.Children.Add(_subLayer);

        var background = new Ellipse
        {
            Width = circle,
            Height = circle,
            Fill = Palette.Background,
            Stroke = Palette.Rim,
            StrokeThickness = 1,
        };
        Canvas.SetLeft(background, Center - circle / 2);
        Canvas.SetTop(background, Center - circle / 2);
        canvas.Children.Add(background);
        foreach (var ticks in Ticks(circle / 2 - 4))
            canvas.Children.Add(ticks);

        for (var i = 0; i < (HasSectors ? ActionCatalog.SectorCount : 0); i++)
        {
            _fills[i] = new SolidColorBrush(Palette.Sector.Color);
            _labelBrushes[i] = new SolidColorBrush(Palette.Text.Color);
            _shifts[i] = new TranslateTransform();
            var group = _groups[i] = new Canvas();
            var (start, end) = _geometry.DrawnAngles(i);
            _sectors[i] = new Path
            {
                Data = RingSector(start, end, _geometry.InnerRadius, _geometry.OuterRadius),
                Fill = _fills[i],
                RenderTransform = _shifts[i],
            };
            group.Children.Add(_sectors[i]);

            if (ActionAt(i) is { } action)
            {
                var label = ActionLabel(action, _labelBrushes[i], _shifts[i]);
                group.Children.Add(label);
                Place(label, _geometry.LabelPoint(i), LabelWidth);
                group.Children.Add(Digit(i));

                // Группа отмечена маленьким треугольником у внешнего края: там раскроется веер.
                if (action.IsGroup)
                    group.Children.Add(_markers[i] = GroupMarker(i));
            }
            canvas.Children.Add(group);
        }

        if (HasSectors)
        {
            // Тонкое кольцо вокруг центра — как втулка у ручки.
            var hub = _geometry.InnerRadius - 7;
            var ring = new Ellipse
            {
                Width = hub * 2,
                Height = hub * 2,
                Stroke = Palette.Rim,
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(ring, Center - hub);
            Canvas.SetTop(ring, Center - hub);
            canvas.Children.Add(ring);
            canvas.Children.Add(_pointer = Pointer(circle / 2));
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

        _placement = new WindowPlacement(this, cursor, PlacementMode.CenterOnCursor, _size, _size);
        SourceInitialized += (_, _) =>
        {
            WindowPlacement.MakeToolWindow(this, noActivate: false);
            _placement.Apply();
        };
        // Появление: круг доворачивается, как ручка, а секторы зажигаются по часовой стрелке.
        if (Motion.Enabled)
        {
            _canvas.Opacity = 0;
            _scale.ScaleX = _scale.ScaleY = 0.9;
            _spin.Angle = -18;
            foreach (var group in _groups)
                if (group is not null)
                    group.Opacity = 0;
        }
        Loaded += (_, _) =>
        {
            Motion.To(_canvas, OpacityProperty, 1, Motion.Normal);
            Motion.To(_scale, ScaleTransform.ScaleXProperty, 1, OpenDuration);
            Motion.To(_scale, ScaleTransform.ScaleYProperty, 1, OpenDuration);
            Motion.To(_spin, RotateTransform.AngleProperty, 0, OpenDuration);
            for (var i = 0; i < _groups.Length; i++)
                if (_groups[i] is { } group)
                    Motion.Animate(group, OpacityProperty, 0, 1, 140, delay: 30 + i * 22);
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
        Closed += (_, _) => _isClosed = true;
    }

    /// <summary>Выбранное действие после закрытия окна (null — закрыто без действия).</summary>
    public DialAction? Chosen { get; private set; }

    private bool HasSectors => _menu.Kind == DialMenuKind.Actions;

    private double Center => _size / 2;

    private DialAction? ActionAt(int index) =>
        index < _menu.Actions.Count ? _menu.Actions[index] : null;

    private IReadOnlyList<DialAction> ExpandedChildren =>
        _expanded is { } g && ActionAt(g)?.Children is { } children ? children : [];

    private DialAction? ChildAt(int index) =>
        index < ExpandedChildren.Count ? ExpandedChildren[index] : null;

    // --- Построение ---

    private Point ToCanvas(PointD p) => new(Center + p.X, Center + p.Y);

    private Geometry RingSector(double start, double end, double inner, double outer)
    {
        Point P(double angle, double radius) => ToCanvas(SectorGeometry.PointAt(angle, radius));

        var large = end - start > 180;
        var figure = new PathFigure { StartPoint = P(start, inner), IsClosed = true };
        figure.Segments.Add(new LineSegment(P(start, outer), true));
        figure.Segments.Add(new ArcSegment(P(end, outer), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(P(end, inner), true));
        figure.Segments.Add(new ArcSegment(P(start, inner), new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>Значок над названием действия. Кисть общая — подсвечиваются вместе.</summary>
    private static StackPanel ActionLabel(DialAction action, Brush foreground, Transform shift)
    {
        var panel = new StackPanel { Width = LabelWidth, IsHitTestVisible = false, RenderTransform = shift };
        if (Icons.Create(action.Id, foreground) is { } icon)
        {
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(icon);
        }
        panel.Children.Add(new TextBlock
        {
            Text = action.Label,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Palette.DialFont,
            FontSize = 14,
            Foreground = foreground,
        });
        return panel;
    }

    /// <summary>Номер клавиши — мелко в углу сектора у обода, как индекс на шкале.</summary>
    private TextBlock Digit(int index)
    {
        var digit = new TextBlock
        {
            Text = (index + 1).ToString(CultureInfo.InvariantCulture),
            FontFamily = Palette.DialFont,
            FontSize = 11,
            Foreground = _labelBrushes[index],
            Opacity = 0.6,
            IsHitTestVisible = false,
            RenderTransform = _shifts[index],
        };
        var point = SectorGeometry.PointAt(_geometry.CenterAngle(index) - 15, _geometry.OuterRadius - 12);
        digit.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(digit, Center + point.X - digit.DesiredSize.Width / 2);
        Canvas.SetTop(digit, Center + point.Y - digit.DesiredSize.Height / 2);
        return digit;
    }

    /// <summary>Деления через 5°; длинные — напротив середин секторов.</summary>
    private IEnumerable<Path> Ticks(double outer)
    {
        var minor = new StreamGeometry();
        var major = new StreamGeometry();
        using (var minorContext = minor.Open())
        using (var majorContext = major.Open())
        {
            for (var angle = 0; angle < 360; angle += 5)
            {
                var isMajor = HasSectors && angle % (360 / ActionCatalog.SectorCount) == 0;
                var context = isMajor ? majorContext : minorContext;
                context.BeginFigure(ToCanvas(SectorGeometry.PointAt(angle, outer)), false, false);
                context.LineTo(ToCanvas(SectorGeometry.PointAt(angle, outer - (isMajor ? TickMajor : TickMinor))), true, false);
            }
        }
        minor.Freeze();
        major.Freeze();
        yield return new Path { Data = minor, Stroke = Palette.Rim, StrokeThickness = 1, IsHitTestVisible = false };
        yield return new Path { Data = major, Stroke = Palette.TextMuted, StrokeThickness = 1.4, IsHitTestVisible = false };
    }

    /// <summary>Засечка-указатель на ободе острием внутрь: рисуется у сектора 0 и поворачивается.</summary>
    private Path Pointer(double rim)
    {
        // Острие не заходит под выдвинутый сектор.
        var tip = ToCanvas(SectorGeometry.PointAt(0, _geometry.OuterRadius + PopDistance + 1));
        var left = ToCanvas(SectorGeometry.PointAt(-2.6, rim - 1));
        var right = ToCanvas(SectorGeometry.PointAt(2.6, rim - 1));
        var figure = new PathFigure { StartPoint = tip, IsClosed = true };
        figure.Segments.Add(new LineSegment(left, false));
        figure.Segments.Add(new LineSegment(right, false));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        _pointerTurn.CenterX = _pointerTurn.CenterY = Center;
        return new Path
        {
            Data = geometry,
            Fill = _pointerFill,
            RenderTransform = _pointerTurn,
            Opacity = 0,
            IsHitTestVisible = false,
        };
    }

    /// <summary>Указатель переезжает коротким путём и встаёт с лёгкой отдачей.</summary>
    private void MovePointer(int? index)
    {
        if (_pointer is null)
            return;
        if (index is not { } i || ActionAt(i) is not { } action)
        {
            _pointerShown = false;
            Motion.To(_pointer, OpacityProperty, 0, Motion.Fast);
            return;
        }

        var target = _geometry.CenterAngle(i);
        Motion.ColorTo(_pointerFill, (action.IsEnabled ? Palette.Accent : Palette.TextMuted).Color, Motion.Fast);
        if (!_pointerShown)
        {
            // Из скрытого состояния — сразу на место, без пролёта через полкруга.
            _pointerAngle = target;
            _pointerTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            _pointerTurn.Angle = target;
            _pointerShown = true;
            Motion.To(_pointer, OpacityProperty, 1, Motion.Fast);
            return;
        }
        var delta = ((target - _pointerAngle) % 360 + 540) % 360 - 180;
        _pointerAngle += delta;
        Motion.To(_pointerTurn, RotateTransform.AngleProperty, _pointerAngle, 240, Motion.Detent);
    }

    private static TextBlock Label(string? number, string? text, double width, double fontSize, Brush foreground,
        Transform shift)
    {
        var label = new TextBlock
        {
            Width = width,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Palette.DialFont,
            FontSize = fontSize,
            IsHitTestVisible = false,
            Foreground = foreground,
            RenderTransform = shift, // подпись едет вместе со своим сектором
        };
        if (text is not null)
        {
            if (number is not null)
                label.Inlines.Add(new System.Windows.Documents.Run(number + "\n") { FontSize = 11 });
            label.Inlines.Add(new System.Windows.Documents.Run(text));
        }
        return label;
    }

    private void Place(FrameworkElement element, PointD point, double width)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        Canvas.SetLeft(element, Center + point.X - width / 2);
        Canvas.SetTop(element, Center + point.Y - element.DesiredSize.Height / 2);
    }

    private Path GroupMarker(int index)
    {
        var angle = _geometry.CenterAngle(index);
        var tip = ToCanvas(SectorGeometry.PointAt(angle, _geometry.OuterRadius - 5));
        var left = ToCanvas(SectorGeometry.PointAt(angle - 2.6, _geometry.OuterRadius - 12));
        var right = ToCanvas(SectorGeometry.PointAt(angle + 2.6, _geometry.OuterRadius - 12));
        var figure = new PathFigure { StartPoint = tip, IsClosed = true };
        figure.Segments.Add(new LineSegment(left, false));
        figure.Segments.Add(new LineSegment(right, false));
        return new Path
        {
            Data = new PathGeometry([figure]),
            Fill = _labelBrushes[index],
            RenderTransform = _shifts[index],
            IsHitTestVisible = false,
        };
    }

    private FrameworkElement BuildCenter()
    {
        var size = HasSectors ? _geometry.InnerRadius * 2 - 20 : CompactDiameter - 48;
        var title = new TextBlock
        {
            Text = _menu.Title,
            Foreground = Palette.Text,
            FontFamily = Palette.HeadingFont,
            FontSize = 13.5,
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

    // --- Внешнее кольцо ---

    private void Expand(int group)
    {
        if (_expanded == group)
            return;

        _expanded = group;
        _selectedChild = null;
        _subLayer.Children.Clear();
        _children.Clear();

        var children = ExpandedChildren;
        var parentAngle = _geometry.CenterAngle(group);
        _sub = new SubmenuGeometry(parentAngle, children.Count, ChildInner, ChildOuter, ChildSweep, ChildGap);

        // Подложка веера сливается с фоном основного круга — без «дыры» между ними.
        _subLayer.Children.Add(new Path
        {
            Data = RingSector(_sub.FanStart - 1, _sub.FanEnd + 1, _geometry.OuterRadius, ChildOuter + 4),
            Fill = Palette.Background,
            Stroke = Palette.Rim,
            StrokeThickness = 1,
        });

        for (var i = 0; i < children.Count; i++)
        {
            var fill = new SolidColorBrush(Palette.Sector.Color);
            var text = new SolidColorBrush((children[i].IsEnabled ? Palette.Text : Palette.TextMuted).Color);
            var shift = new TranslateTransform();
            var (start, end) = _sub.DrawnAngles(i);
            var sector = new Path { Data = RingSector(start, end, ChildInner, ChildOuter), Fill = fill, RenderTransform = shift };
            var label = Label($"{i + 1}", children[i].Label, ChildLabelWidth, 13, text, shift);
            Place(label, _sub.LabelPoint(i), ChildLabelWidth);

            // Каждый формат разворачивается веером из-под своей группы, один за другим.
            var turn = new RotateTransform(0, Center, Center);
            var item = new Canvas { RenderTransform = turn, Children = { sector, label } };
            _subLayer.Children.Add(item);
            Motion.Animate(turn, RotateTransform.AngleProperty, parentAngle - _sub.CenterAngle(i), 0, 220, delay: i * 26);
            Motion.Animate(item, OpacityProperty, 0, 1, 150, delay: i * 26);
            _children.Add((sector, fill, text, shift));
        }

        // Подложка веера проявляется вместе с первым сектором.
        _subLayer.Visibility = Visibility.Visible;
        if (Motion.Enabled)
        {
            _subLayer.Opacity = 0;
            _subScale.ScaleX = _subScale.ScaleY = 0.96;
        }
        Motion.To(_subLayer, OpacityProperty, 1, Motion.Fast);
        Motion.To(_subScale, ScaleTransform.ScaleXProperty, 1, Motion.Normal);
        Motion.To(_subScale, ScaleTransform.ScaleYProperty, 1, Motion.Normal);
    }

    private void Collapse()
    {
        if (_expanded is null)
            return;
        _expanded = null;
        _selectedChild = null;
        _sub = null;
        _children.Clear();
        Motion.To(_subLayer, OpacityProperty, 0, Motion.Fast, () =>
        {
            // За время исчезновения могла раскрыться другая группа — тогда слой уже новый.
            if (_expanded is null)
            {
                _subLayer.Children.Clear();
                _subLayer.Visibility = Visibility.Collapsed;
            }
        });
    }

    // --- Состояние и отрисовка ---

    private void UpdateVisuals()
    {
        for (var i = 0; i < _sectors.Length; i++)
        {
            if (_sectors[i] is null)
                continue;
            var action = ActionAt(i);
            var enabled = action?.IsEnabled == true;
            var selected = _selected == i && action is not null;
            // Пока выбран формат во внешнем кольце, группа подсвечена приглушённо — акцент у формата.
            var groupBehindChild = selected && _selectedChild is not null;

            var fill = !selected ? Palette.Sector
                : groupBehindChild || !enabled ? Palette.SectorDisabledHover
                : Palette.Accent;
            var text = selected && enabled && !groupBehindChild ? Palette.AccentText : enabled ? Palette.Text : Palette.TextMuted;
            Motion.ColorTo(_fills[i], fill.Color, Motion.Fast);
            Motion.ColorTo(_labelBrushes[i], text.Color, Motion.Fast);

            // Активный сектор слегка выдвигается наружу по своей оси.
            var push = selected && enabled ? SectorGeometry.PointAt(_geometry.CenterAngle(i), PopDistance) : default;
            Motion.To(_shifts[i], TranslateTransform.XProperty, push.X, 120);
            Motion.To(_shifts[i], TranslateTransform.YProperty, push.Y, 120);

            // Метка группы нужна, пока веер закрыт: при выборе на её место встаёт указатель.
            if (_markers[i] is { } marker)
                Motion.To(marker, OpacityProperty, selected ? 0 : 1, Motion.Fast);
        }

        for (var i = 0; i < _children.Count; i++)
        {
            var child = ChildAt(i)!;
            var selected = _selectedChild == i;
            var fill = !selected ? Palette.Sector : child.IsEnabled ? Palette.Accent : Palette.SectorDisabledHover;
            var text = selected && child.IsEnabled ? Palette.AccentText : child.IsEnabled ? Palette.Text : Palette.TextMuted;
            Motion.ColorTo(_children[i].Fill, fill.Color, Motion.Fast);
            Motion.ColorTo(_children[i].Text, text.Color, Motion.Fast);

            var push = selected && child.IsEnabled ? SectorGeometry.PointAt(_sub!.CenterAngle(i), PopDistance - 1) : default;
            Motion.To(_children[i].Shift, TranslateTransform.XProperty, push.X, 120);
            Motion.To(_children[i].Shift, TranslateTransform.YProperty, push.Y, 120);
        }

        MovePointer(_selected);
        _hint.Text = HintText();
        // В компактном круге единственное действие выделяем цветом — это приглашение нажать.
        _hint.Foreground = _menu.QuickAction is not null && !HasSectors ? Palette.Accent : Palette.TextMuted;
    }

    private string HintText()
    {
        if (_selectedChild is { } c && ChildAt(c) is { } child)
            return child.IsEnabled ? $"Enter — {LowerFirst(child.Title)}" : child.DisabledReason ?? ActionCatalog.NotInPrototype;

        var current = _selected is { } s ? ActionAt(s) : null;
        return current switch
        {
            null => DefaultHint(),
            { IsGroup: true } => "Выберите формат снаружи или Enter",
            { IsEnabled: false, FixActionId: not null } => $"{current.DisabledReason} — Enter: скачать",
            { IsEnabled: false } => current.DisabledReason ?? ActionCatalog.NotInPrototype,
            _ => "Enter — запустить",
        };
    }

    /// <summary>«В WebP» → «в WebP»: регистр самого формата сохраняем.</summary>
    private static string LowerFirst(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    /// <summary>Подсказка без выбранного сектора. Если запускать нечего, сразу объясняем почему.</summary>
    private string DefaultHint()
    {
        if (_menu.Kind == DialMenuKind.Unsupported)
            return _menu.QuickAction is { } quick ? $"Enter или клик — {LowerFirst(quick.Title)}" : "Esc — закрыть";
        if (_menu.Actions.Any(a => a.IsEnabled))
            return "1–8, Enter · Esc";
        return _menu.Actions.FirstOrDefault(a => a.DisabledReason is not null and not ActionCatalog.NotInPrototype)
                   ?.DisabledReason
               ?? "Нет доступных действий";
    }

    /// <summary>Выбор сектора основного круга. Группа раскрывает веер, любой другой сектор его сворачивает.</summary>
    private void SelectMain(int? index)
    {
        if (_selected == index && _selectedChild is null)
            return;
        _selected = index;
        _selectedChild = null;
        if (index is { } i && ActionAt(i)?.IsGroup == true)
            Expand(i);
        else
            Collapse();
        UpdateVisuals();
    }

    private void SelectChild(int? index)
    {
        if (_selectedChild == index)
            return;
        _selectedChild = index;
        UpdateVisuals();
    }

    private void Run(DialAction? action)
    {
        // Неактивный сектор, причину которого можно устранить (нет модели речи), ведёт к исправлению.
        if (!_closing && action is { IsEnabled: false, FixActionId: { } fix })
        {
            Chosen = new DialAction(fix, action.Label, true);
            SafeClose();
            return;
        }
        if (_closing || action is not { IsEnabled: true } || action.IsGroup)
            return; // неактивный сектор или группа: ничего не запускаем, круг остаётся открытым
        Chosen = action;
        Press(action);
        SafeClose();
    }

    /// <summary>Нажатие: выбранный сектор уходит дальше наружу, пока круг гаснет.</summary>
    private void Press(DialAction action)
    {
        _pressed = true;
        TranslateTransform? shift = null;
        var angle = 0.0;
        if (_selectedChild is { } c && _sub is not null && ChildAt(c) == action)
            (shift, angle) = (_children[c].Shift, _sub.CenterAngle(c));
        else if (_selected is { } s && ActionAt(s) == action)
            (shift, angle) = (_shifts[s], _geometry.CenterAngle(s));
        if (shift is null)
            return;

        var push = SectorGeometry.PointAt(angle, PopDistance * 2.6);
        Motion.To(shift, TranslateTransform.XProperty, push.X, Motion.Fast);
        Motion.To(shift, TranslateTransform.YProperty, push.Y, Motion.Fast);
    }

    // --- Ввод ---

    private (int? Main, int? Child, double Distance) Hit(Point p)
    {
        if (!HasSectors)
            return (null, null, 0);
        var dx = p.X - Center;
        var dy = p.Y - Center;
        var child = _sub?.HitTest(dx, dy, bridgeRadius: _geometry.OuterRadius);
        return (_geometry.HitTest(dx, dy), child, Math.Sqrt(dx * dx + dy * dy));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (main, child, distance) = Hit(e.GetPosition(this));
        if (child is not null)
            SelectChild(child);
        else if (main is not null)
            SelectMain(main);
        else if (distance < _geometry.InnerRadius)
            SelectMain(null); // вернулись в центр — веер сворачивается
        // Иначе мышь в зазоре или на подложке веера — состояние не меняем, чтобы веер не мигал.
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var (main, child, _) = Hit(e.GetPosition(this));
        if (!HasSectors && _menu.QuickAction is { } quick)
            Run(quick); // компактный круг — одна большая кнопка
        else if (child is { } c)
            Run(ChildAt(c));
        else if (main is { } m)
            Run(ActionAt(m));
        else
            SafeClose(); // клик в центре или мимо секторов
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
        var inSubmenu = _selectedChild is not null;

        switch (key)
        {
            case Key.Escape when inSubmenu:
            case Key.Back when inSubmenu:
                SelectChild(null); // назад к основному кругу, веер остаётся открытым
                return;
            case Key.Escape:
                SafeClose();
                return;
            case Key.Enter or Key.Space when !HasSectors:
                if (_menu.QuickAction is { } quick)
                    Run(quick);
                return;
            case Key.Enter or Key.Space:
                if (_selectedChild is { } c)
                    Run(ChildAt(c));
                else if (_selected is { } s && ActionAt(s) is { IsGroup: true } group)
                    SelectChild(IndexOfFirstEnabled(group.Children!)); // вход во внешнее кольцо
                else if (_selected is { } s2)
                    Run(ActionAt(s2));
                return;
            case Key.Right or Key.Left or Key.Up or Key.Down:
                Step(key is Key.Right or Key.Down ? 1 : -1);
                return;
        }

        int? digit = key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => null,
        };
        if (digit is not { } d || !HasSectors)
        {
            e.Handled = false;
            return;
        }

        // Во внешнем кольце цифры выбирают формат, иначе — сектор основного круга.
        if (inSubmenu)
        {
            if (d <= ExpandedChildren.Count)
                SelectChild(d - 1);
        }
        else if (_geometry.IndexForDigit(d) is { } index)
        {
            SelectMain(index);
        }
    }

    /// <summary>Стрелки: по кругу внутри внешнего кольца или основного круга.</summary>
    private void Step(int delta)
    {
        if (!HasSectors)
            return;
        if (_selectedChild is { } c)
        {
            var count = ExpandedChildren.Count;
            SelectChild(((c + delta) % count + count) % count);
            return;
        }
        var current = _selected ?? (delta > 0 ? -1 : 0);
        SelectMain(((current + delta) % ActionCatalog.SectorCount + ActionCatalog.SectorCount) % ActionCatalog.SectorCount);
    }

    private static int IndexOfFirstEnabled(IReadOnlyList<DialAction> actions)
    {
        for (var i = 0; i < actions.Count; i++)
            if (actions[i].IsEnabled)
                return i;
        return 0;
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

    /// <summary>Закрыть без действия (повторное нажатие горячей клавиши).</summary>
    public void Dismiss() => SafeClose();

    private void CloseIfOpen()
    {
        if (!_isClosed)
            Close();
    }

    private void SafeClose()
    {
        if (_closing)
            return;
        _closing = true;
        IsHitTestVisible = false;
        // Close — асинхронно, так надёжнее внутри Deactivated и обработчиков ввода.
        if (!Motion.Enabled || !IsLoaded)
        {
            Dispatcher.BeginInvoke(CloseIfOpen);
            return;
        }
        // Ручка чуть проворачивается дальше и гаснет; после выбора — чуть дольше, чтобы нажатие было видно.
        var duration = _pressed ? 170 : 110;
        Motion.To(_spin, RotateTransform.AngleProperty, 8, duration);
        Motion.To(_canvas, OpacityProperty, 0, duration);
        Motion.To(_scale, ScaleTransform.ScaleXProperty, 0.94, duration);
        Motion.To(_scale, ScaleTransform.ScaleYProperty, 0.94, duration, () => Dispatcher.BeginInvoke(CloseIfOpen));
    }
}
