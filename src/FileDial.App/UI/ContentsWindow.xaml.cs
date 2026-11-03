using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FileDial.App.Interop;
using FileDial.Core.Archives;
using FileDial.Core.Compression;
using FileDial.Core.Text;

namespace FileDial.App.UI;

/// <summary>Список файлов архива без распаковки: дерево с отступами, размеры, итог.</summary>
public partial class ContentsWindow : Window
{
    private readonly string _archive;
    private readonly Action _extract;

    /// <param name="extract">«Распаковать сюда» — запускает обычное действие из круга.</param>
    internal ContentsWindow(string archive, NativeMethods.POINT cursor, Action extract)
    {
        InitializeComponent();
        _archive = archive;
        _extract = extract;
        ArchiveName.Text = Path.GetFileName(archive);
        ArchiveName.ToolTip = archive;

        var placement = new WindowPlacement(this, cursor, PlacementMode.CenterOnCursor, Width, Height);
        SourceInitialized += (_, _) => placement.Apply();
        Opacity = 0;
        Loaded += async (_, _) =>
        {
            placement.Apply();
            Motion.To(this, OpacityProperty, 1, Motion.Normal);
            await LoadAsync();
        };
    }

    /// <summary>Строка списка.</summary>
    public sealed record Row(string Display, string SizeText, Brush Brush, string Path);

    private async Task LoadAsync()
    {
        try
        {
            var contents = await Task.Run(() => ArchiveListing.ReadAsync(_archive, CancellationToken.None));
            EntriesList.ItemsSource = Rows(contents.Entries);
            var packed = new FileInfo(_archive).Length;
            SummaryText.Text = contents.Files == 0
                ? "Архив пуст."
                : $"{ArchiveSupport.FilesLabel(contents.Files)} · {SizeFormatter.Format(contents.UnpackedBytes)} после распаковки" +
                  $" · архив {SizeFormatter.Format(packed)}";
            ExtractButton.IsEnabled = contents.Files > 0;
        }
        catch (Exception ex) when (ex is CompressionException or IOException or UnauthorizedAccessException)
        {
            SummaryText.Text = ex.Message;
            SummaryText.Foreground = Palette.Text;
        }
    }

    /// <summary>
    /// Дерево из плоского списка: папки и файлы по алфавиту, вложенность — отступом. Папки, которых нет
    /// отдельной записью (бывает в zip), достраиваются из путей файлов.
    /// </summary>
    internal static List<Row> Rows(IReadOnlyList<ArchiveEntryInfo> entries)
    {
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var path = e.Path.TrimEnd('/');
            if (e.IsDirectory) dirs.Add(path);
            for (var slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
                dirs.Add(path[..slash]);
        }

        var items = dirs.Select(d => (Path: d, IsDir: true, Size: 0L))
            .Concat(entries.Where(e => !e.IsDirectory).Select(e => (Path: e.Path, IsDir: false, e.Size)))
            // Как в Проводнике: по частям пути, на каждом уровне сначала папки, потом файлы.
            .OrderBy(i => SortKey(i.Path, i.IsDir), Comparer<string[]>.Create(ComparePaths))
            .ToList();

        return items.Select(i =>
        {
            var depth = i.Path.Count(c => c == '/');
            var name = i.Path[(i.Path.LastIndexOf('/') + 1)..];
            return new Row(new string(' ', depth * 4) + (i.IsDir ? name + "/" : name),
                i.IsDir ? "" : SizeFormatter.Format(i.Size),
                i.IsDir ? Palette.TextMuted : Palette.Text,
                i.Path);
        }).ToList();
    }

    /// <summary>Части пути с пометкой «папка» (0) или «файл» (1): промежуточные части — всегда папки.</summary>
    private static string[] SortKey(string path, bool isDirectory)
    {
        var parts = path.Split('/');
        for (var i = 0; i < parts.Length; i++)
            parts[i] = (i < parts.Length - 1 || isDirectory ? "0" : "1") + parts[i];
        return parts;
    }

    private static int ComparePaths(string[] pa, string[] pb)
    {
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var c = string.Compare(pa[i], pb[i], StringComparison.CurrentCultureIgnoreCase);
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }

    private void OnExtract(object sender, RoutedEventArgs e)
    {
        Close();
        _extract();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
