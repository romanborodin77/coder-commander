using System.Diagnostics;
using System.Runtime.InteropServices;
using CoderCommander.Services;

namespace CoderCommander.WinForms.Shell;

/// <summary>
/// "Open in Explorer" and native Windows Properties, both driven directly through shell32 rather
/// than shelling out to <c>explorer.exe /select,"..."</c> - that verb's own quoting is notoriously
/// broken for a path containing a comma or leading/trailing space, and there is no safe way to
/// build it with this codebase's own <c>Win32ArgumentQuoting.Quote</c> (built for a plain command
/// line, not Explorer's undocumented comma-separated form).
///
/// <para>Every path handed to these methods must already be a real Windows path (the result of
/// <c>IFileSystem.GetShellPath</c>), never a VFS one - callers gate on that before reaching here.</para>
/// </summary>
internal static class ExplorerHelper
{
    /// <summary>One Explorer window per distinct parent folder is opened when a selection spans
    /// more than one (only possible in Flat View); capped so a huge cross-folder selection can't
    /// flood the desktop with windows.</summary>
    private const int MaxWindows = 4;

    /// <summary>Opens Explorer with <paramref name="shellPaths"/> selected, grouped by parent
    /// folder - <see cref="ShellNative.SHOpenFolderAndSelectItems"/> requires every selected item
    /// in one call to share the same parent, which a Flat View selection isn't guaranteed to.</summary>
    public static void OpenAndSelect(IReadOnlyList<string> shellPaths)
    {
        if (shellPaths.Count == 0) return;

        var groups = shellPaths
            .GroupBy(p => Path.GetDirectoryName(p) ?? p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var opened = 0;
        foreach (var group in groups)
        {
            if (opened >= MaxWindows)
            {
                LogService.Warning($"Open in Explorer: {groups.Count - opened} additional folder group(s) not opened (past the {MaxWindows}-window cap)");
                break;
            }
            OpenAndSelectOneFolder(group.Key, group.ToList());
            opened++;
        }
    }

    /// <summary>Opens Explorer at <paramref name="shellFolder"/> itself, nothing selected - the
    /// background (empty-space) context menu's "Open in Explorer" and the folder-target case.</summary>
    public static void OpenFolder(string shellFolder)
    {
        using var folderPidl = ParseDisplayName(shellFolder);
        if (folderPidl == null)
        {
            OpenFolderFallback(shellFolder);
            return;
        }

        var hr = ShellNative.SHOpenFolderAndSelectItems(folderPidl.DangerousGetHandle(), 0, null, 0);
        if (hr != 0)
            LogService.Warning($"SHOpenFolderAndSelectItems failed for {shellFolder}: HRESULT 0x{hr:X8}");
    }

    private static void OpenAndSelectOneFolder(string folder, IReadOnlyList<string> items)
    {
        using var folderPidl = ParseDisplayName(folder);
        if (folderPidl == null)
        {
            OpenFolderFallback(folder);
            return;
        }

        // Child PIDLs are interior pointers into their own absolute PIDL (ILFindLastID never
        // allocates), so each absolute PIDL must stay alive - and therefore disposed - for exactly
        // as long as the apidl array built from it is in use.
        var ownedPidls = new List<SafePidlHandle>();
        try
        {
            var apidl = new List<IntPtr>();
            foreach (var item in items)
            {
                var itemPidl = ParseDisplayName(item);
                if (itemPidl == null) continue;
                ownedPidls.Add(itemPidl);
                apidl.Add(ShellNative.ILFindLastID(itemPidl.DangerousGetHandle()));
            }

            var hr = apidl.Count > 0
                ? ShellNative.SHOpenFolderAndSelectItems(folderPidl.DangerousGetHandle(), (uint)apidl.Count, apidl.ToArray(), 0)
                : ShellNative.SHOpenFolderAndSelectItems(folderPidl.DangerousGetHandle(), 0, null, 0);
            if (hr != 0)
                LogService.Warning($"SHOpenFolderAndSelectItems failed for {folder}: HRESULT 0x{hr:X8}");
        }
        finally
        {
            foreach (var p in ownedPidls) p.Dispose();
        }
    }

    /// <summary>Falls back to plainly opening the folder in Explorer when
    /// <c>SHParseDisplayName</c> can't resolve it (e.g. an SMB share that just went offline) -
    /// still better than silently doing nothing.</summary>
    private static void OpenFolderFallback(string folder)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Error($"Open in Explorer fallback failed for {folder}: {ex.Message}", ex);
        }
    }

    private static SafePidlHandle? ParseDisplayName(string path)
    {
        var hr = ShellNative.SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _);
        return hr == 0 && pidl != IntPtr.Zero ? new SafePidlHandle(pidl) : null;
    }

    /// <summary>Opens the native Windows Properties sheet for one item - <c>SEE_MASK_INVOKEIDLIST</c>
    /// loads its registered property-sheet handlers (the same ones Explorer's own right-click ▸
    /// Properties would), not just a generic file-open fallback. Modeless: returns as soon as the
    /// sheet is requested, does not block on it closing.</summary>
    public static void ShowProperties(IntPtr ownerHwnd, string shellPath)
    {
        var info = new ShellNative.ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellNative.ShellExecuteInfo>(),
            fMask = ShellNative.SeeMaskInvokeIdList,
            hwnd = ownerHwnd,
            lpVerb = "properties",
            lpFile = shellPath,
            nShow = ShellNative.SwShowNormal
        };

        if (!ShellNative.ShellExecuteExW(ref info))
        {
            var error = Marshal.GetLastWin32Error();
            LogService.Error($"Windows Properties failed for {shellPath}: Win32 error {error}");
        }
    }
}
