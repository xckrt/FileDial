using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileDial.App.Interop;
using FileDial.Core.Compression;
using FileDial.Core.Jobs;
using FileDial.Core.Settings;
using FileDial.Core.Text;

namespace FileDial.App.UI;

/// <summary>
/// Окно настроек. Изменения применяются только по «Сохранить»; если применить не удалось
/// (например, сочетание занято), окно остаётся открытым с понятной причиной.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Func<AppSettings, bool, string?> _apply;
    private readonly AppSettings _initial;
    private HotkeyGesture _hotkey;
    private string? _ffmpegFolder;
    private string? _whisperModel;
    private CancellationTokenSource? _download;
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <param name="apply">Применяет настройки и автозапуск; возвращает текст ошибки или null.</param>
    /// <param name="focusSpeech">Сразу показать раздел распознавания речи (пришли из круга за моделью).</param>
    internal SettingsWindow(AppSettings settings, bool autostart, NativeMethods.POINT cursor,
        Func<AppSettings, bool, string?> apply, bool focusSpeech = false)
    {
        InitializeComponent();
        _apply = apply;
        _initial = settings;
        _hotkey = settings.HotkeyGesture;
        _ffmpegFolder = settings.FfmpegFolder;
        _whisperModel = settings.WhisperModel;

        HotkeyBox.Text = _hotkey.Format();
        AutostartBox.IsChecked = autostart;
        AnimationsBox.IsChecked = settings.Animations;
        HistoryBox.IsChecked = settings.KeepHistory;
        OpenFolderBox.IsChecked = settings.OpenFolderWhenDone;
        OutputFolderBox.Text = settings.OutputFolder ?? "";
        (settings.OutputFolder is null ? NextToSourceRadio : FolderRadio).IsChecked = true;
        UpdateOutputControls();

        FillTargets(VideoTargets, "Video", AppSettings.VideoTargets, settings.VideoTargetBytes);
        FillTargets(ImageTargets, "Image", AppSettings.ImageTargets, settings.ImageTargetBytes);
        UpdateFfmpegStatus();
        UpdateWhisperStatus();
        foreach (var (code, label) in Speech.Languages)
        {
            WhisperLanguages.Children.Add(new RadioButton
            {
                Content = label,
                Tag = code,
                GroupName = "WhisperLanguage",
                IsChecked = code == settings.WhisperLanguage,
                Style = (Style)Application.Current.FindResource("SegmentRadio"),
            });
        }

        // На маленьких экранах окно ниже рабочей области — остальное прокручивается.
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 16);
        var placement = new WindowPlacement(this, cursor, PlacementMode.CenterOnCursor, Width, Height);
        SourceInitialized += (_, _) => placement.Apply();
        Opacity = 0;
        Loaded += (_, _) =>
        {
            placement.Apply();
            Motion.To(this, OpacityProperty, 1, Motion.Normal);
            if (focusSpeech)
                SpeechSection.BringIntoView(new Rect(0, 0, 1, 300));
        };
        // Закрыли окно во время загрузки — останавливаем её, недокачанный файл удаляется.
        Closed += (_, _) => _download?.Cancel();
    }

    /// <summary>Пока идёт запись сочетания, горячую клавишу программы не обрабатываем.</summary>
    public bool IsCapturingHotkey => HotkeyBox.IsKeyboardFocused;

    private static void FillTargets(Panel panel, string group, IReadOnlyList<long> values, long selected)
    {
        foreach (var value in values)
        {
            panel.Children.Add(new RadioButton
            {
                Content = SizeFormatter.Format(value),
                Tag = value,
                GroupName = group,
                IsChecked = value == selected,
                Style = (Style)Application.Current.FindResource("SegmentRadio"),
            });
        }
    }

    private static long SelectedTarget(Panel panel, long fallback) =>
        panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as long? ?? fallback;

    // --- Горячая клавиша ---

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = "Нажмите сочетание…";
        HotkeyHint.Text = "Esc — оставить как было.";
    }

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = _hotkey.Format();
        HotkeyHint.Text = "Щёлкните поле и нажмите сочетание: Ctrl, Alt или Win вместе с буквой, цифрой или F1–F24.";
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        // С зажатым Alt WPF присылает Key.System.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            FocusManager.SetFocusedElement(this, this);
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            // Пока зажаты только модификаторы — показываем, что уже нажато.
            HotkeyBox.Text = ModifiersText(CurrentModifiers()) + "…";
            return;
        }

        var gesture = new HotkeyGesture(CurrentModifiers(), KeyInterop.VirtualKeyFromKey(key));
        if (gesture.Problem() is { } problem)
        {
            HotkeyHint.Text = problem;
            return;
        }

        _hotkey = gesture;
        HotkeyBox.Text = gesture.Format();
        HotkeyHint.Text = "Сочетание будет применено после «Сохранить».";
        // Уходим с поля, чтобы следующее нажатие не перезаписало выбор.
        FocusManager.SetFocusedElement(this, this);
        Keyboard.Focus(this);
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var m = HotkeyModifiers.None;
        var k = Keyboard.Modifiers;
        if (k.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Ctrl;
        if (k.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (k.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (k.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
            m |= HotkeyModifiers.Win;
        return m;
    }

    private static string ModifiersText(HotkeyModifiers m) =>
        string.Concat(new[]
        {
            m.HasFlag(HotkeyModifiers.Ctrl) ? "Ctrl+" : "",
            m.HasFlag(HotkeyModifiers.Alt) ? "Alt+" : "",
            m.HasFlag(HotkeyModifiers.Shift) ? "Shift+" : "",
            m.HasFlag(HotkeyModifiers.Win) ? "Win+" : "",
        });

    // --- Папки ---

    private void OnOutputModeChanged(object sender, RoutedEventArgs e)
    {
        if (IsInitialized)
            UpdateOutputControls();
    }

    private void UpdateOutputControls()
    {
        var custom = FolderRadio.IsChecked == true;
        OutputFolderBox.IsEnabled = PickOutputButton.IsEnabled = custom;
        // Выбрали «в папку», а папки ещё нет — сразу предлагаем выбрать.
        if (custom && IsLoaded && string.IsNullOrWhiteSpace(OutputFolderBox.Text))
            OnPickOutputFolder(this, new RoutedEventArgs());
    }

    private void OnPickOutputFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Папка для результатов" };
        if (!string.IsNullOrWhiteSpace(OutputFolderBox.Text) && Directory.Exists(OutputFolderBox.Text))
            dialog.InitialDirectory = OutputFolderBox.Text;
        if (dialog.ShowDialog(this) == true)
            OutputFolderBox.Text = dialog.FolderName;
        else if (string.IsNullOrWhiteSpace(OutputFolderBox.Text))
            NextToSourceRadio.IsChecked = true; // отказались выбирать — возвращаемся к «рядом с файлом»
    }

    private void OnPickFfmpegFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Папка с ffmpeg.exe и ffprobe.exe" };
        if (dialog.ShowDialog(this) != true)
            return;
        _ffmpegFolder = dialog.FolderName;
        UpdateFfmpegStatus();
    }

    private void OnResetFfmpegFolder(object sender, RoutedEventArgs e)
    {
        _ffmpegFolder = null;
        UpdateFfmpegStatus();
    }

    private void UpdateFfmpegStatus()
    {
        FfmpegFolderBox.Text = _ffmpegFolder ?? "Авто (рядом с программой или PATH)";
        var inFolder = _ffmpegFolder is null ? null : FfmpegLocator.Locate(_ffmpegFolder, null);
        var found = inFolder ?? FfmpegLocator.LocateDefault(_ffmpegFolder);
        FfmpegStatus.Text = (_ffmpegFolder, inFolder, found) switch
        {
            (not null, null, not null) => $"В выбранной папке ffmpeg нет — будет использован {found.Ffmpeg}",
            (not null, null, null) => "В выбранной папке нет ffmpeg.exe и ffprobe.exe, в PATH тоже не найден.",
            (_, _, not null) => $"Найден: {found.Ffmpeg}",
            _ => "ffmpeg не найден. Инструкция — в README, раздел «Установка ffmpeg».",
        };
    }

    private void OnPickWhisperModel(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Модель whisper.cpp",
            Filter = "Модель whisper (ggml-*.bin)|*.bin|Все файлы|*.*",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        _whisperModel = dialog.FileName;
        UpdateWhisperStatus();
    }

    private void OnResetWhisperModel(object sender, RoutedEventArgs e)
    {
        _whisperModel = null;
        UpdateWhisperStatus();
    }

    private void UpdateWhisperStatus()
    {
        var effective = SpeechModels.FindDefault(_whisperModel);
        WhisperModelBox.Text = _whisperModel ?? (effective is null ? "Не установлена" : $"Авто: {effective}");
        WhisperStatus.Text = (_whisperModel, effective) switch
        {
            (not null, _) when !File.Exists(_whisperModel) => "Файл модели не найден.",
            (not null, _) => $"Модель: {Path.GetFileName(_whisperModel)}, {Size(_whisperModel)}. Работает офлайн.",
            (null, not null) => $"Используется {Path.GetFileName(effective)} ({Size(effective)}). Работает офлайн.",
            _ => "Модель не установлена — скачайте её кнопкой ниже. Это нужно один раз, дальше всё работает офлайн.",
        };

        DownloadButtons.Children.Clear();
        foreach (var model in SpeechModels.Downloadable)
        {
            var downloaded = File.Exists(Path.Combine(SpeechModels.DataFolder, model.FileName));
            var button = new Button
            {
                Content = downloaded
                    ? $"{model.Label}: скачана"
                    : $"Скачать: {model.Label.ToLowerInvariant()} ({SizeFormatter.Format(model.SizeBytes)})",
                ToolTip = model.Id == "small" ? "Заметно точнее для русского, медленнее" : "Быстрая, для простой речи",
                Style = (Style)FindResource(downloaded ? "SmallButton" : "AccentButton"),
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = !downloaded && _download is null,
            };
            button.Click += async (_, _) => await DownloadAsync(model);
            DownloadButtons.Children.Add(button);
        }
    }

    private static string Size(string path) => SizeFormatter.Format(new FileInfo(path).Length);

    private async Task DownloadAsync(SpeechModelInfo model)
    {
        _download = new CancellationTokenSource();
        DownloadPanel.Visibility = Visibility.Visible;
        UpdateWhisperStatus();
        var progress = new Progress<(long Done, long Total)>(p =>
        {
            DownloadText.Text = $"{model.FileName}: {SizeFormatter.Format(p.Done)} из {SizeFormatter.Format(p.Total)}";
            DownloadProgress.Value = p.Total > 0 ? (double)p.Done / p.Total : 0;
        });
        DownloadText.Text = $"{model.FileName}: подключение…";
        DownloadProgress.Value = 0;

        try
        {
            var path = await Task.Run(() =>
                SpeechModelDownloader.DownloadAsync(Http, model, SpeechModels.DataFolder, progress, _download.Token));
            // Скачанная модель находится автоматически; выбранный вручную файл заменяем на неё.
            _whisperModel = null;
            ShowError($"Модель {Path.GetFileName(path)} скачана и проверена.");
        }
        catch (OperationCanceledException)
        {
            ShowError("Загрузка отменена.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
        {
            ErrorLog.Write("Загрузка модели", ex);
            ShowError(ex is HttpRequestException
                ? "Не удалось скачать: нет подключения к интернету или сервер недоступен."
                : ex.Message);
        }
        finally
        {
            _download.Dispose();
            _download = null;
            if (IsLoaded)
            {
                DownloadPanel.Visibility = Visibility.Collapsed;
                UpdateWhisperStatus();
            }
        }
    }

    private void OnCancelDownload(object sender, RoutedEventArgs e) => _download?.Cancel();

    // --- Сохранение ---

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var folder = FolderRadio.IsChecked == true ? OutputFolderBox.Text.Trim() : null;
        if (folder is not null && !Directory.Exists(folder))
        {
            ShowError(string.IsNullOrEmpty(folder) ? "Выберите папку для результатов." : "Папка для результатов не найдена.");
            return;
        }

        var settings = _initial with
        {
            Hotkey = _hotkey.Format(),
            Animations = AnimationsBox.IsChecked == true,
            KeepHistory = HistoryBox.IsChecked == true,
            OpenFolderWhenDone = OpenFolderBox.IsChecked == true,
            OutputFolder = string.IsNullOrEmpty(folder) ? null : folder,
            FfmpegFolder = _ffmpegFolder,
            WhisperModel = _whisperModel,
            WhisperLanguage = WhisperLanguages.Children.OfType<RadioButton>()
                .FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "auto",
            VideoTargetBytes = SelectedTarget(VideoTargets, _initial.VideoTargetBytes),
            ImageTargetBytes = SelectedTarget(ImageTargets, _initial.ImageTargetBytes),
        };

        if (_apply(settings, AutostartBox.IsChecked == true) is { } error)
        {
            ShowError(error);
            return;
        }
        Close();
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Opacity = 0;
        Motion.To(StatusText, OpacityProperty, 1, Motion.Normal);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
            DragMove();
    }
}
