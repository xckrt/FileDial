using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FileDial.App.Interop;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Custom;
using FileDial.Core.History;
using FileDial.Core.Jobs;
using FileDial.Core.Localization;
using FileDial.Core.Menu;
using FileDial.Core.Settings;

namespace FileDial.App.UI;

/// <summary>Окно выполнения действия над файлом. Вся работа — асинхронно, UI не блокируется.</summary>
public partial class JobWindow : Window
{
    private readonly string _sourcePath;
    private readonly IReadOnlyList<string> _sources;
    private readonly string _actionId;
    private readonly Func<FfmpegTools?, IFileJob>? _jobFactory;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private string? _resultPath;
    private bool _running;

    /// <summary>Закрыть окно после отмены (например, при выходе из программы).</summary>
    private bool _closeWhenStopped;

    /// <param name="jobFactory">Готовая задача (например, обрезка с введёнными границами); иначе — по id действия.</param>
    /// <param name="sourcePaths">Один файл или несколько файлов одного типа (действие выполнится для каждого).</param>
    internal JobWindow(string actionId, string actionLabel, IReadOnlyList<string> sourcePaths, NativeMethods.POINT cursor,
        AppSettings? settings = null, Func<FfmpegTools?, IFileJob>? jobFactory = null)
    {
        InitializeComponent();
        _settings = settings ?? new AppSettings();
        _jobFactory = jobFactory;
        _sources = sourcePaths;
        var sourcePath = sourcePaths[0];
        _sourcePath = sourcePath;
        _actionId = actionId;
        ActionText.Text = actionLabel.Replace(' ', ' ');
        FileNameText.Text = IsBatch
            ? SelectionInterpreter.KindCount(FileKinds.Of(sourcePath), sourcePaths.Count)
            : Path.GetFileName(sourcePath);
        FileNameText.ToolTip = string.Join(Environment.NewLine, sourcePaths.Take(20)) +
                               (sourcePaths.Count > 20 ? Environment.NewLine + "…" : "");
        StageText.Text = Lang.T.UiJobPreparing;
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

    private bool IsBatch => _sources.Count > 1;

    /// <summary>Работа ещё идёт (или вот-вот начнётся) — итог не показан.</summary>
    public bool IsBusy { get; private set; } = true;

    private async Task RunAsync()
    {
        var tools = FfmpegLocator.LocateDefault(_settings.FfmpegFolder);
        // Архивам ffmpeg не нужен — их можно распаковывать и до его установки.
        if (tools is null && FileJobs.NeedsFfmpeg(_actionId) && !CustomActionIds.IsCustom(_actionId))
        {
            ShowFinished(
                Lang.T.UiJobNeedsFfmpeg,
                HistoryStatus.Failed, showHelp: true);
            return;
        }

        var job = _jobFactory?.Invoke(tools) ?? FileJobs.Create(_actionId, tools, _settings.Jobs);
        if (job is null)
        {
            ShowFinished(Lang.T.UiJobNotImplemented, HistoryStatus.Failed);
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
            // Отдельный поток пула: чтение файлов и ожидание процессов не трогают UI-поток.
            JobResult result;
            var perFileHistory = false;
            if (job is IMultiFileJob multi)
            {
                // Несколько файлов собираются в один результат (PDF, ZIP) — это одно действие, а не пакет.
                result = await Task.Run(() => multi.RunAsync(_sources, progress, _cts.Token));
            }
            else if (IsBatch)
            {
                perFileHistory = true;
                var summary = await Task.Run(() => BatchRunner.RunAsync(
                    _sources,
                    _ => _jobFactory?.Invoke(tools) ?? FileJobs.Create(_actionId, tools, _settings.Jobs)!,
                    BatchRunner.SkipWhenAlreadyInFormat(_actionId),
                    progress,
                    outcome => Dispatcher.BeginInvoke(() => RecordFile(outcome)),
                    _cts.Token));
                if (summary.Done == 0 && summary.Failed > 0)
                {
                    _running = false;
                    StageText.Text = Lang.T.UiJobError;
                    ResetProgress();
                    ShowFinished(summary.Message, HistoryStatus.Failed, record: false);
                    return;
                }
                result = new JobResult(summary.Done > 0 ? JobStatus.Done : JobStatus.NothingToDo, summary.Message,
                    summary.LastOutput);
            }
            else
            {
                result = await RunAskingPasswordAsync(job, progress);
            }
            _running = false;

            if (result.Status == JobStatus.NothingToDo)
            {
                StageText.Text = Lang.T.UiJobNothingToDo;
                ResetProgress();
                ShowFinished(result.Message, HistoryStatus.NothingToDo, record: !perFileHistory);
                return;
            }

            _resultPath = result.OutputPath;
            StageText.Text = Lang.T.UiJobDone;
            SetProgress(1);
            ShowFinished(result.Message, HistoryStatus.Done, record: !perFileHistory);
            // Своё действие без {out} ничего не создаёт — показывать в папке нечего.
            RevealButton.Visibility = _resultPath is null ? Visibility.Collapsed : Visibility.Visible;
            if (_resultPath is not null)
                RevealButton.Focus();
            if (_settings.OpenFolderWhenDone && _resultPath is not null)
                ShellActions.ShowInFolder(_resultPath);
        }
        catch (OperationCanceledException)
        {
            _running = false;
            if (_closeWhenStopped)
            {
                Record(HistoryStatus.Cancelled, Lang.T.UiJobCancelledOnExit);
                Close();
                return;
            }
            StageText.Text = Lang.T.UiJobCancelled;
            ResetProgress();
            ShowFinished(Lang.T.UiJobCancelledDetails, HistoryStatus.Cancelled);
        }
        catch (CompressionException ex)
        {
            _running = false;
            StageText.Text = Lang.T.UiJobError;
            ResetProgress();
            ShowFinished(ex.Details is { Length: > 0 } d ? $"{ex.Message}\n{LastLine(d)}" : ex.Message, HistoryStatus.Failed);
            if (ex.Details is not null)
                ErrorLog.Write("ffmpeg", ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            _running = false;
            StageText.Text = Lang.T.UiJobError;
            ResetProgress();
            ShowFinished(Lang.T.UiJobSourceNotFound, HistoryStatus.Failed);
        }
        catch (UnauthorizedAccessException)
        {
            _running = false;
            StageText.Text = Lang.T.UiJobError;
            ResetProgress();
            ShowFinished(Lang.T.UiJobNoAccess, HistoryStatus.Failed);
        }
        catch (Exception ex)
        {
            _running = false;
            ErrorLog.Write("Действие над файлом", ex);
            StageText.Text = Lang.T.UiJobError;
            ResetProgress();
            ShowFinished(Lang.T.UiJobFailed(ex.Message), HistoryStatus.Failed);
        }
    }

    /// <summary>
    /// Запуск с повтором: если архиву нужен пароль (или введённый неверен), спрашиваем и пробуем снова.
    /// Отказ от ввода пароля — это отмена.
    /// </summary>
    private async Task<JobResult> RunAskingPasswordAsync(IFileJob job, IProgress<JobProgress> progress)
    {
        while (true)
        {
            try
            {
                return await Task.Run(() => job.RunAsync(_sourcePath, progress, _cts.Token));
            }
            catch (PasswordRequiredException ex) when (job is IPasswordAware aware)
            {
                StageText.Text = Lang.T.UiJobNeedsPassword;
                ProgressIndicator.IsIndeterminate = false;
                var password = PasswordWindow.AskExisting(Path.GetFileName(_sourcePath), ex.WrongPassword, this)
                               ?? throw new OperationCanceledException();
                job = aware.WithPassword(password);
            }
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

    private void RecordFile(FileOutcome outcome) =>
        Finished?.Invoke(new HistoryEntry
        {
            Time = DateTimeOffset.Now,
            Action = ActionText.Text,
            SourcePath = outcome.SourcePath,
            OutputPath = outcome.OutputPath,
            Status = outcome.Status,
            Message = outcome.Message,
        });

    /// <param name="record">false — итоги уже записаны по каждому файлу пакета.</param>
    private void ShowFinished(string message, HistoryStatus status, bool showHelp = false, bool record = true)
    {
        IsBusy = false;
        if (record)
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
            StageText.Text = Lang.T.UiFfmpegNotFound;
            PercentText.Text = string.Empty;
        }
        CloseButton.Focus();
    }

    private void RequestCancel()
    {
        if (!_running || _cts.IsCancellationRequested)
            return;
        StageText.Text = Lang.T.UiJobCancelling;
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

    private void OnOpenReadme(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).OpenFfmpegSetup();
        Close();
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
