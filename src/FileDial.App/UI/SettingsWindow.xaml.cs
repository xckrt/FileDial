using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileDial.App.Interop;
using FileDial.Core.Compression;
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

    /// <param name="apply">Применяет настройки и автозапуск; возвращает текст ошибки или null.</param>
    internal SettingsWindow(AppSettings settings, bool autostart, NativeMethods.POINT cursor,
        Func<AppSettings, bool, string?> apply)
    {
        InitializeComponent();
        _apply = apply;
        _initial = settings;
        _hotkey = settings.HotkeyGesture;
        _ffmpegFolder = settings.FfmpegFolder;

        HotkeyBox.Text = _hotkey.Format();
        AutostartBox.IsChecked = autostart;
        AnimationsBox.IsChecked = settings.Animations;
        OpenFolderBox.IsChecked = settings.OpenFolderWhenDone;
        OutputFolderBox.Text = settings.OutputFolder ?? "";
        (settings.OutputFolder is null ? NextToSourceRadio : FolderRadio).IsChecked = true;
        UpdateOutputControls();

        FillTargets(VideoTargets, "Video", AppSettings.VideoTargets, settings.VideoTargetBytes);
        FillTargets(ImageTargets, "Image", AppSettings.ImageTargets, settings.ImageTargetBytes);
        UpdateFfmpegStatus();

        var placement = new WindowPlacement(this, cursor, PlacementMode.CenterOnCursor, Width, Height);
        SourceInitialized += (_, _) => placement.Apply();
        Opacity = 0;
        Loaded += (_, _) =>
        {
            placement.Apply();
            Motion.To(this, OpacityProperty, 1, Motion.Normal);
        };
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
            OpenFolderWhenDone = OpenFolderBox.IsChecked == true,
            OutputFolder = string.IsNullOrEmpty(folder) ? null : folder,
            FfmpegFolder = _ffmpegFolder,
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
