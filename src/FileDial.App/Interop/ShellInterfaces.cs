using System.Runtime.InteropServices;

namespace FileDial.App.Interop;

// Минимальные COM-описания интерфейсов оболочки. Порядок методов совпадает с vtable в SDK (shobjidl.h, exdisp.h);
// неиспользуемые методы объявлены заглушками с IntPtr, чтобы сохранить смещения.

internal static class ShellGuids
{
    public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    public static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    public static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");
    public static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");

    public const int SWC_DESKTOP = 8;
    public const int SWFO_NEEDDISPATCH = 1;
    public const int CSIDL_DESKTOP = 0;
    public const uint SVGIO_BACKGROUND = 0;
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppvObject);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowser
{
    // IOleWindow
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
    // IShellBrowser
    [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
    [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
    [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
    [PreserveSig] int SetStatusTextSB(IntPtr pszStatusText);
    [PreserveSig] int EnableModelessSB(int fEnable);
    [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
    [PreserveSig] int BrowseObject(IntPtr pidl, uint wFlags);
    [PreserveSig] int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
    [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
    [PreserveSig] int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
    [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView? ppshv);
}

[ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellView
{
    // IOleWindow
    [PreserveSig] int GetWindow(out IntPtr phwnd);
    [PreserveSig] int ContextSensitiveHelp(int fEnterMode);
    // IShellView
    [PreserveSig] int TranslateAccelerator(IntPtr pmsg);
    [PreserveSig] int EnableModeless(int fEnable);
    [PreserveSig] int UIActivate(uint uState);
    [PreserveSig] int Refresh();
    [PreserveSig] int CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb, IntPtr prcView, out IntPtr phWnd);
    [PreserveSig] int DestroyViewWindow();
    [PreserveSig] int GetCurrentInfo(IntPtr pfs);
    [PreserveSig] int AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
    [PreserveSig] int SaveViewState();
    [PreserveSig] int SelectItem(IntPtr pidlItem, uint uFlags);
    [PreserveSig] int GetItemObject(uint uItem, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? ppv);
}

[ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IShellWindows
{
    [PreserveSig] int get_Count(out int count);
    [PreserveSig] int Item([MarshalAs(UnmanagedType.Struct)] object index, [MarshalAs(UnmanagedType.IDispatch)] out object? folder);
    [PreserveSig] int _NewEnum([MarshalAs(UnmanagedType.IUnknown)] out object? ppunk);
    [PreserveSig] int Register([MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int plCookie);
    [PreserveSig] int RegisterPending(int lThreadId, IntPtr pvarloc, IntPtr pvarlocRoot, int swClass, out int plCookie);
    [PreserveSig] int Revoke(int lCookie);
    [PreserveSig] int OnNavigate(int lCookie, IntPtr pvarLoc);
    [PreserveSig] int OnActivated(int lCookie, short fActive);
    [PreserveSig]
    int FindWindowSW(
        [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
        [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarLocRoot,
        int swClass,
        out int phwnd,
        int swfwOptions,
        [MarshalAs(UnmanagedType.IDispatch)] out object? ppdispOut);
}
