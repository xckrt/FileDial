using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FileDial.App.UI;
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

    internal SetupWindow(bool uninstall, string sourceExe, string version, InstalledApp? installed)
    {
        InitializeComponent();
        _uninstall = uninstall;
        _sourceExe = sourceExe;
        _version = version;
        _installed = installed;
        _directory = installed?.Directory ?? Installer.DefaultDirectory;
        VersionText.Text = $"Версия {version}";

        if (uninstall)
            InitUninstall();
        else
            InitInstall();

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
        Title = "Установка FileDial";
        Dial.Highlight = 0;
        var settings = SettingsStore.Load(SettingsStore.DefaultPath);
        CenterText.Text = settings.HotkeyGesture.Format();
        PrimaryButton.Content = "Установить";

        if (_installed is { } app)
        {
            var same = app.Version == _version;
            UpdateNote.Text = same
                ? "Эта версия уже установлена — можно переустановить или поменять параметры ниже."
                : $"Установлена версия {app.Version} — она обновится, настройки и история сохранятся.";
            UpdateNote.Visibility = Visibility.Visible;
            PrimaryButton.Content = same ? "Переустановить" : "Обновить";
            // Папку при обновлении не меняем: иначе осталась бы вторая копия программы.
            ChangeFolderButton.IsEnabled = false;
            AutostartBox.IsChecked = _installer.AutostartPointsTo(app.ExePath);
            StartMenuBox.IsChecked = _installer.ShortcutExists(desktop: false, app.ExePath);
            DesktopBox.IsChecked = _installer.ShortcutExists(desktop: true, app.ExePath);
        }

        foreach (var id in AppSettings.Themes)
        {
            var choice = new RadioButton
            {
                Content = id == "auto" ? "Как в Windows" : Theme.Resolve(id).Name,
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

        ShowFolder();
    }

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
        FolderStatus.Text = $"Нужно {SizeFormatter.Format(PayloadBytes)} · на диске {Path.GetPathRoot(_directory)!.TrimEnd('\\')} " +
                            $"свободно {SizeFormatter.Format(free)}";
        FolderStatus.Foreground = Palette.TextMuted;
    }

    private void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var existing = _directory;
        while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) is { } parent)
            existing = parent;
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Куда установить FileDial",
            InitialDirectory = existing,
        };
        if (dialog.ShowDialog(this) != true)
            return;
        _directory = Installer.NormalizeDirectory(dialog.FolderName);
        ShowFolder();
    }

    private async Task InstallAsync()
    {
        var options = new InstallOptions(_directory, AutostartBox.IsChecked == true, StartMenuBox.IsChecked == true,
            DesktopBox.IsChecked == true, ChosenTheme) { ReadmePath = ShellActions.ReadmePath() };
        BeginWork(_installed is null ? "Установка" : "Обновление");
        _cts = new CancellationTokenSource();
        var started = DateTime.UtcNow;
        try
        {
            if (RunningCopies.Any())
            {
                StageText.Text = "Закрываю запущенный FileDial…";
                await RunningCopies.StopAsync(TimeSpan.FromSeconds(10));
            }
            var progress = new Progress<InstallProgress>(p =>
            {
                StageText.Text = p.Stage;
                SetDial(p.Fraction);
                // Файл уже на месте — дальше отмена оставила бы установку наполовину.
                if (p.Fraction >= 0.85)
                    SecondaryButton.Visibility = Visibility.Collapsed;
            });
            _installedExe = await Task.Run(() => _installer.InstallAsync(_sourceExe, _version, options, progress, _cts.Token));
            await FinishAnimationAsync(started, 1);

            ShowDone("Готово", $"FileDial {_version} установлен в {_directory}.");
            AddHowTo();
            LaunchBox.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            ShowOptions(OptionsPage);
            FolderStatus.Text = "Установка отменена — ничего не изменилось.";
            FolderStatus.Foreground = Palette.Text;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Установка", ex);
            ShowFailed("Не получилось установить", Explain(ex));
        }
    }

    private void AddHowTo()
    {
        HowTo.Children.Clear();
        var hotkey = SettingsStore.Load(SettingsStore.DefaultPath).HotkeyGesture.Format();
        string[] steps =
        [
            "Выделите файл в Проводнике или на рабочем столе",
            $"Нажмите {hotkey} — круг появится у курсора",
            "Выберите действие мышью или цифрой 1–8",
        ];
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
            Text = "Значок FileDial живёт в области уведомлений: там настройки и история.",
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 4, 0, 0),
        });
    }

    // --- Удаление ---

    private void InitUninstall()
    {
        Title = "Удаление FileDial";
        Dial.Progress = 1;
        OptionsPage.Visibility = Visibility.Collapsed;
        UninstallPage.Visibility = Visibility.Visible;
        FooterNote.Text = "";
        PrimaryButton.Content = "Удалить";
        UninstallText.Text = $"Программа будет удалена из «{_directory}» вместе с ярлыками, автозапуском и записью в «Приложениях».";

        var data = _installer.DataSize();
        RemoveDataBox.Content = "Удалить также настройки, историю и скачанные компоненты";
        RemoveDataHint.Text = $"ffmpeg, модели распознавания речи и прочее — {SizeFormatter.Format(data)}. " +
                              "Без галочки всё останется для повторной установки.";
        if (data == 0)
            RemoveDataBox.Visibility = RemoveDataHint.Visibility = Visibility.Collapsed;
    }

    private async Task UninstallAsync()
    {
        var removeData = RemoveDataBox.IsChecked == true;
        var app = _installed ?? new InstalledApp(_directory, _version);
        BeginWork("Удаление");
        var started = DateTime.UtcNow;
        try
        {
            if (RunningCopies.Any())
            {
                StageText.Text = "Закрываю запущенный FileDial…";
                await RunningCopies.StopAsync(TimeSpan.FromSeconds(10));
            }
            // Круг гаснет против хода установки.
            var progress = new Progress<InstallProgress>(p =>
            {
                StageText.Text = p.Stage;
                SetDial(1 - p.Fraction);
            });
            _leftovers = await Task.Run(() => _installer.Uninstall(app, removeData, progress));
            await FinishAnimationAsync(started, 0);

            ShowDone("FileDial удалён", removeData
                ? "Программа, настройки и скачанные компоненты удалены."
                : "Настройки и история сохранены — при повторной установке всё вернётся.");
            DoneMark.Visibility = Visibility.Collapsed;
            LaunchBox.Visibility = Visibility.Collapsed;
            PrimaryButton.Content = "Закрыть";
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Удаление", ex);
            ShowFailed("Не получилось удалить", Explain(ex));
        }
    }

    // --- Общее ---

    private void BeginWork(string title)
    {
        _state = State.Working;
        ShowPage(ProgressPage);
        ProgressTitle.Text = title;
        StageText.Text = "Подготовка";
        Dial.Highlight = -1;
        CenterText.Text = _uninstall ? "" : "0%";
        PrimaryButton.Visibility = Visibility.Collapsed;
        SecondaryButton.Visibility = _uninstall ? Visibility.Collapsed : Visibility.Visible;
        SecondaryButton.Content = "Отмена";
        CloseButton.IsEnabled = false;
        FooterNote.Text = "";
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

    private void ShowDone(string title, string text)
    {
        _state = State.Done;
        ShowPage(DonePage);
        DoneTitle.Text = title;
        DoneText.Text = text;
        CenterText.Text = "";
        DoneMark.Visibility = Visibility.Visible;
        Motion.Animate(DoneMark, OpacityProperty, 0, 1, 260);
        PrimaryButton.Content = "Готово";
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Visibility = Visibility.Collapsed;
        CloseButton.IsEnabled = true;
    }

    private void ShowFailed(string title, string text)
    {
        _state = State.Failed;
        ShowPage(DonePage);
        DoneTitle.Text = title;
        DoneText.Text = text;
        HowTo.Children.Clear();
        LaunchBox.Visibility = Visibility.Collapsed;
        PrimaryButton.Content = "Назад";
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Content = "Закрыть";
        SecondaryButton.Visibility = Visibility.Visible;
        CloseButton.IsEnabled = true;
        SetDial(_uninstall ? 1 : 0);
        CenterText.Text = "";
    }

    private void ShowOptions(UIElement page)
    {
        _state = State.Options;
        ShowPage(_uninstall ? UninstallPage : page);
        PrimaryButton.Content = _uninstall ? "Удалить" : _installed is null ? "Установить" : "Обновить";
        PrimaryButton.Visibility = Visibility.Visible;
        SecondaryButton.Content = "Отмена";
        SecondaryButton.Visibility = Visibility.Visible;
        CloseButton.IsEnabled = true;
        FooterNote.Text = _uninstall ? "" : "Без прав администратора";
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
        UnauthorizedAccessException => "Нет доступа к папке или реестру. Выберите папку в своём профиле и попробуйте снова.",
        IOException io => $"Файл занят или диск недоступен: {io.Message}",
        _ => "Подробности записаны в журнал ошибок (%LOCALAPPDATA%\\FileDial\\errors.log).",
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
