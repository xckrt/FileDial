using System.Windows;
using System.Windows.Controls;
using FileDial.Core.Custom;
using FileDial.Core.Localization;

namespace FileDial.App.UI;

// Раздел «Мои действия»: список с кнопками «Изменить» и «Удалить», добавление через редактор.
// Изменения, как и остальные настройки, применяются только по «Сохранить».
public partial class SettingsWindow
{
    private List<CustomAction> _customActions = [];

    private void RefreshCustomList()
    {
        CustomList.Children.Clear();
        if (_customActions.Count == 0)
        {
            CustomList.Children.Add(new TextBlock
            {
                Text = Lang.T.CstEmpty,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
            });
            return;
        }
        foreach (var action in _customActions)
            CustomList.Children.Add(CustomRow(action));
    }

    private Border CustomRow(CustomAction action)
    {
        var edit = new Button { Content = Lang.T.CstEdit, Style = (Style)FindResource("SmallButton") };
        edit.Click += (_, _) => EditCustomAction(action);
        var delete = new Button { Content = Lang.T.CstDelete, Style = (Style)FindResource("SmallButton") };
        delete.Click += (_, _) =>
        {
            _customActions.Remove(action);
            RefreshCustomList();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(edit);
        buttons.Children.Add(delete);
        DockPanel.SetDock(buttons, Dock.Right);

        var applies = action.AppliesTo == CustomActions.ByExtension
            ? action.Extensions
            : CustomActionWindow.KindLabel(action.AppliesTo);
        var command = $"{CustomCommand.ProgramText(action)} {action.Arguments}".Trim();
        // Программу удалили или переименовали — видно сразу, а не только по неактивному сектору в круге.
        var problem = CustomCommand.Problem(action);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = action.Name,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = problem ?? $"{applies} · {command}",
            Foreground = (System.Windows.Media.Brush)FindResource(problem is null ? "MutedBrush" : "TextBrush"),
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = command,
        });

        var panel = new DockPanel();
        panel.Children.Add(buttons);
        panel.Children.Add(text);
        return new Border
        {
            Background = (System.Windows.Media.Brush)FindResource("SectorBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 6, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Child = panel,
        };
    }

    private void EditCustomAction(CustomAction action)
    {
        if (CustomActionWindow.Edit(action, _ffmpegFolder, this) is not { } changed)
            return;
        var index = _customActions.IndexOf(action);
        if (index >= 0)
            _customActions[index] = changed;
        RefreshCustomList();
    }

    private void OnAddCustomAction(object sender, RoutedEventArgs e)
    {
        if (CustomActionWindow.Edit(null, _ffmpegFolder, this) is not { } added)
            return;
        _customActions.Add(added);
        RefreshCustomList();
    }
}
