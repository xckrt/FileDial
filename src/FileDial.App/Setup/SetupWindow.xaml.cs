using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FileDial.App.UI;
using FileDial.Core.Localization;
using FileDial.Core.Settings;
using FileDial.Core.Setup;
using FileDial.Core.Text;

namespace FileDial.App.Setup;

/// <summary>
/// Установщик и удаление FileDial в одном окне: слева круг, который заполняется ходом работы,
/// справа — параметры, ход и итог. Всё для текущего пользователя, без прав администратора.
/// </summary>
public partial class SetupWindow : Window
{
    private enum State { Options, Working, Done, Failed }

    // Даже быстрая установка показывает заполнение круга целиком — иначе мелькнуло бы и не понятно, что произошло.
    private static readonly TimeSpan MinimumWork = TimeSpan.FromMilliseconds(1400);

    private readonly bool _uninstall;
    private readonly string _sourceExe;
    private readonly string _version;
    private readonly InstalledApp? _installed;
    private readonly Installer _installer = new(InstallEnvironment.Current);
    private string _directory;
    private State _state = State.Options;
    private CancellationTokenSource? _cts;
    private string? _installedExe;
    private IReadOnlyList<string> _leftovers = [];

    // Тексты, зависящие от хода работы, — функциями: при смене языка окно пересобирает их на новом (ApplyTexts).
    private Func<string> _progressTitle = () => "";
    private Func<string>? _stageText;
    private Func<string> _doneTitle = () => "";
    private Func<string> _doneText = () => "";
    private bool _showHowTo;
    private Func<string>? _folderNote;
    private string _language = "auto";

