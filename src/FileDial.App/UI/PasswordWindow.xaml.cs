using System.Windows;
using System.Windows.Input;
using FileDial.Core.Archives;
using FileDial.Core.Localization;
using FileDial.Core.Menu;

namespace FileDial.App.UI;

/// <summary>
/// Пароль к архиву, PDF или документу. Режим «новый» — дважды, с проверкой длины и совпадения; режим «ввести» — один раз
/// (с подсказкой «неверный пароль» при повторе). Пароль никуда не сохраняется.
/// </summary>
public partial class PasswordWindow : Window
{
    public const int MinLength = 6;

    private readonly bool _create;
    private readonly UiPasswordSubject _subject;

    private PasswordWindow(string fileName, bool create, bool wrongBefore, string subjectPath)
    {
        InitializeComponent();
        _create = create;
        _subject = Subject(subjectPath);
        FileText.Text = fileName;
        TitleText.Text = create ? Lang.T.ProtectWithPassword : Lang.T.UiPasswordProtected(_subject);
        OkButton.Content = create ? Lang.T.UiProtect : Lang.T.UiOpen;
        ConfirmPanel.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = create
            ? Lang.T.UiPasswordWarning(MinLength, _subject)
            : wrongBefore ? Lang.T.UiWrongPassword : "";
        if (wrongBefore)
            StatusText.Foreground = Palette.Text;
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string? Password { get; private set; }

    /// <summary>Спросить новый пароль; null — отказались.</summary>
    /// <param name="subjectPath">Какой файл защищаем (по расширению — «архив», «PDF», «документ»).</param>
    public static string? AskNew(string fileName, string subjectPath, Window? owner = null) =>
        Ask(fileName, create: true, false, owner, subjectPath);

    /// <summary>Спросить пароль от архива или PDF; null — отказались.</summary>
    public static string? AskExisting(string fileName, bool wrongBefore, Window? owner = null) =>
        Ask(fileName, create: false, wrongBefore, owner, fileName);

    private static UiPasswordSubject Subject(string path) => FileKinds.Of(path) switch
    {
        FileKind.Pdf => UiPasswordSubject.Pdf,
        FileKind.Document => UiPasswordSubject.Document,
        _ => UiPasswordSubject.Archive,
    };

    private static string? Ask(string fileName, bool create, bool wrongBefore, Window? owner, string subjectPath)
    {
        var dialog = new PasswordWindow(fileName, create, wrongBefore, subjectPath);
        if (owner is { IsLoaded: true })
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    private string? Problem()
    {
        var p = PasswordBox.Password;
        if (p.Length == 0)
            return "";
        // tar.exe получает пароль в ANSI-кодировке — для открытия такие символы не годятся.
        if (!_create && _subject == UiPasswordSubject.Archive && !ArchiveSupport.IsAnsiSafe(p))
            return Lang.T.UiPasswordTarUnsafe;
        if (!_create)
            return null;
        if (p.Length < MinLength)
            return Lang.T.UiPasswordTooShort(MinLength);
        if (ConfirmBox.Password.Length == 0)
            return Lang.T.UiPasswordRepeatPrompt;
        if (p != ConfirmBox.Password)
            return Lang.T.UiPasswordsDiffer;
        return null;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        var problem = Problem();
        OkButton.IsEnabled = problem is null;
        if (_create || problem is { Length: > 0 })
        {
            StatusText.Text = problem ?? Lang.T.UiPasswordsMatch;
            StatusText.Foreground = problem is null ? Palette.Accent : Palette.TextMuted;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Problem() is not null)
            return;
        Password = PasswordBox.Password;
        DialogResult = true;
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not System.Windows.Controls.PasswordBox)
            DragMove();
    }
}
