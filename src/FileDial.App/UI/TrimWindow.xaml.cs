using System.Windows;
using System.Windows.Input;
using FileDial.App.Interop;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Menu;

namespace FileDial.App.UI;

/// <summary>Ввод границ отрезка. Длительность берётся из ffprobe, ввод проверяется на лету.</summary>
public partial class TrimWindow : Window
{
    private readonly string _sourcePath;
    private readonly bool _isVideo;
    private double _duration;

    internal TrimWindow(string sourcePath, NativeMethods.POINT cursor)
    {
        InitializeComponent();
        _sourcePath = sourcePath;
        _isVideo = FileKinds.Of(sourcePath) == FileKind.Video;
        FileNameText.Text = Path.GetFileName(sourcePath);
        FileNameText.ToolTip = sourcePath;
        PreciseBox.Visibility = _isVideo ? Visibility.Visible : Visibility.Collapsed;
        Opacity = 0;

        var placement = new WindowPlacement(this, cursor, PlacementMode.NearCursor, Width, Height);
        SourceInitialized += (_, _) => placement.Apply();
        Loaded += async (_, _) =>
        {
            placement.Apply();
            Motion.To(this, OpacityProperty, 1, Motion.Normal);
            await LoadDurationAsync();
        };
    }

    /// <summary>Пользователь подтвердил отрезок.</summary>
    public event Action<TrimRequest>? Confirmed;

    private async Task LoadDurationAsync()
    {
        var tools = FfmpegLocator.LocateDefault();
        if (tools is null)
        {
            DurationText.Text = "ffmpeg не найден";
            StatusText.Text = "Не найдены ffmpeg.exe и ffprobe.exe. Инструкция — в README, раздел «Установка ffmpeg».";
            return;
        }

        try
        {
            var info = await Task.Run(() => ProbeInfo.RunAsync(tools, _sourcePath, CancellationToken.None));
            _duration = info.DurationSeconds;
        }
        catch (Exception ex) when (ex is CompressionException or IOException or System.ComponentModel.Win32Exception)
        {
            DurationText.Text = "Длительность неизвестна";
            StatusText.Text = ex.Message;
            return;
        }

        if (_duration <= 0)
        {
            DurationText.Text = "Длительность неизвестна";
            StatusText.Text = "ffprobe не сообщил длительность — обрезать нельзя.";
            return;
        }

        DurationText.Text = $"Длительность: {TimeInput.Format(TimeSpan.FromSeconds(_duration))}";
        StartBox.Text = TimeInput.Format(TimeSpan.Zero);
        EndBox.Text = TimeInput.Format(TimeSpan.FromSeconds(_duration));
        StartBox.IsEnabled = EndBox.IsEnabled = true;
        StartBox.Focus();
        StartBox.SelectAll();
        Validate();
    }

    private TrimRequest? Validate()
    {
        if (_duration <= 0)
            return null;

        string? error;
        TrimRequest? request = null;
        if (!TimeInput.TryParse(StartBox.Text, out var start))
            error = "Начало: введите время, например 0:15 или 1:02:30.";
        else if (!TimeInput.TryParse(EndBox.Text, out var end))
            error = "Конец: введите время, например 0:45 или 1:05:00.";
        else if (start == TimeSpan.Zero && end.TotalSeconds >= _duration - 0.05)
        {
            // Начальное состояние (весь файл) — не ошибка, а приглашение указать отрезок.
            StatusText.Text = "Укажите, какой отрезок оставить.";
            StatusText.Foreground = Palette.TextMuted;
            TrimButton.IsEnabled = false;
            return null;
        }
        else
        {
            error = TrimRequest.Validate(start, end, _duration);
            if (error is null)
                request = new TrimRequest(start, end, _isVideo && PreciseBox.IsChecked == true);
        }

        StatusText.Text = error ?? $"Длина отрезка: {TimeInput.Format(request!.Length)}";
        StatusText.Foreground = error is null ? Palette.TextMuted : Palette.Text;
        TrimButton.IsEnabled = request is not null;
        return request;
    }

    private void OnInputChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Validate();

    private void OnPreciseChanged(object sender, RoutedEventArgs e) => Validate();

    private void OnTrim(object sender, RoutedEventArgs e)
    {
        if (Validate() is not { } request)
            return;
        Close();
        Confirmed?.Invoke(request);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.TextBox)
            DragMove();
    }
}
