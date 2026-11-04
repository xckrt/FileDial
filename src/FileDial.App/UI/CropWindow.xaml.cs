using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;
using Path = System.IO.Path;

namespace FileDial.App.UI;

/// <summary>
/// «Кадрировать…». Для одной картинки — превью (его рисует ffmpeg: WPF не читает HEIC и AVIF) с рамкой: рамку
/// двигают мышью и стрелками, тянут за углы, пропорции соблюдаются. Для нескольких картинок превью нет: выбираются
/// пропорции и какую часть оставить, и каждая картинка кадрируется сама.
/// </summary>
public partial class CropWindow : Window
{
    private const double MaxPreviewWidth = 538;
    private const double MaxPreviewHeight = 380;
    /// <summary>Длинная сторона превью в пикселях: с запасом на масштаб экрана 200%.</summary>
    private const int PreviewLongSide = 1280;
    private const double HandleSize = 10;
    private const double HandleHit = 10;
    /// <summary>Меньше рамку не сжать — иначе её не ухватить.</summary>
    private const double MinFrame = 24;

    private enum Drag { None, Move, TopLeft, TopRight, BottomLeft, BottomRight }

    private readonly string? _source;
    private readonly FfmpegTools? _tools;
    private readonly ProbeInfo? _info;
    private readonly (int Width, int Height)? _size;
    /// <summary>Точек экрана на пиксель картинки.</summary>
    private readonly double _scale;
    private readonly CancellationTokenSource _closing = new();

    // Рамка в пикселях картинки (после поворота — как её видит пользователь).
    private double _x, _y, _w, _h;
    private CropAspect? _preset;
    private bool _original;
    private bool _swapped;
    private CropAnchor _anchor;
    private CropRequest? _request;
    private bool _updating;

    private Drag _drag;
    private Point _dragStart;
    private (double X, double Y) _dragOrigin;

    private readonly System.Windows.Shapes.Path _mask = new() { Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle _frame = new() { StrokeThickness = 1.5, IsHitTestVisible = false };
    private readonly Line[] _thirds = new Line[4];
    private readonly Rectangle[] _handles = new Rectangle[4];

    private CropWindow(string title, string? source, FfmpegTools? tools, ProbeInfo? info, CropChoice initial)
    {
        InitializeComponent();
        _source = source;
        _tools = tools;
        _info = info;
        if (source is not null && info is { Width: > 0, Height: > 0 })
            _size = (info.Width, info.Height);
        FileText.Text = title;
        SizeText.Text = _size is { } s ? Lang.T.ImgCurrentSize(s.Width, s.Height) : Lang.T.ImgSizeUnknown;

        if (_size is { } size)
        {
            _scale = Math.Min(MaxPreviewWidth / size.Width, MaxPreviewHeight / size.Height);
            PreviewHost.Width = Math.Max(1, Math.Round(size.Width * _scale));
            PreviewHost.Height = Math.Max(1, Math.Round(size.Height * _scale));
            BuildOverlay();
            (_x, _y, _w, _h) = (0, 0, size.Width, size.Height);
            AnchorPanel.Visibility = Visibility.Collapsed;
            NoteText.Text = Lang.T.ImgCropKeys;
        }
        else
        {
            PreviewHost.Visibility = Visibility.Collapsed;
            NoteText.Text = Lang.T.ImgCropBatchNote;
            foreach (var anchor in Enum.GetValues<CropAnchor>())
                Anchors.Children.Add(Segment("Anchor", Lang.T.ImgAnchor(anchor), anchor, () => SelectAnchor(anchor)));
        }

        BuildAspects();
        Restore(initial);
        Loaded += async (_, _) =>
        {
            (Aspects.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true) as UIElement ?? OkButton).Focus();
            await LoadPreviewAsync();
        };
        Closed += (_, _) => _closing.Cancel();
    }

    private bool IsSingle => _size is not null;

    /// <summary>Пропорции рамки с учётом «как у снимка» и поворота рамки.</summary>
    private CropAspect Aspect
    {
        get
        {
            var aspect = _original && _size is { } s ? CropAspect.Of(s.Width, s.Height) : _preset ?? CropAspect.Free;
            return _swapped ? aspect.Swapped : aspect;
        }
    }

    // --- Выбор пропорций и края ---