    internal SetupWindow(bool uninstall, string sourceExe, string version, InstalledApp? installed)
    {
        InitializeComponent();
        _uninstall = uninstall;
        _sourceExe = sourceExe;
        _version = version;
        _installed = installed;
        _directory = installed?.Directory ?? Installer.DefaultDirectory;

        if (uninstall)
            InitUninstall();
        else
            InitInstall();
        ApplyTexts();

        // Язык выбирается прямо в установщике — окно переводится сразу.
        Lang.Changed += ApplyTexts;
        Closed += (_, _) => Lang.Changed -= ApplyTexts;

        // Появление: круг доворачивается на место, как в самой программе.
        if (Motion.Enabled)
        {
            DialHost.Opacity = 0;
            DialSpin.Angle = -30;
            DialScale.ScaleX = DialScale.ScaleY = 0.92;
        }
        Loaded += (_, _) =>
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Motion.Animate(DialHost, OpacityProperty, 0, 1, 300);
            Motion.Animate(DialSpin, RotateTransform.AngleProperty, -30, 0, 520, easing: ease);
            Motion.Animate(DialScale, ScaleTransform.ScaleXProperty, 0.92, 1, 520, easing: ease);
            Motion.Animate(DialScale, ScaleTransform.ScaleYProperty, 0.92, 1, 520, easing: ease);
        };
    }

    // --- Установка ---

    private void InitInstall()
    {
        Dial.Highlight = 0;
        var settings = SettingsStore.Load(SettingsStore.DefaultPath);
        CenterText.Text = settings.HotkeyGesture.Format();

        if (_installed is { } app)
        {
            UpdateNote.Visibility = Visibility.Visible;
            // Папку при обновлении не меняем: иначе осталась бы вторая копия программы.
            ChangeFolderButton.IsEnabled = false;
            AutostartBox.IsChecked = _installer.AutostartPointsTo(app.ExePath);
            StartMenuBox.IsChecked = _installer.ShortcutExists(desktop: false, app.ExePath);
            DesktopBox.IsChecked = _installer.ShortcutExists(desktop: true, app.ExePath);
            ContextMenuBox.IsChecked = settings.ContextMenu; // реестр программа сверяет с настройкой
        }

        foreach (var id in AppSettings.Themes)
        {
            var choice = new RadioButton
            {
                Tag = id,
                GroupName = "SetupTheme",
                IsChecked = id == settings.Theme,
                Style = (Style)Application.Current.FindResource("SegmentRadio"),
            };
            // Тема видна сразу — на самом установщике.
            choice.Checked += (_, _) =>
            {
                Palette.Apply(Theme.Resolve(id));
                Dial.InvalidateVisual();
            };
            ThemeChoices.Children.Add(choice);
        }

        _language = settings.Language;
        foreach (var code in Lang.Settings)
        {
            var choice = new RadioButton
            {
                Tag = code,
                GroupName = "SetupLanguage",
                IsChecked = code == _language,
                Style = (Style)Application.Current.FindResource("SegmentRadio"),
            };
            choice.Checked += (_, _) =>
            {
                _language = code;
                Lang.Set(code);
            };
            LanguageChoices.Children.Add(choice);
        }
    }

    /// <summary>
    /// Все тексты окна, которые задаются из кода, — для текущего состояния. Вызывается при открытии и при смене языка;
    /// постоянные подписи переводятся сами (<c>{l:T …}</c> в разметке).
    /// </summary>
    private void ApplyTexts()
    {
        var t = Lang.T;
        Title = _uninstall ? t.SetupUninstallWindowTitle : t.SetupWindowTitle;
        VersionText.Text = t.SetupVersion(_version);

        if (_uninstall)
        {
            UninstallText.Text = t.SetupUninstallText(_directory);
            RemoveDataBox.Content = t.SetupRemoveData;
            RemoveDataHint.Text = t.SetupRemoveDataHint(SizeFormatter.Format(_dataSize));
        }
        else
        {
            if (_installed is { } app)
                UpdateNote.Text = app.Version == _version ? t.SetupSameVersionNote : t.SetupUpdateNote(app.Version);
            foreach (var choice in ThemeChoices.Children.OfType<RadioButton>())
                choice.Content = choice.Tag is "auto" ? t.LikeWindows : Theme.Resolve((string)choice.Tag).Name;
            // Названия языков — на них самих, не переводятся.
            foreach (var choice in LanguageChoices.Children.OfType<RadioButton>())
                choice.Content = choice.Tag switch
                {
                    "ru" => new RuTexts().NativeName,
                    "en" => new EnTexts().NativeName,
                    _ => t.LikeWindows,
                };
            if (_folderNote is { } note)
                FolderStatus.Text = note();
            else if (_state == State.Options)
                ShowFolder(); // заодно включает кнопку — только на странице параметров
        }

        switch (_state)
        {
            case State.Options:
                PrimaryButton.Content = OptionsPrimaryText;
                SecondaryButton.Content = t.SetupCancel;
                FooterNote.Text = _uninstall ? "" : t.SetupNoAdmin;
                break;
            case State.Working:
                SecondaryButton.Content = t.SetupCancel;
                ProgressTitle.Text = _progressTitle();
                if (_stageText is { } stage)
                    StageText.Text = stage();
                break;
            case State.Done:
                PrimaryButton.Content = _uninstall ? t.SetupClose : t.SetupDone;
                DoneTitle.Text = _doneTitle();
                DoneText.Text = _doneText();
                if (_showHowTo)
                    AddHowTo();
                break;
            case State.Failed:
                PrimaryButton.Content = t.SetupBack;
                SecondaryButton.Content = t.SetupClose;
                DoneTitle.Text = _doneTitle();
                DoneText.Text = _doneText();
                break;
        }
    }

    private string OptionsPrimaryText =>
        _uninstall ? Lang.T.SetupRemove
        : _installed is null ? Lang.T.SetupInstall
        : _installed.Version == _version ? Lang.T.SetupReinstall
        : Lang.T.SetupUpdate;

    private string ChosenTheme =>
        ThemeChoices.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "graphite";

    private long PayloadBytes => new FileInfo(_sourceExe).Length;

    private void ShowFolder()
    {
        FolderBox.Text = _directory;
        var problem = Installer.WhyNot(_directory, PayloadBytes);
        PrimaryButton.IsEnabled = problem is null;
        if (problem is not null)
        {
            FolderStatus.Text = problem;
            FolderStatus.Foreground = Palette.Text;
            return;
        }
        var free = new DriveInfo(Path.GetPathRoot(_directory)!).AvailableFreeSpace;
        FolderStatus.Text = Lang.T.SetupFolderSpace(SizeFormatter.Format(PayloadBytes),
            Path.GetPathRoot(_directory)!.TrimEnd('\\'), SizeFormatter.Format(free));
        FolderStatus.Foreground = Palette.TextMuted;
    }

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var existing = _directory;
        while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent)
            existing = parent;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Lang.T.SetupChooseFolder,
            InitialDirectory = existing,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        _directory = Installer.NormalizeDirectory(dialog.FolderName);
        _folderNote = null;
        ShowFolder();
    }

    private async Task InstallAsync()
    {
        var options = new InstallOptions(_directory, AutostartBox.IsChecked == true, StartMenuBox.IsChecked == true,
            DesktopBox.IsChecked == true, ChosenTheme, ContextMenuBox.IsChecked == true) { ReadmePaths = ShellActions.AllReadmes() };
        BeginWork(_installed is null ? () => Lang.T.SetupInstalling : () => Lang.T.SetupUpdating);
        _cts = new CancellationTokenSource();
        var started = DateTime.UtcNow;
        try
        {
            if (RunningCopies.Any())
            {
                SetStage(() => Lang.T.SetupClosingRunning);
                await RunningCopies.StopAsync(TimeSpan.FromSeconds(10));
            }
            var progress = new Progress<InstallProgress>(p =>
            {
                // Этапы пишет установщик ядра на текущем языке; язык во время работы не меняется.
                _stageText = null;
                StageText.Text = p.Stage;
                SetDial(p.Fraction);
                // Файл уже на месте — дальше отмена оставила бы установку наполовину.
                if (p.Fraction >= 0.85)
                    SecondaryButton.Visibility = Visibility.Collapsed;
            });
            _installedExe = await Task.Run(() => _installer.InstallAsync(_sourceExe, _version, options, progress, _cts.Token));
            await FinishAnimationAsync(started, 1);
            SaveLanguage();

            var directory = _directory;
            ShowDone(() => Lang.T.SetupDone, () => Lang.T.SetupInstalledTo(_version, directory));
            _showHowTo = true;
            AddHowTo();
            LaunchBox.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            ShowOptions(OptionsPage);
            _folderNote = () => Lang.T.SetupCancelled;
            FolderStatus.Text = _folderNote();
            FolderStatus.Foreground = Palette.Text;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Установка", ex);
            ShowFailed(() => Lang.T.SetupInstallFailed, () => Explain(ex));
        }
    }

    private void AddHowTo()
    {
        HowTo.Children.Clear();
        var hotkey = SettingsStore.Load(SettingsStore.DefaultPath).HotkeyGesture.Format();
        string[] steps = [Lang.T.SetupStepSelect, Lang.T.SetupStepPress(hotkey), Lang.T.SetupStepPick];
        for (var i = 0; i < steps.Length; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var number = new TextBlock
            {
                Text = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                FontFamily = Palette.DialFont,
                FontSize = 16,
                Width = 22,
                Foreground = Palette.Accent,
            };
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(number);
            row.Children.Add(new TextBlock { Text = steps[i], FontSize = 13, Foreground = Palette.Text, TextWrapping = TextWrapping.Wrap });
            HowTo.Children.Add(row);
        }
        HowTo.Children.Add(new TextBlock
        {
            Text = Lang.T.SetupTrayHint,
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 4, 0, 0),
        });
    }

    /// <summary>Язык, выбранный в установщике, — в настройки программы (тему записывает сам установщик).</summary>
    private void SaveLanguage()
    {
        try
        {
            var settings = SettingsStore.Load(SettingsStore.DefaultPath);
            if (settings.Language != _language)
                SettingsStore.Save(SettingsStore.DefaultPath, settings with { Language = _language });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Язык после установки", ex);
        }
    }

    // --- Удаление ---

    private long _dataSize;

    private void InitUninstall()
    {
        Dial.Progress = 1;
        OptionsPage.Visibility = Visibility.Collapsed;
        UninstallPage.Visibility = Visibility.Visible;
        FooterNote.Text = "";

        var data = _dataSize = _installer.DataSize();
        if (data == 0)
            RemoveDataBox.Visibility = RemoveDataHint.Visibility = Visibility.Collapsed;
    }

    private async Task UninstallAsync()
    {
        var removeData = RemoveDataBox.IsChecked == true;
        var app = _installed ?? new InstalledApp(_directory, _version);
        BeginWork(() => Lang.T.SetupUninstalling);
        var started = DateTime.UtcNow;
        try
        {
            if (RunningCopies.Any())
            {
                SetStage(() => Lang.T.SetupClosingRunning);
                await RunningCopies.StopAsync(TimeSpan.FromSeconds(10));
            }
            // Круг гаснет против хода установки.
            var progress = new Progress<InstallProgress>(p =>
            {
                _stageText = null;
                StageText.Text = p.Stage;
                SetDial(1 - p.Fraction);
            });
            _leftovers = await Task.Run(() => _installer.Uninstall(app, removeData, progress));
            await FinishAnimationAsync(started, 0);

            ShowDone(() => Lang.T.SetupRemoved,
                () => removeData ? Lang.T.SetupRemovedAll : Lang.T.SetupRemovedKeptData);
            DoneMark.Visibility = Visibility.Collapsed;
            LaunchBox.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Удаление", ex);
            ShowFailed(() => Lang.T.SetupUninstallFailed, () => Explain(ex));
        }
    }

    // --- Общее ---

    private void BeginWork(Func<string> title)
    {
        _state = State.Working;
        _progressTitle = title;
        _stageText = () => Lang.T.SetupPreparing;
        ShowPage(ProgressPage);
        Dial.Highlight = -1;
        CenterText.Text = _uninstall ? "" : "0%";
        PrimaryButton.Visibility = Visibility.Collapsed;
        SecondaryButton.Visibility = _uninstall ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.IsEnabled = false;
        FooterNote.Text = "";
        ApplyTexts();
    }

    private void SetStage(Func<string> stage)
    {
        _stageText = stage;
        StageText.Text = stage();
    }

    private void SetDial(double fraction)
    {
        Motion.To(Dial, DialArt.ProgressProperty, fraction, 260);
        if (!_uninstall && _state != State.Done)
            CenterText.Text = $"{fraction * 100:0}%";
    }

    private async Task FinishAnimationAsync(DateTime started, double final)
    {
        var left = MinimumWork - (DateTime.UtcNow - started);
        if (Motion.Enabled && left > TimeSpan.Zero)
        {
            // Дозаполняем круг плавно за оставшееся время.
            Motion.To(Dial, DialArt.ProgressProperty, final, (int)left.TotalMilliseconds);
            await Task.Delay(left);
        }
        SetDial(final);
    }

    private void ShowDone(Func<string> title, Func<string> text)
    {
        _state = State.Done;
        _doneTitle = title;
        _doneText = text;
        ShowPage(DonePage);
        ApplyTexts();
        CenterText.Text = "";
        DoneMark.Visibility = Visibility.Visible;
        Motion.Animate(DoneMark, OpacityProperty, 0, 1, 260);
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Visibility = Visibility.Collapsed;
        CloseButton.IsEnabled = true;
    }

    private void ShowFailed(Func<string> title, Func<string> text)
    {
        _state = State.Failed;
        _doneTitle = title;
        _doneText = text;
        _showHowTo = false;
        ShowPage(DonePage);
        ApplyTexts();
        HowTo.Children.Clear();
        LaunchBox.Visibility = Visibility.Collapsed;
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Visibility = Visibility.Visible;
        CloseButton.IsEnabled = true;
        SetDial(_uninstall ? 1 : 0);
        CenterText.Text = "";
    }

    private void ShowOptions(UIElement page)
    {
        _state = State.Options;
        ShowPage(_uninstall ? UninstallPage : page);
        ApplyTexts();
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Visibility = Visibility.Visible;
        CloseButton.IsEnabled = true;
        SetDial(_uninstall ? 1 : 0);
        if (!_uninstall && _state != State.Done)
        {
            Dial.Highlight = 0;
            CenterText.Text = SettingsStore.Load(SettingsStore.DefaultPath).HotkeyGesture.Format();
        }
    }

    private void ShowPage(UIElement page)
    {
        foreach (var p in new UIElement[] { OptionsPage, UninstallPage, ProgressPage, DonePage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;
        Motion.Animate(page, OpacityProperty, 0, 1, 220);
    }

    private static string Explain(Exception ex) => ex switch
    {
        UnauthorizedAccessException => Lang.T.SetupNoAccess,
        IOException io => Lang.T.SetupFileBusy(io.Message),
        _ => Lang.T.SetupSeeErrorLog,
    };

    private async void OnPrimary(object sender, RoutedEventArgs e)
    {
        switch (_state)
        {
            case State.Options when _uninstall:
                await UninstallAsync();
                break;
            case State.Options:
                await InstallAsync();
                break;
            case State.Done:
                Finish();
                break;
            case State.Failed:
                ShowOptions(OptionsPage);
                break;
        }
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        if (_state == State.Working)
            _cts?.Cancel();
        else
            Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_state == State.Done)
            Finish();
        else if (_state != State.Working)
            Close();
    }

    private void Finish()
    {
        if (_uninstall)
        {
            SetupLauncher.ScheduleRemoval(_leftovers, _directory);
        }
        else if (LaunchBox.IsChecked == true && _installedExe is { } exe)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = _directory })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                ErrorLog.Write("Запуск после установки", ex);
            }
        }
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Во время работы окно не закрывается: половина установки хуже любой.
        if (_state == State.Working)
            e.Cancel = true;
        base.OnClosing(e);
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
            DragMove();
    }
}
