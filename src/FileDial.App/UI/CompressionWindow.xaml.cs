using System.Windows;
using System.Windows.Input;
using FileDial.App.Interop;
using FileDial.Core.Compression;
using FileDial.Core.Text;

namespace FileDial.App.UI;

/// <summary>Окно прогресса сжатия. Вся работа — асинхронно, UI не блокируется.</summary>
public partial class CompressionWindow : Window
{
    private readonly string _sourcePath;
    private readonly CancellationTokenSource _cts = new();
    private string? _resultPath;
    private bool _running;

    internal CompressionWindow(string sourcePath, NativeMethods.POINT cursor)
    {
        InitializeComponent();
        _sourcePath = sourcePath;
        FileNameText.Text = Path.GetFileName(sourcePath);
        FileNameText.ToolTip = sourcePath;
        StageText.Text = "Подготовка";

        var placement = new WindowPlacement(this, cursor, PlacementMode.NearCursor, Width, Height);
        SourceInitialized += (_, _) => placement.Apply();
        Loaded += async (_, _) =>
        {
            placement.Apply();
            await RunAsync();
        };
        Closing += (_, e) =>
        {
            // Закрытие во время работы = отмена; окно закроется, когда ffmpeg остановится и временные файлы удалятся.
            if (_running)
            {
                e.Cancel = true;
                RequestCancel();
            }
        };
        Closed += (_, _) => _cts.Dispose();
    }

    public string SourcePath => _sourcePath;

    /// <summary>Работа ещё идёт (или вот-вот начнётся) — итог не показан.</summary>
    public bool IsBusy { get; private set; } = true;

    /// <summary>Закрыть окно после отмены (например, при выходе из программы).</summary>
    private bool _closeWhenStopped;

    private async Task RunAsync()
    {
        var tools = FfmpegLocator.LocateDefault();
        if (tools is null)
        {
            ShowFinished(
                "Не найдены ffmpeg.exe и ffprobe.exe. Положите их рядом с FileDial.exe или добавьте папку с ними в PATH.",
                showHelp: true);
            return;
        }

        _running = true;
        var progress = new Progress<CompressionProgress>(p =>
        {
            StageText.Text = p.Stage;
            ProgressIndicator.Value = p.Fraction;
            PercentText.Text = $"{p.Fraction * 100:0}%";
        });

        try
        {
            var compressor = new VideoCompressor(tools);
            // Отдельный поток пула: чтение файла и ожидание процессов не трогают UI-поток.
            var result = await Task.Run(() => compressor.CompressAsync(_sourcePath, progress, _cts.Token));
            _running = false;

            if (result.Status == CompressionStatus.AlreadySmall)
            {
                ShowFinished($"Файл уже меньше 25 МБ ({SizeFormatter.Format(result.SourceSize)}), сжимать не нужно.");
                return;
            }

            _resultPath = result.OutputPath;
            ProgressIndicator.Value = 1;
            PercentText.Text = "100%";
            StageText.Text = "Готово";
            var over = result.OutputSize > BitrateCalculator.DefaultTargetBytes
                ? " Чуть больше цели: кодер не попал точно."
                : string.Empty;
            ShowFinished(
                $"{SizeFormatter.Format(result.SourceSize)} → {SizeFormatter.Format(result.OutputSize)}. " +
                $"Сохранено как {Path.GetFileName(result.OutputPath)}.{over}");
            RevealButton.Visibility = Visibility.Visible;
            RevealButton.Focus();
        }
        catch (OperationCanceledException)
        {
            _running = false;
            if (_closeWhenStopped)
            {
                Close();
                return;
            }
            StageText.Text = "Отменено";
            ShowFinished("Сжатие отменено. Исходный файл не изменён, временные файлы удалены.");
        }
        catch (CompressionException ex)
        {
            _running = false;
            StageText.Text = "Ошибка";
            ShowFinished(ex.Details is { Length: > 0 } d ? $"{ex.Message}\n{LastLine(d)}" : ex.Message);
            if (ex.Details is not null)
                ErrorLog.Write("ffmpeg", ex);
        }
        catch (Exception ex)
        {
            _running = false;
            ErrorLog.Write("Сжатие", ex);
            StageText.Text = "Ошибка";
            ShowFinished($"Не удалось сжать файл: {ex.Message}");
        }
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? text;

    private void ShowFinished(string message, bool showHelp = false)
    {
        IsBusy = false;
        MessageText.Text = message;
        MessageText.ToolTip = message;
        MessageText.Visibility = Visibility.Visible;
        HelpLinkText.Visibility = showHelp ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.IsCancel = true;
        if (showHelp)
        {
            StageText.Text = "ffmpeg не найден";
            PercentText.Text = string.Empty;
        }
        CloseButton.Focus();
    }

    private void RequestCancel()
    {
        if (!_running || _cts.IsCancellationRequested)
            return;
        StageText.Text = "Отмена…";
        CancelButton.IsEnabled = false;
        _cts.Cancel();
    }

    /// <summary>Отменить работу и закрыть окно, когда всё остановится.</summary>
    public void CancelAndClose()
    {
        if (_running)
        {
            _closeWhenStopped = true;
            RequestCancel();
        }
        else
        {
            Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => RequestCancel();

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnReveal(object sender, RoutedEventArgs e)
    {
        if (_resultPath is not null)
            ShellActions.ShowInFolder(_resultPath);
    }

    private void OnOpenReadme(object sender, RoutedEventArgs e) => ShellActions.OpenReadme();

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
