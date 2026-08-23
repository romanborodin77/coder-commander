using System.Runtime.InteropServices;

namespace CoderCommander.WinForms.Shell;

/// <summary>
/// Every raw shell32.dll P/Invoke declaration used by the shell-integration context menu items
/// (Open in Explorer, Windows Properties; the <c>IContextMenu</c> host reuses these too) in one
/// place - the same "one file, raw signatures only, no lifecycle" style
/// <c>Terminal/Native/ConPtyInterop.cs</c> already uses for ConPTY. All members are
/// <see langword="internal"/>: CA1401 (P/Invokes should not be visible) only fires on
/// <see langword="public"/> ones, and nothing outside <c>WinForms/Shell/</c> needs these directly.
/// <see cref="DllImportAttribute.CharSet"/> is <see cref="CharSet.Unicode"/> on every string-
/// bearing import (CA2101) - <c>[DefaultDllImportSearchPaths]</c> is not needed per import because
/// the assembly already declares it (<c>Properties/AssemblyInfo.cs</c>, CA5392).
/// </summary>
internal static class ShellNative
{
    // ── PIDL parsing / lifetime ──

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    internal static extern void ILFree(IntPtr pidl);

    /// <summary>Returns the last (child-relative) item ID within <paramref name="pidl"/> - an
    /// <b>interior pointer</b> into that same PIDL. Never free the result separately; it is only
    /// valid for as long as the PIDL it came from is alive.</summary>
    [DllImport("shell32.dll")]
    internal static extern IntPtr ILFindLastID(IntPtr pidl);

    // ── Open in Explorer ──

    [DllImport("shell32.dll")]
    internal static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, [In] IntPtr[]? apidl, uint dwFlags);

    // ── Windows Properties (ShellExecuteEx "properties" verb) ──

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShellExecuteExW(ref ShellExecuteInfo lpExecInfo);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    /// <summary>Loads the item's own registered property-sheet handlers, exactly what right-click
    /// ▸ Properties in Explorer does - without this, <c>lpFile</c> alone would fall back to a
    /// generic "open" launch instead of the properties sheet.</summary>
    internal const uint SeeMaskInvokeIdList = 0x0000000C;

    internal const int SwShowNormal = 1;
}