    private RadioButton Segment(string group, string label, object tag, Action selected)
    {
        var button = new RadioButton
        {
            Content = label,
            Tag = tag,
            GroupName = group,
            Style = (Style)Application.Current.FindResource("SegmentRadio"),
            Margin = new Thickness(0, 0, 6, 6),
        };
        button.Checked += (_, _) =>
        {
            if (!_updating)
                selected();
        };
        return button;
    }

    private void BuildAspects()
    {
        // Свободная рамка и «как у снимка» имеют смысл, только когда рамку видно; для пакета — готовые пропорции.
        if (IsSingle)
        {
            Aspects.Children.Add(Segment("Aspect", Lang.T.ImgAspectFree, "free", () => SelectAspect(null, false)));
            Aspects.Children.Add(Segment("Aspect", Lang.T.ImgAspectOriginal, "original", () => SelectAspect(null, true)));
        }
        foreach (var preset in CropAspect.Presets)
            Aspects.Children.Add(Segment("Aspect", preset.ToString(), preset, () => SelectAspect(preset, false)));
    }

    private void Restore(CropChoice choice)
    {
        _anchor = choice.Anchor;
        if (choice.Original && IsSingle)
            _original = true;
        else if (!choice.Aspect.IsFree)
        {
            var preset = CropAspect.Presets.FirstOrDefault(p => p == choice.Aspect);
            if (preset.IsFree)
            {
                preset = CropAspect.Presets.FirstOrDefault(p => p.Swapped == choice.Aspect);
                _swapped = !preset.IsFree;
            }
            _preset = preset.IsFree ? null : preset;
        }
        // Для пакета свободной рамки нет — начинаем с квадрата.
        if (!IsSingle && _preset is null)
            _preset = CropAspect.Presets[0];

        _updating = true;
        foreach (var button in Aspects.Children.OfType<RadioButton>())
            button.IsChecked = button.Tag switch
            {
                "free" => _preset is null && !_original,
                "original" => _original,
                CropAspect a => a == _preset,
                _ => false,
            };
        foreach (var button in Anchors.Children.OfType<RadioButton>())
            button.IsChecked = Equals(button.Tag, _anchor);
        _updating = false;
        Relabel();
        ResetFrame();
        Update();
    }

    private void SelectAspect(CropAspect? preset, bool original)
    {
        _preset = preset;
        _original = original;
        ResetFrame();
        Update();
    }

    private void SelectAnchor(CropAnchor anchor)
    {
        _anchor = anchor;
        Update();
    }

    /// <summary>↔: та же пропорция в другой ориентации; подписи готовых пропорций переворачиваются вместе с ней.</summary>
    private void OnSwap(object sender, RoutedEventArgs e)
    {
        _swapped = !_swapped;
        Relabel();
        ResetFrame();
        Update();
    }

    private void Relabel()
    {
        foreach (var button in Aspects.Children.OfType<RadioButton>())
            if (button.Tag is CropAspect a)
                button.Content = (_swapped ? a.Swapped : a).ToString();
    }

    /// <summary>Новая пропорция — наибольшая рамка с центром там же, где был (сначала — по центру картинки).</summary>
    private void ResetFrame()
    {
        if (_size is not { } s || Aspect.IsFree)
            return;
        var (w, h) = CropRequest.Largest(s.Width, s.Height, Aspect);
        var cx = _x + _w / 2;
        var cy = _y + _h / 2;
        (_w, _h) = (w, h);
        _x = Math.Clamp(cx - w / 2.0, 0, s.Width - w);
        _y = Math.Clamp(cy - h / 2.0, 0, s.Height - h);
    }

    // --- Итог ---

    private void Update()
    {
        SwapButton.IsEnabled = !Aspect.IsFree && Aspect.Width != Aspect.Height;
        if (_size is { } s)
        {
            _request = new CropRequest(Aspect, CropAnchor.Center,
                new CropRegion(_x / s.Width, _y / s.Height, _w / s.Width, _h / s.Height));
            var r = _request.Rectangle(s.Width, s.Height);
            var crops = _request.Crops(s.Width, s.Height);
            ResultText.Text = crops ? Lang.T.ImgCropWillBe(r.Width, r.Height) : Lang.T.ImgCropWhole;
            OkButton.IsEnabled = crops;
            Draw();
            return;
        }

        _request = new CropRequest(Aspect, _anchor);
        ResultText.Text = Lang.T.ImgCropEach(Aspect.ToString(), _anchor);
        OkButton.IsEnabled = !Aspect.IsFree;
    }

    private CropChoice Choice => new(_original ? CropAspect.Free : Aspect, _original, _anchor);

