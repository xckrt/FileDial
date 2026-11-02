using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FileDial.App.Interop;
using FileDial.Core.Compression;
using FileDial.Core.History;
using FileDial.Core.Jobs;
using FileDial.Core.Settings;

namespace FileDial.App.UI;

/// <summary>Окно выполнения действия над файлом. Вся работа — асинхронно, UI не блокируется.</summary>
public partial class JobWindow : Window
{
    private readonly string _sourcePath;
    private readonly string _actionId;
    private readonly Func<FfmpegTools, IFileJob>? _jobFactory;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private string? _resultPath;
    private bool _running;

    /// <summary>Закрыть окно после отмены (например, при выходе из программы).</summary>
    private bool _closeWhenStopped;

    /// <param name="jobFactory">Готовая задача (например, обрезка с введёнными границами); иначе — по id действия.</param>
    internal JobWindow(string actionId, string actionLabel, string sourcePath, NativeMethods.POINT cursor,
        AppSettings? settings = null, Func<FfmpegTools, IFileJob>? jobFactory = null)
    {
        InitializeComponent();
        _settings = settings ?? new AppSettings();
        _jobFactory = jobFactory;
        _sourcePath = sourcePath;
        _actionId = actionId;
        ActionText.Text = actionLabel.Replace(' ', ' ');
        FileNameText.Text = Path.GetFileName(sourcePath);
        FileNameText.ToolTip = sourcePath;
        StageText.Text = "Подготовка";
        Opacity = 0;

        var placement = new WindowPlacement(this, cursor, PlacementMode.NearCursor, Width, Height);
        SourceInitialized += (_, _) => placement.Apply();
        Loaded += async (_, _) =>
        {
            placement.Apply();
            Motion.To(this, OpacityProperty, 1, Motion.Normal);
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

    public string ActionId => _actionId;

    /// <summary>Работа ещё идёт (или вот-вот начнётся) — итог не показан.</summary>
    public bool IsBusy { get; private set; } = true;

    private async Task RunAsync()
    {
        var tools = FfmpegLocator.LocateDefault(_settings.FfmpegFolder);
        if (tools is null)
        {
            ShowFinished(
                "Не найдены ffmpeg.exe и ffprobe.exe. Положите их рядом с FileDial.exe, добавьте в PATH или укажите папку в настройках.",
                HistoryStatus.Failed, showHelp: true);
            return;
        }

        var job = _jobFactory?.Invoke(tools) ?? FileJobs.Create(_actionId, tools, _settings.Jobs);
        if (job is null)
        {
            ShowFinished("Это действие пока не реализовано.", HistoryStatus.Failed);
            return;
        }

        _running = true;
        var progress = new Progress<JobProgress>(p =>
        {
            if (!_running) return; // поздние отчёты после завершения не должны перетирать итог
            StageText.Text = p.Stage;
            SetProgress(p.Fraction);
        });

        try
        {
            // Отдельный поток пула: чтение файла и ожидание процессов не трогают UI-поток.
            var result = await Task.Run(() => job.RunAsync(_sourcePath, progress, _cts.Token));
            _running = false;

            if (result.Status == JobStatus.NothingToDo)
            {
                StageText.Text = "Ничего делать не нужно";
                ResetProgress();
                ShowFinished(result.Message, HistoryStatus.NothingToDo);
                return;
            }

            _resultPath = result.OutputPath;
            StageText.Text = "Готово";
            SetProgress(1);
            ShowFinished(result.Message, HistoryStatus.Done);
            RevealButton.Visibility = Visibility.Visible;
            RevealButton.Focus();
            if (_settings.OpenFolderWhenDone && _resultPath is not null)
                ShellActions.ShowInFolder(_resultPath);
        }
        catch (OperationCanceledException)
        {
            _running = false;
            if (_closeWhenStopped)
            {
                Record(HistoryStatus.Cancelled, "Отменено при выходе из программы.");
                Close();
                return;
            }
            StageText.Text = "Отменено";
            ResetProgress();
            ShowFinished("Отменено. Исходный файл не изменён, временные файлы удалены.", HistoryStatus.Cancelled);
        }
        catch (CompressionException ex)
        {
            _running = false;
            StageText.Text = "Ошибка";
            ResetProgress();
            ShowFinished(ex.Details is { Length: > 0 } d ? $"{ex.Message}\n{LastLine(d)}" : ex.Message, HistoryStatus.Failed);
            if (ex.Details is not null)
                ErrorLog.Write("ffmpeg", ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _running = false;
            StageText.Text = "Ошибка";
            ResetProgress();
            ShowFinished("Исходный файл не найден: его переместили, переименовали или удалили.", HistoryStatus.Failed);
        }
        catch (UnauthorizedAccessException)
        {
            _running = false;
            StageText.Text = "Ошибка";
            ResetProgress();
            ShowFinished("Нет доступа к исходному файлу.", HistoryStatus.Failed);
        }
        catch (Exception ex)
        {
            _running = false;
            ErrorLog.Write("Действие над файлом", ex);
            StageText.Text = "Ошибка";
            ResetProgress();
            ShowFinished($"Не удалось обработать файл: {ex.Message}", HistoryStatus.Failed);
        }
    }

    /// <summary>null — бегущая полоса без процентов (короткие операции, где длительность неизвестна).</summary>
    private void SetProgress(double? fraction)
    {
        ProgressIndicator.IsIndeterminate = fraction is null;
        PercentText.Text = fraction is { } f ? $"{f * 100:0}%" : string.Empty;
        if (fraction is { } value)
            Motion.To(ProgressIndicator, RangeBase.ValueProperty, value, 200);
    }

    /// <summary>Итог без результата (ошибка, отмена, нечего делать): пустая полоса без процентов.</summary>
    private void ResetProgress()
    {
        SetProgress(0);
        PercentText.Text = string.Empty;
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? text;

    /// <summary>Итог действия — для истории.</summary>
    public event Action<HistoryEntry>? Finished;

    private void Record(HistoryStatus status, string message) =>
        Finished?.Invoke(new HistoryEntry
        {
            Time = DateTimeOffset.Now,
            Action = ActionText.Text,
            SourcePath = _sourcePath,
            OutputPath = status == HistoryStatus.Done ? _resultPath : null,
            Status = status,
            Message = message,
        });

    private void ShowFinished(string message, HistoryStatus status, bool showHelp = false)
    {
        IsBusy = false;
        Record(status, message);
        ProgressIndicator.IsIndeterminate = false;
        MessageText.Text = message;
        MessageText.ToolTip = message;
        MessageText.Visibility = Visibility.Visible;
        MessageText.Opacity = 0;
        Motion.To(MessageText, OpacityProperty, 1, Motion.Normal);
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
