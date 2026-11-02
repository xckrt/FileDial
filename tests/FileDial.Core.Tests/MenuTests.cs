using FileDial.Core.Menu;
using FileDial.Core.Selection;

namespace FileDial.Core.Tests;

public class ActionCatalogTests
{
    [Theory]
    [InlineData(@"C:\v\a.mp4")]
    [InlineData(@"C:\v\a.MOV")]
    [InlineData(@"C:\v\a.mkv")]
    [InlineData(@"C:\v\a.WebM")]
    public void VideoFiles_GetEightActions_OnlyCompressEnabled(string path)
    {
        var actions = ActionCatalog.ForFile(path, 80_000_000);

        Assert.Equal(8, actions.Count);
        Assert.Equal(
            ["Сжать до 25\u00A0МБ", "Обрезать", "Сделать GIF", "Извлечь звук", "Кадр в картинку", "Субтитры", "Убрать звук", "Формат"],
            actions.Select(a => a.Label));
        Assert.Equal(ActionIds.Compress25, actions[0].Id);
        Assert.True(actions[0].IsEnabled);
        Assert.Null(actions[0].DisabledReason);
        Assert.All(actions.Skip(1), a =>
        {
            Assert.False(a.IsEnabled);
            Assert.Equal(ActionCatalog.NotInPrototype, a.DisabledReason);
        });
    }

    [Theory]
    [InlineData(534_323)]       // 521,8 КБ — как на скриншоте
    [InlineData(25_000_000)]    // ровно цель — сжимать нечего
    [InlineData(0)]
    public void SmallVideo_CompressIsDisabledWithReason(long size)
    {
        var compress = ActionCatalog.ForFile(@"C:\v\a.mp4", size)[0];

        Assert.False(compress.IsEnabled);
        Assert.Equal(ActionCatalog.AlreadySmall, compress.DisabledReason);
    }

    [Fact]
    public void VideoJustAboveTarget_CanBeCompressed()
    {
        Assert.True(ActionCatalog.ForFile(@"C:\v\a.mp4", 25_000_001)[0].IsEnabled);
    }

    [Theory]
    [InlineData(@"C:\v\a.avi")]
    [InlineData(@"C:\v\a.pdf")]
    [InlineData(@"C:\v\a.docx")]
    [InlineData(@"C:\v\mp4")]
    [InlineData(@"C:\v\a.mp4.txt")]
    public void OtherFiles_GetNoActions(string path)
    {
        Assert.Empty(ActionCatalog.ForFile(path, 80_000_000));
    }
}

public class SelectionInterpreterTests
{
    private sealed class FakeProbe(FileState state) : IFileProbe
    {
        public FileState Probe(string path) => state;
    }

    private static readonly IFileProbe File30Mb = new FakeProbe(new FileState(FileStateKind.File, 30_000_000));

    [Fact]
    public void SingleVideo_ShowsActions()
    {
        var outcome = SelectionInterpreter.Interpret(
            SelectionResult.Success([@"D:\Мои видео\отпуск 2024.mp4"]), File30Mb);

        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(outcome).Menu;
        Assert.Equal(DialMenuKind.Actions, menu.Kind);
        Assert.Equal("отпуск 2024.mp4", menu.Title);
        Assert.Equal("30 МБ", menu.Subtitle);
        Assert.Equal(8, menu.Actions.Count);
        Assert.Equal(@"D:\Мои видео\отпуск 2024.mp4", menu.SourcePath);
    }

    [Fact]
    public void SmallVideo_ShowsMenuWithCompressDisabled()
    {
        var outcome = SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\a\clip.mp4"]),
            new FakeProbe(new FileState(FileStateKind.File, 521_800)));

        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(outcome).Menu;
        Assert.Equal(DialMenuKind.Actions, menu.Kind);
        Assert.Equal("521,8 КБ", menu.Subtitle);
        Assert.DoesNotContain(menu.Actions, a => a.IsEnabled);
        Assert.Equal(ActionCatalog.AlreadySmall, menu.Actions[0].DisabledReason);
    }

    [Fact]
    public void OtherType_ShowsUnsupported()
    {
        var outcome = SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\a\doc.pdf"]), File30Mb);

        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(outcome).Menu;
        Assert.Equal(DialMenuKind.Unsupported, menu.Kind);
        Assert.Empty(menu.Actions);
        Assert.Contains("Пока не поддерживается", menu.Subtitle);
    }

    [Fact]
    public void SeveralFiles_ShowUnsupported_EvenIfAllVideos()
    {
        var outcome = SelectionInterpreter.Interpret(
            SelectionResult.Success([@"C:\a\1.mp4", @"C:\a\2.mp4"]), File30Mb);

        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(outcome).Menu;
        Assert.Equal(DialMenuKind.Unsupported, menu.Kind);
        Assert.Equal("Выделено объектов: 2", menu.Title);
        Assert.Equal("Пока не поддерживается", menu.Subtitle);
        Assert.Null(menu.SourcePath);
    }

    [Fact]
    public void Folder_ShowsUnsupported()
    {
        var outcome = SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\a\Видео.mp4"]),
            new FakeProbe(new FileState(FileStateKind.Directory)));

        var menu = Assert.IsType<HotkeyOutcome.ShowMenu>(outcome).Menu;
        Assert.Equal(DialMenuKind.Unsupported, menu.Kind);
    }

    [Fact]
    public void NothingSelected_ShowsToast()
    {
        var outcome = SelectionInterpreter.Interpret(SelectionResult.Success([]), File30Mb);
        Assert.Equal("Ничего не выделено", Assert.IsType<HotkeyOutcome.ShowToast>(outcome).Message);
    }

    [Fact]
    public void ReadError_ShowsToastWithReason()
    {
        var outcome = SelectionInterpreter.Interpret(SelectionResult.Fail("Активное окно — не Проводник"), File30Mb);
        Assert.Equal("Активное окно — не Проводник", Assert.IsType<HotkeyOutcome.ShowToast>(outcome).Message);
    }

    [Theory]
    [InlineData(FileStateKind.Missing)]
    [InlineData(FileStateKind.Unavailable)]
    public void UnavailableFile_ShowsToast(FileStateKind kind)
    {
        var outcome = SelectionInterpreter.Interpret(SelectionResult.Success([@"C:\a\1.mp4"]),
            new FakeProbe(new FileState(kind)));
        Assert.StartsWith("Файл недоступен", Assert.IsType<HotkeyOutcome.ShowToast>(outcome).Message);
    }
}

public class FileSystemProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FileDialTests", Guid.NewGuid().ToString("N"), "папка с пробелом");

    public FileSystemProbeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true);

    [Fact]
    public void ExistingFile_ReturnsSize()
    {
        var path = Path.Combine(_dir, "ролик.mp4");
        File.WriteAllBytes(path, new byte[1234]);
        Assert.Equal(new FileState(FileStateKind.File, 1234), new FileSystemProbe().Probe(path));
    }

    [Fact]
    public void Directory_And_Missing_And_Virtual()
    {
        var probe = new FileSystemProbe();
        Assert.Equal(FileStateKind.Directory, probe.Probe(_dir).Kind);
        Assert.Equal(FileStateKind.Missing, probe.Probe(Path.Combine(_dir, "нет.mp4")).Kind);
        Assert.Equal(FileStateKind.Missing, probe.Probe("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}").Kind);
    }

    [Fact]
    public void ExclusivelyLockedFile_IsUnavailable()
    {
        var path = Path.Combine(_dir, "locked.mp4");
        using var lockStream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(FileStateKind.Unavailable, new FileSystemProbe().Probe(path).Kind);
    }
}