    // --- Рамка на превью ---

    private void BuildOverlay()
    {
        _frame.Stroke = Palette.Accent;
        Overlay.Children.Add(_mask);
        for (var i = 0; i < _thirds.Length; i++)
        {
            _thirds[i] = new Line { Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), StrokeThickness = 1, IsHitTestVisible = false };
            Overlay.Children.Add(_thirds[i]);
        }
        Overlay.Children.Add(_frame);
        for (var i = 0; i < _handles.Length; i++)
        {
            _handles[i] = new Rectangle { Width = HandleSize, Height = HandleSize, Fill = Palette.Accent, IsHitTestVisible = false };
            Overlay.Children.Add(_handles[i]);
        }
    }

    private Rect FrameOnScreen => new(_x * _scale, _y * _scale, _w * _scale, _h * _scale);

    private void Draw()
    {
        var frame = FrameOnScreen;
        var all = new Rect(0, 0, PreviewHost.Width, PreviewHost.Height);
        _mask.Data = new GeometryGroup
        {
            FillRule = FillRule.EvenOdd,
            Children = { new RectangleGeometry(all), new RectangleGeometry(frame) },
        };
        Canvas.SetLeft(_frame, frame.X);
        Canvas.SetTop(_frame, frame.Y);
        _frame.Width = frame.Width;
        _frame.Height = frame.Height;

        // Линии третей — подсказка для композиции.
        for (var i = 0; i < 2; i++)
        {
            var x = frame.X + frame.Width * (i + 1) / 3;
            var y = frame.Y + frame.Height * (i + 1) / 3;
            (_thirds[i].X1, _thirds[i].Y1, _thirds[i].X2, _thirds[i].Y2) = (x, frame.Top, x, frame.Bottom);
            (_thirds[i + 2].X1, _thirds[i + 2].Y1, _thirds[i + 2].X2, _thirds[i + 2].Y2) = (frame.Left, y, frame.Right, y);
        }

        // Ручки — внутри углов рамки: рамка во всю картинку упирается в край превью, и снаружи их бы обрезало.
        var right = frame.Right - HandleSize;
        var bottom = frame.Bottom - HandleSize;
        Point[] corners = [frame.TopLeft, new(right, frame.Top), new(frame.Left, bottom), new(right, bottom)];
        for (var i = 0; i < corners.Length; i++)
        {
            Canvas.SetLeft(_handles[i], corners[i].X);
            Canvas.SetTop(_handles[i], corners[i].Y);
        }
    }

    private Drag HitTest(Point p)
    {
        var frame = FrameOnScreen;
        // Ухватить угол можно и чуть снаружи, и по всей ручке внутри.
        bool Near(Point corner) => Math.Abs(p.X - corner.X) <= HandleHit && Math.Abs(p.Y - corner.Y) <= HandleHit;
        if (Near(frame.TopLeft)) return Drag.TopLeft;
        if (Near(frame.TopRight)) return Drag.TopRight;
        if (Near(frame.BottomLeft)) return Drag.BottomLeft;
        if (Near(frame.BottomRight)) return Drag.BottomRight;
        return frame.Contains(p) ? Drag.Move : Drag.None;
    }

    private static Cursor CursorFor(Drag drag) => drag switch
    {
        Drag.Move => Cursors.SizeAll,
        Drag.TopLeft or Drag.BottomRight => Cursors.SizeNWSE,
        Drag.TopRight or Drag.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Arrow,
    };

    private void OnOverlayDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // по превью окно не перетаскивается
        if (!IsSingle)
            return;
        var p = e.GetPosition(Overlay);
        _drag = HitTest(p);
        if (_drag == Drag.None)
            return;
        _dragStart = p;
        _dragOrigin = (_x, _y);
        Overlay.CaptureMouse();
    }

    private void OnOverlayMove(object sender, MouseEventArgs e)
    {
        if (_size is not { } s)
            return;
        var p = e.GetPosition(Overlay);
        if (_drag == Drag.None)
        {
            Overlay.Cursor = CursorFor(HitTest(p));
            return;
        }

        if (_drag == Drag.Move)
        {
            _x = Math.Clamp(_dragOrigin.X + (p.X - _dragStart.X) / _scale, 0, s.Width - _w);
            _y = Math.Clamp(_dragOrigin.Y + (p.Y - _dragStart.Y) / _scale, 0, s.Height - _h);
        }
        else
        {
            // Противоположный угол стоит на месте, тянется ближний к указателю.
            var signX = _drag is Drag.TopRight or Drag.BottomRight ? 1 : -1;
            var signY = _drag is Drag.BottomLeft or Drag.BottomRight ? 1 : -1;
            var fixedX = signX > 0 ? _x : _x + _w;
            var fixedY = signY > 0 ? _y : _y + _h;
            Resize(s, fixedX, fixedY, signX, signY, Math.Clamp(p.X / _scale, 0, s.Width), Math.Clamp(p.Y / _scale, 0, s.Height));
        }
        Update();
    }

    private void Resize((int Width, int Height) s, double fixedX, double fixedY, int signX, int signY, double px, double py)
    {
        var min = Math.Max(1, MinFrame / _scale);
        var maxW = signX > 0 ? s.Width - fixedX : fixedX;
        var maxH = signY > 0 ? s.Height - fixedY : fixedY;
        // Указатель зашёл за неподвижный угол — рамка не выворачивается, а сжимается до наименьшей.
        var w = (px - fixedX) * signX > 0 ? Math.Max(Math.Abs(px - fixedX), min) : min;
        var h = (py - fixedY) * signY > 0 ? Math.Max(Math.Abs(py - fixedY), min) : min;
        var aspect = Aspect;
        if (aspect.IsFree)
        {
            w = Math.Min(w, maxW);
            h = Math.Min(h, maxH);
        }
        else
        {
            var ratio = (double)aspect.Width / aspect.Height;
            w = Math.Min(Math.Max(w, h * ratio), Math.Min(maxW, maxH * ratio));
            h = w / ratio;
        }
        _w = w;
        _h = h;
        _x = signX > 0 ? fixedX : fixedX - w;
        _y = signY > 0 ? fixedY : fixedY - h;
    }

    private void OnOverlayUp(object sender, MouseButtonEventArgs e) => Overlay.ReleaseMouseCapture();

    private void OnOverlayLostCapture(object sender, MouseEventArgs e) => _drag = Drag.None;

    /// <summary>Стрелки двигают рамку на точку экрана, с Shift — на десять.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_size is not { } s || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;
        var step = (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1) / _scale;
        switch (e.Key)
        {
            case Key.Left: _x -= step; break;
            case Key.Right: _x += step; break;
            case Key.Up: _y -= step; break;
            case Key.Down: _y += step; break;
        }
        _x = Math.Clamp(_x, 0, s.Width - _w);
        _y = Math.Clamp(_y, 0, s.Height - _h);
        Update();
        e.Handled = true;
    }

    // --- Превью ---

    private async Task LoadPreviewAsync()
    {
        if (!IsSingle || _tools is null || _source is null || _info is null)
        {
            if (IsSingle)
                PreviewStatus.Text = Lang.T.ImgPreviewFailed;
            return;
        }

        PreviewStatus.Text = Lang.T.ImgPreviewLoading;
        var folder = Path.Combine(Path.GetTempPath(), "FileDial");
        var temp = Path.Combine(folder, $"preview-{Guid.NewGuid():N}.png");
        try
        {
            Directory.CreateDirectory(folder);
            var ct = _closing.Token;
            await Task.Run(() => ImagePreview.RenderAsync(_tools, _source, _info, PreviewLongSide, temp, ct), ct);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // файл читается сразу — его можно удалить
            bitmap.UriSource = new Uri(temp);
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
            PreviewStatus.Text = "";
            Placeholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            // окно закрыли раньше, чем превью было готово
        }
        catch (Exception ex) when (ex is CompressionException or IOException or UnauthorizedAccessException
                                       or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write("Превью для кадрирования", ex);
            PreviewStatus.Text = Lang.T.ImgPreviewFailed;
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // не страшно: это папка временных файлов
            }
        }
    }

    // --- Окно ---

    /// <summary>Спросить кадр; null — отказались. Choice — что запомнить в настройках.</summary>
    /// <param name="source">Картинка, если она одна: для неё окно покажет превью и рамку.</param>
    /// <param name="info">Сведения ffprobe о ней; без размера окно работает как для нескольких картинок.</param>
    public static (CropRequest Request, CropChoice Choice)? Ask(string title, string? source, FfmpegTools? tools,
        ProbeInfo? info, CropChoice initial)
    {
        var dialog = new CropWindow(title, source, tools, info, initial);
        return dialog.ShowDialog() == true && dialog._request is { } request ? (request, dialog.Choice) : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = _request is not null;

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
