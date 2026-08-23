using System.Runtime.InteropServices;
using CoderCommander.Services;

namespace CoderCommander.WinForms.Shell;

/// <summary>
/// Shows the real Windows shell context menu - the one with every installed extension (7-Zip,
/// Git, antivirus, "Open in Code") in it, not a reimplementation of it. Two entry points:
/// <see cref="Show"/> for a set of items (via <c>IShellFolder.GetUIObjectOf</c>) and
/// <see cref="ShowForFolder"/> for a folder's own background menu (via
/// <c>IShellFolder.CreateViewObject</c> on the folder itself - the one that includes "New ▸" and
/// the shell's own "Paste", the same as right-clicking empty space inside it in Explorer).
///
/// <para><b>Why this always gets the classic, full menu on Windows 11.</b> Calling
/// <c>IContextMenu::QueryContextMenu</c> directly is the same mechanism Explorer's own "Show more
/// options" falls back to - Windows 11's compact menu is produced by a separate
/// <c>IExplorerCommand</c>-based path inside <c>explorer.exe</c>'s own process that a third-party
/// host has no access to (and no reason to want). This is a property of the approach, not a
/// limitation to "fix" later.</para>
///
/// <para><b>The headline risk.</b> Every call from <see cref="Show"/> onward runs arbitrary
/// third-party in-process code (a buggy shell extension can crash or hang). Both entry points
/// therefore wrap their entire body in <c>try/catch (Exception)</c> - a rare deliberately-broad
/// catch, appropriate here for the same reason a plugin host would use one - log, tell the user,
/// and let the rest of the app's own menu keep working regardless.</para>
/// </summary>
internal static class ShellContextMenuHost
{
    private const uint IdCmdFirst = 1;
    private const uint IdCmdLast = 0x7FFF;

    /// <summary>Shows the shell context menu for a set of items that must all share one parent
    /// folder (the caller - <c>FilePanelUserControl</c>'s "Windows menu…" gating - is responsible
    /// for checking that and disabling the menu item otherwise; this defends anyway by only using
    /// the first item's parent).</summary>
    public static void Show(Control owner, IReadOnlyList<string> shellPaths, Point screenPoint, bool extendedVerbs)
    {
        if (shellPaths.Count == 0) return;
        try
        {
            ShowItemsCore(owner, shellPaths, screenPoint, extendedVerbs);
        }
        catch (Exception ex)
        {
            ReportFailure(owner, shellPaths[0], ex);
        }
    }

    /// <summary>Shows the shell context menu for a folder itself - "New ▸", the shell's own
    /// "Paste", and folder Properties, the same set Explorer shows for a right-click on empty
    /// space inside it.</summary>
    public static void ShowForFolder(Control owner, string folderShellPath, Point screenPoint, bool extendedVerbs)
    {
        try
        {
            ShowFolderCore(owner, folderShellPath, screenPoint, extendedVerbs);
        }
        catch (Exception ex)
        {
            ReportFailure(owner, folderShellPath, ex);
        }
    }

    private static void ReportFailure(Control owner, string path, Exception ex)
    {
        LogService.Error($"Windows system menu failed for {path}: {ex.Message}", ex);
        var l = LocalizationService.Current;
        StyledMessageBox.Show(l.GetString("Shell.MenuFailed"), l.GetString("Common.Error"),
            MsgBoxButtons.OK, MsgBoxIcon.Error, owner.FindForm());
    }

    private static void ShowItemsCore(Control owner, IReadOnlyList<string> shellPaths, Point screenPoint, bool extendedVerbs)
    {
        var pidls = new List<SafePidlHandle>();
        try
        {
            foreach (var path in shellPaths)
            {
                var pidl = ShellPaths.ParseDisplayName(path);
                if (pidl == null)
                {
                    LogService.Warning($"System menu: could not resolve {path}");
                    continue;
                }
                pidls.Add(pidl);
            }
            if (pidls.Count == 0) return;

            var riidShellFolder = ShellIids.IidIShellFolder;
            var hr = ShellNative.SHBindToParent(pidls[0].DangerousGetHandle(), ref riidShellFolder, out var parentPtr, out var firstChildPidl);
            if (hr != 0 || parentPtr == IntPtr.Zero)
            {
                LogService.Warning($"System menu: SHBindToParent failed (HRESULT 0x{hr:X8})");
                return;
            }

            IShellFolder? parentFolder = null;
            IContextMenu? contextMenu = null;
            try
            {
                parentFolder = (IShellFolder)Marshal.GetObjectForIUnknown(parentPtr);
                Marshal.Release(parentPtr);

                // apidl is an array of CHILD-relative PIDLs, one per item, all relative to
                // parentFolder - pidls[0]'s own child id is the interior pointer SHBindToParent
                // already returned; the rest come from ILFindLastID on their own absolute PIDL.
                // Every entry here is an interior pointer into one of `pidls` above - never freed
                // on its own, and `pidls` must outlive this whole call.
                var apidl = new IntPtr[pidls.Count];
                apidl[0] = firstChildPidl;
                for (var i = 1; i < pidls.Count; i++)
                    apidl[i] = ShellNative.ILFindLastID(pidls[i].DangerousGetHandle());

                var pinned = GCHandle.Alloc(apidl, GCHandleType.Pinned);
                IntPtr contextMenuPtr;
                try
                {
                    var riidContextMenu = ShellIids.IidIContextMenu;
                    parentFolder.GetUIObjectOf(owner.Handle, (uint)apidl.Length, pinned.AddrOfPinnedObject(),
                        ref riidContextMenu, IntPtr.Zero, out contextMenuPtr);
                }
                finally
                {
                    pinned.Free();
                }
                if (contextMenuPtr == IntPtr.Zero) return;

                contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
                Marshal.Release(contextMenuPtr);

                RunMenu(owner, contextMenu, screenPoint, extendedVerbs);
            }
            finally
            {
                if (contextMenu != null) Marshal.FinalReleaseComObject(contextMenu);
                if (parentFolder != null) Marshal.FinalReleaseComObject(parentFolder);
            }
        }
        finally
        {
            foreach (var pidl in pidls) pidl.Dispose();
        }
    }

