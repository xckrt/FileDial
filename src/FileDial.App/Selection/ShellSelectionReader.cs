using FileDial.Core.Localization;
using System.Runtime.InteropServices;
using FileDial.App.Interop;
using FileDial.Core.Selection;
using IServiceProvider = FileDial.App.Interop.IServiceProvider;

namespace FileDial.App.Selection;

/// <summary>
/// Читает выделение из активного окна Проводника или с рабочего стола через COM оболочки.
/// Должен вызываться в STA-потоке. Ничего не меняет в выделении и не трогает буфер обмена.
/// </summary>
internal static class ShellSelectionReader
{
    public static SelectionResult Read(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero)
            return SelectionResult.Fail(Lang.T.ShellNoActiveWindow);

        var className = NativeMethods.GetClassName(foreground);
        var kind = ClassifyWindow(foreground, className);
        if (kind == WindowKind.Other)
            return SelectionResult.Fail(Lang.T.ShellNotExplorer);

        if (ProcessElevation.IsWindowElevatedAboveUs(foreground))
            return SelectionResult.Fail(Lang.T.ShellElevatedWindow);

        try
        {
            return kind == WindowKind.Explorer ? ReadExplorer(foreground) : ReadDesktop();
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            return SelectionResult.Fail(Lang.T.ShellNoExplorerAccess);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Чтение выделения", ex);
            return SelectionResult.Fail(Lang.T.ShellSelectionReadFailed);
        }
    }

    private enum WindowKind { Other, Explorer, Desktop }

    private static WindowKind ClassifyWindow(IntPtr hwnd, string className)
    {
        if (className == "CabinetWClass")
            return WindowKind.Explorer;
        if (className == "Progman")
            return WindowKind.Desktop;
        // WorkerW бывает разным; рабочий стол — тот, в котором лежит SHELLDLL_DefView.
        if (className == "WorkerW" && NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            return WindowKind.Desktop;
        return WindowKind.Other;
    }

    private static SelectionResult ReadExplorer(IntPtr explorerHwnd)
    {
        using var com = new ComScope();
        var shellType = Type.GetTypeFromProgID("Shell.Application")
                        ?? throw new InvalidOperationException("Shell.Application недоступен");
        dynamic shell = com.Track(Activator.CreateInstance(shellType))!;
        dynamic windows = com.Track((object)shell.Windows())!;

        // В Windows 11 все вкладки окна имеют общий HWND; активная вкладка — видимый ShellTabWindowClass.
        var activeTab = FindActiveTab(explorerHwnd);
        var sameHwnd = 0;
        object? match = null;

        int count = windows.Count;
        for (var i = 0; i < count; i++)
        {
            object? window;
            try
            {
                window = com.Track((object?)windows.Item(i));
            }
            catch (COMException)
            {
                continue; // окно закрылось во время перебора
            }
            if (window is null)
                continue;

            long hwnd;
            try
            {
                hwnd = Convert.ToInt64(((dynamic)window).HWND);
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                continue;
            }
            if (hwnd != explorerHwnd.ToInt64())
                continue;

            sameHwnd++;
            if (activeTab == IntPtr.Zero)
            {
                match = window;
                continue;
            }

            if (GetTabWindow(window, com) == activeTab)
            {
                match = window;
                break;
            }
        }

        if (sameHwnd == 0)
            return SelectionResult.Fail(Lang.T.ShellExplorerWindowNotFound);
        // Без вкладок (Windows 10) подходит единственный кандидат; иначе — только активная вкладка.
        if (match is null || (activeTab == IntPtr.Zero && sameHwnd > 1))
            return SelectionResult.Fail(Lang.T.ShellNoActiveTab);

        object document = com.Track((object?)((dynamic)match).Document)
                           ?? throw new InvalidOperationException("У вкладки нет документа");
        return SelectionResult.Success(ReadSelectedItems(document, com));
    }

    private static SelectionResult ReadDesktop()
    {
        using var com = new ComScope();
        var shellWindowsType = Type.GetTypeFromCLSID(ShellGuids.CLSID_ShellWindows)!;
        var shellWindows = (IShellWindows)com.Track(Activator.CreateInstance(shellWindowsType))!;

        object location = ShellGuids.CSIDL_DESKTOP;
        object? root = null;
        var hr = shellWindows.FindWindowSW(ref location, ref root, ShellGuids.SWC_DESKTOP, out _,
            ShellGuids.SWFO_NEEDDISPATCH, out var desktopDispatch);
        com.Track(desktopDispatch);
        if (hr != 0 || desktopDispatch is null)
            return SelectionResult.Fail(Lang.T.ShellDesktopUnavailable);

        var browser = QueryTopLevelBrowser(desktopDispatch, com)
                      ?? throw new InvalidOperationException("IShellBrowser рабочего стола недоступен");
        Marshal.ThrowExceptionForHR(browser.QueryActiveShellView(out var view));
        com.Track(view);

        var iidDispatch = ShellGuids.IID_IDispatch;
        Marshal.ThrowExceptionForHR(view!.GetItemObject(ShellGuids.SVGIO_BACKGROUND, ref iidDispatch, out var folderView));
        com.Track(folderView);

        return SelectionResult.Success(ReadSelectedItems(folderView!, com));
    }

    private static List<string> ReadSelectedItems(object folderViewObject, ComScope com)
    {
        dynamic folderView = folderViewObject;
        var result = new List<string>();
        dynamic? items = com.Track((object?)folderView.SelectedItems());
        if (items is null)
            return result;

        int count = items.Count;
        for (var i = 0; i < count; i++)
        {
            dynamic? item = com.Track((object?)items.Item(i));
            if (item is null)
                continue;
            string? path = item.Path;
            if (!string.IsNullOrEmpty(path))
                result.Add(path);
        }
        return result;
    }

    private static IntPtr FindActiveTab(IntPtr explorerHwnd)
    {
        IntPtr first = IntPtr.Zero, tab = IntPtr.Zero;
        while ((tab = NativeMethods.FindWindowEx(explorerHwnd, tab, "ShellTabWindowClass", null)) != IntPtr.Zero)
        {
            if (first == IntPtr.Zero)
                first = tab;
            if (NativeMethods.IsWindowVisible(tab))
                return tab;
        }
        // Если видимость не помогла, верхняя по Z-порядку вкладка — активная.
        return first;
    }

    private static IntPtr GetTabWindow(object window, ComScope com)
    {
        var browser = QueryTopLevelBrowser(window, com);
        if (browser is null)
            return IntPtr.Zero;
        return browser.GetWindow(out var hwnd) == 0 ? hwnd : IntPtr.Zero;
    }

    private static IShellBrowser? QueryTopLevelBrowser(object comObject, ComScope com)
    {
        if (comObject is not IServiceProvider provider)
            return null;
        var sid = ShellGuids.SID_STopLevelBrowser;
        var iid = ShellGuids.IID_IShellBrowser;
        if (provider.QueryService(ref sid, ref iid, out var obj) != 0 || obj is null)
            return null;
        com.Track(obj);
        return obj as IShellBrowser;
    }
}
