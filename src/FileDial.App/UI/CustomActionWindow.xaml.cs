using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileDial.Core.Compression;
using FileDial.Core.Custom;
using FileDial.Core.Localization;

namespace FileDial.App.UI;

/// <summary>
/// Редактор своего действия: название, для каких файлов, программа, аргументы, файл результата.
/// Команда для примерного файла пересчитывается на каждом изменении; сохранить можно только проверенное действие.
/// </summary>
public partial class CustomActionWindow : Window
{
    private readonly string _id;
    private readonly string? _ffmpeg;
    private CustomAction? _result;

    private CustomActionWindow(CustomAction? existing, string? ffmpegFolder)
    {
        InitializeComponent();
        _id = existing?.Id is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N");
        _ffmpeg = FfmpegLocator.LocateDefault(ffmpegFolder)?.Ffmpeg;
        HeadingText.Text = existing is null ? Lang.T.CstNewTitle : Lang.T.CstEditTitle;

        foreach (var kind in CustomActions.Kinds)
        {
            var choice = new RadioButton
            {
                Content = KindLabel(kind),
                Tag = kind,
                GroupName = "AppliesTo",
                Style = (Style)Application.Current.FindResource("SegmentRadio"),
            };
            choice.Checked += (_, _) =>
            {
                ExtensionsPanel.Visibility = kind == CustomActions.ByExtension ? Visibility.Visible : Visibility.Collapsed;
                Refresh();
            };
            KindChoices.Children.Add(choice);
        }

        Fill(existing ?? new CustomAction());
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>Создать (existing = null) или изменить действие; null — отказались.</summary>
    public static CustomAction? Edit(CustomAction? existing, string? ffmpegFolder, Window? owner)
    {
        var dialog = new CustomActionWindow(existing, ffmpegFolder);
        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true ? dialog._result : null;
    }

    public static string KindLabel(string kind) => kind switch
    {
        "video" => Lang.T.CstKindVideo,
        "image" => Lang.T.CstKindImage,
        "audio" => Lang.T.CstKindAudio,
        "pdf" => Lang.T.CstKindPdf,
        "document" => Lang.T.CstKindDocument,
        "archive" => Lang.T.CstKindArchive,
        CustomActions.ByExtension => Lang.T.CstKindExtensions,
        _ => Lang.T.CstKindAny,
    };

    private void Fill(CustomAction action)
    {
        NameBox.Text = action.Name;
        ExtensionsBox.Text = action.Extensions;
        ProgramBox.Text = action.Program;
        ArgumentsBox.Text = action.Arguments;
        SuffixBox.Text = action.OutputSuffix;
        ExtensionBox.Text = action.OutputExtension;
        ShowWindowBox.IsChecked = action.ShowWindow;
        var kind = CustomActions.Kinds.Contains(action.AppliesTo) ? action.AppliesTo : CustomActions.Any;
        foreach (var choice in KindChoices.Children.OfType<RadioButton>())
            choice.IsChecked = (string)choice.Tag == kind;
        Refresh();
    }

    private CustomAction Current() => new()
    {
        Id = _id,
        Name = NameBox.Text.Trim(),
        AppliesTo = KindChoices.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string
                    ?? CustomActions.Any,
        Extensions = ExtensionsBox.Text.Trim(),
        Program = ProgramBox.Text.Trim(),
        Arguments = ArgumentsBox.Text.Trim(),
        OutputSuffix = SuffixBox.Text.Trim(),
        OutputExtension = ExtensionBox.Text.Trim().TrimStart('.'),
        ShowWindow = ShowWindowBox.IsChecked == true,
    };

    /// <summary>Примерный файл того типа, для которого действие: «Документы\Отпуск.mp4».</summary>
    private static string SamplePath(CustomAction action)
    {
        var extension = action.AppliesTo switch
        {
            "video" => ".mp4",
            "image" => ".jpg",
            "audio" => ".mp3",
            "pdf" => ".pdf",
            "document" => ".docx",
            "archive" => ".zip",
            CustomActions.ByExtension => CustomActions.ParseExtensions(action.Extensions).FirstOrDefault() ?? ".txt",
            _ => ".txt",
        };
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(folder.Length > 0 ? folder : @"C:\Users\Public\Documents", Lang.T.CstSampleName + extension);
    }

    private void Refresh()
    {
        if (!IsInitialized || PreviewText is null)
            return;
        var action = Current();
        PreviewText.Text = ProgramBox.Text.Trim().Length == 0 ? "" : CustomCommand.Preview(action, SamplePath(action), _ffmpeg);
        StatusText.Text = "";
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Refresh();

    private void OnBrowseProgram(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Lang.T.CstProgramFilter,
            CheckFileExists = true,
        };
        var current = ProgramBox.Text.Trim().Trim('"');
        if (Path.IsPathFullyQualified(current) && Path.GetDirectoryName(current) is { } folder && Directory.Exists(folder))
            dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(this) == true)
            ProgramBox.Text = dialog.FileName;
    }

    // --- Примеры ---

    private void Example(string name, string kind, string program, string arguments, string suffix, string extension,
        bool showWindow, string? note = null)
    {
        Fill(new CustomAction
        {
            Name = name,
            AppliesTo = kind,
            Program = program,
            Arguments = arguments,
            OutputSuffix = suffix,
            OutputExtension = extension,
            ShowWindow = showWindow,
        });
        if (note is not null)
            StatusText.Text = note;
    }

    private void OnExampleFfmpeg(object sender, RoutedEventArgs e) =>
        Example(Lang.T.CstExampleFfmpegName, "video", CustomCommand.FfmpegVariable,
            "-i {file} -vf scale=-2:720 {out}", "_720p", "mp4", false);

    private void OnExampleNotepad(object sender, RoutedEventArgs e) =>
        Example(Lang.T.CstExampleNotepadName, CustomActions.Any, "notepad.exe", "{file}", "", "", true);

    private void OnExampleMagick(object sender, RoutedEventArgs e) =>
        Example(Lang.T.CstExampleMagickName, "image", "magick", "{file} -resize 50% {out}", "_50", "", false,
            Lang.T.CstMagickNote);

    // --- Сохранение ---

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var action = Current();
        if (CustomCommand.Problem(action) is { } problem)
        {
            StatusText.Text = problem;
            StatusText.Opacity = 0;
            Motion.To(StatusText, OpacityProperty, 1, Motion.Normal);
            return;
        }
        _result = action;
        DialogResult = true;
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox)
            DragMove();
    }
}