    private static void ShowFolderCore(Control owner, string folderShellPath, Point screenPoint, bool extendedVerbs)
    {
        using var pidl = ShellPaths.ParseDisplayName(folderShellPath);
        if (pidl == null) return;

        var hrDesktop = ShellNative.SHGetDesktopFolder(out var desktopPtr);
        if (hrDesktop != 0 || desktopPtr == IntPtr.Zero) return;

        IShellFolder? desktop = null;
        IShellFolder? folder = null;
        IContextMenu? contextMenu = null;
        try
        {
            desktop = (IShellFolder)Marshal.GetObjectForIUnknown(desktopPtr);
            Marshal.Release(desktopPtr);

            // SHParseDisplayName's PIDL is already relative to the desktop (the root of the shell
            // namespace), so binding it straight off the desktop folder resolves the target
            // folder's own IShellFolder with no further walking.
            var riidShellFolder = ShellIids.IidIShellFolder;
            desktop.BindToObject(pidl.DangerousGetHandle(), IntPtr.Zero, ref riidShellFolder, out var folderPtr);
            if (folderPtr == IntPtr.Zero) return;
            folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            Marshal.Release(folderPtr);

            var riidContextMenu = ShellIids.IidIContextMenu;
            folder.CreateViewObject(owner.Handle, ref riidContextMenu, out var contextMenuPtr);
            if (contextMenuPtr == IntPtr.Zero) return;
            contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(contextMenuPtr);
            Marshal.Release(contextMenuPtr);

            RunMenu(owner, contextMenu, screenPoint, extendedVerbs);
        }
        finally
        {
            if (contextMenu != null) Marshal.FinalReleaseComObject(contextMenu);
            if (folder != null) Marshal.FinalReleaseComObject(folder);
            if (desktop != null) Marshal.FinalReleaseComObject(desktop);
        }
    }

    /// <summary>The part shared by both entry points once an <see cref="IContextMenu"/> has been
    /// obtained: build the native popup, track it, invoke whatever the user picked. Teardown order
    /// is deliberate - <c>DestroyMenu</c> before the message window is disposed, both before the
    /// caller releases the COM objects (see the class doc comment).</summary>
    private static void RunMenu(Control owner, IContextMenu contextMenu, Point screenPoint, bool extendedVerbs)
    {
        var contextMenu2 = contextMenu as IContextMenu2;
        var contextMenu3 = contextMenu as IContextMenu3;

        using var msgWindow = new ShellMenuMessageWindow(owner.Handle);
        msgWindow.Attach(contextMenu2, contextMenu3);

        var hmenu = ShellNative.CreatePopupMenu();
        if (hmenu == IntPtr.Zero) return;
        try
        {
            var flags = ShellMenuConstants.CmfNormal | ShellMenuConstants.CmfExplore
                | (extendedVerbs ? ShellMenuConstants.CmfExtendedVerbs : 0);
            contextMenu.QueryContextMenu(hmenu, 0, IdCmdFirst, IdCmdLast, flags);

            // TrackPopupMenuEx runs its own modal message loop; SetForegroundWindow first is what
            // lets it dismiss correctly on a click outside the menu, and the WM_NULL afterward is
            // the documented workaround for the menu not fully closing without it.
            ShellNative.SetForegroundWindow(msgWindow.Handle);
            var id = ShellNative.TrackPopupMenuEx(hmenu,
                ShellMenuConstants.TpmReturnCmd | ShellMenuConstants.TpmLeftAlign | ShellMenuConstants.TpmRightButton,
                screenPoint.X, screenPoint.Y, msgWindow.Handle, IntPtr.Zero);
            ShellNative.PostMessage(msgWindow.Handle, ShellMenuConstants.WmNull, IntPtr.Zero, IntPtr.Zero);

            if (id == 0) return; // user dismissed the menu - not a failure

            var info = new CmInvokeCommandInfoEx
            {
                cbSize = Marshal.SizeOf<CmInvokeCommandInfoEx>(),
                fMask = 0,
                hwnd = owner.Handle,
                // A numeric command offset, not a string verb - see CmInvokeCommandInfoEx's own
                // doc comment on why this must stay a raw IntPtr rather than a marshaled string.
                // id is always > 0 here (the id == 0 "dismissed" case already returned above) and
                // idCmdFirst is a small constant, so this can never actually overflow - checked
                // only to satisfy CA2020's post-.NET 7 default.
                lpVerb = checked((IntPtr)(id - IdCmdFirst)),
                nShow = ShellNative.SwShowNormal
            };
            contextMenu.InvokeCommand(ref info);
        }
        finally
        {
            ShellNative.DestroyMenu(hmenu);
        }
    }
}
