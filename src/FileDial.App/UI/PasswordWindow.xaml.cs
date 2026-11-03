using System.Windows;
using System.Windows.Input;
using FileDial.Core.Archives;
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
    private readonly string _subject;

    private PasswordWindow(string fileName, bool create, bool wrongBefore, string subjectPath)
    {
        InitializeComponent();
        _create = create;
        _subject = Subject(subjectPath);
        FileText.Text = fileName;
        TitleText.Text = create ? "Защитить паролем" : $"{char.ToUpperInvariant(_subject[0])}{_subject[1..]} защищён паролем";
        OkButton.Content = create ? "Защитить" : "Открыть";
        ConfirmPanel.Visibility = create ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = create
            ? $"Не короче {MinLength} символов. Без пароля {_subject} не открыть — восстановить его нельзя."
            : wrongBefore ? "Неверный пароль — попробуйте ещё раз." : "";
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

    private static string Subject(string path) => FileKinds.Of(path) switch
    {
        FileKind.Pdf => "PDF",
        FileKind.Document => "документ",
        _ => "архив",
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
        if (!_create && _subject == "архив" && !ArchiveSupport.IsAnsiSafe(p))
            return "В пароле есть символы, которые встроенный tar.exe не передаёт.";
        if (!_create)
            return null;
        if (p.Length < MinLength)
            return $"Не короче {MinLength} символов.";
        if (ConfirmBox.Password.Length == 0)
            return "Повторите пароль.";
        if (p != ConfirmBox.Password)
            return "Пароли не совпадают.";
        return null;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        var problem = Problem();
        OkButton.IsEnabled = problem is null;
        if (_create || problem is { Length: > 0 })
        {
            StatusText.Text = problem ?? "Пароли совпадают.";
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
