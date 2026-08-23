using System.Runtime.InteropServices;

namespace CoderCommander.WinForms.Shell;

/// <summary>
/// Raw <c>[ComImport]</c> declarations for the shell COM interfaces the system-menu host needs.
///
/// <para><b>The method order in each interface is the ABI, not a stylistic choice.</b> COM
/// interface "inheritance" is vtable layout: <see cref="IContextMenu2"/> and
/// <see cref="IContextMenu3"/> below each redeclare every method of their real COM base interface,
/// in the exact published order, before their own new method - a plain C# <c>interface X : Y</c>
/// does <b>not</b> produce a compatible vtable under <c>[ComImport]</c>, so it is deliberately not
/// used here. Reordering or "cleaning up" any method list below silently calls the wrong function
/// pointer at runtime - no exception, just memory corruption - so do not touch the order.</para>
///
/// <para>Methods this host never actually calls (most of <see cref="IShellFolder"/>, and the
/// unused earlier methods each interface redeclares) are still fully spelled out for exactly that
/// vtable-layout reason, using <see cref="IntPtr"/> for every pointer/handle-shaped parameter -
/// the CLR's stdcall COM marshaling only cares about parameter count and pointer-size slots for a
/// method never invoked from managed code, not its C# type.</para>
/// </summary>
[ComImport]
[Guid("000214E6-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
    void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
    void BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
    void BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
    void CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
    /// <summary>Used for the folder-level (background) system menu: <c>riid</c> =
    /// <c>IID_IContextMenu</c> gives the folder's own "New ▸ / Paste" context menu, the same one
    /// Explorer shows for a right-click on empty space inside the folder.</summary>
    void CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
    void GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);
    /// <summary>Used for the item-level system menu: <c>riid</c> = <c>IID_IContextMenu</c> gives
    /// the context menu for the child items in <c>apidl</c> (all relative to this folder).</summary>
    void GetUIObjectOf(IntPtr hwndOwner, uint cidl, IntPtr apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
    void GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
    void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
}

[ComImport]
[Guid("000214E4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu
{
    void QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    void InvokeCommand(ref CmInvokeCommandInfoEx pici);
    void GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
}

/// <summary>
/// Adds <c>HandleMenuMsg</c> - required to forward <c>WM_INITMENUPOPUP</c>/<c>WM_DRAWITEM</c>/
/// <c>WM_MEASUREITEM</c> to an owner-drawn shell extension (7-Zip, TortoiseGit); without this, such
/// a submenu renders blank instead of showing its own icons/labels. <c>HandleMenuMsg</c> itself is
/// declared <c>[PreserveSig]</c> returning a raw HRESULT rather than the auto-throwing <c>void</c>
/// style the other methods here use - it fires on every relevant window message while the menu is
/// open (including ordinary mouse movement over an owner-drawn item), and many of those calls
/// legitimately return a non-success HRESULT for a message the extension doesn't care about; that
/// is normal, not a fault to surface as an exception on every mouse move.
/// </summary>
[ComImport]
[Guid("000214F4-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu2
{
    void QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    void InvokeCommand(ref CmInvokeCommandInfoEx pici);
    void GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    [PreserveSig]
    int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
}

/// <summary>Adds <c>HandleMenuMsg2</c> over <see cref="IContextMenu2"/> - additionally handles
/// <c>WM_MENUCHAR</c> (owner-drawn mnemonic keys), which <c>IContextMenu2.HandleMenuMsg</c> cannot.
/// Preferred over <see cref="IContextMenu2"/> whenever an extension supports it (Windows' own
/// built-in "Open with" handler is <see cref="IContextMenu3"/>; many third-party ones, including
/// 7-Zip, are still only <see cref="IContextMenu2"/>) - the host queries for both and prefers this
/// one.</summary>
[ComImport]
[Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IContextMenu3
{
    void QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
    void InvokeCommand(ref CmInvokeCommandInfoEx pici);
    void GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    [PreserveSig]
    int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    [PreserveSig]
    int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
}

/// <summary>
/// <c>CMINVOKECOMMANDINFOEX</c> - passed to <see cref="IContextMenu.InvokeCommand"/>. This host
/// only ever invokes by <b>numeric offset</b> (the id <c>TrackPopupMenuEx</c> returned, relative to
/// <c>idCmdFirst</c>), never a string verb, so <see cref="lpVerb"/> is deliberately
/// <see cref="IntPtr"/> rather than a marshaled string: the shell convention for "this is a numeric
/// command, not a verb string" is a pointer value small enough to be a 16-bit integer
/// (<c>MAKEINTRESOURCE</c>-style), which requires placing the raw integer in the pointer slot
/// itself - marshaling an actual string here would defeat that entirely.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CmInvokeCommandInfoEx
{
    public int cbSize;
    public uint fMask;
    public IntPtr hwnd;
    public IntPtr lpVerb;
    public IntPtr lpParameters;
    public IntPtr lpDirectory;
    public int nShow;
    public uint dwHotKey;
    public IntPtr hIcon;
    public IntPtr lpTitle;
    public IntPtr lpVerbW;
    public IntPtr lpParametersW;
    public IntPtr lpDirectoryW;
    public IntPtr lpTitleW;
    public int ptInvokeX;
    public int ptInvokeY;
}

/// <summary>Well-known shell COM <c>IID</c>s and <c>CMF_</c>/window-message constants the host
/// needs, gathered in one place next to the interfaces they belong to.</summary>
internal static class ShellIids
{
    public static readonly Guid IidIShellFolder = new("000214E6-0000-0000-C000-000000000046");
    public static readonly Guid IidIContextMenu = new("000214E4-0000-0000-C000-000000000046");
}

internal static class ShellMenuConstants
{
    // CMF_* - flags for IContextMenu.QueryContextMenu.
    public const uint CmfNormal = 0x00000000;
    public const uint CmfExplore = 0x00000004;
    public const uint CmfExtendedVerbs = 0x00000100;

    // TPM_* - flags for TrackPopupMenuEx.
    public const uint TpmLeftAlign = 0x0000;
    public const uint TpmRightButton = 0x0002;
    public const uint TpmReturnCmd = 0x0100;

    // Window messages IContextMenu2/3.HandleMenuMsg(2) must see to render an owner-drawn submenu.
    public const uint WmInitMenuPopup = 0x0117;
    public const uint WmDrawItem = 0x002B;
    public const uint WmMeasureItem = 0x002C;
    public const uint WmMenuChar = 0x0120;
    public const uint WmNull = 0x0000;
}
