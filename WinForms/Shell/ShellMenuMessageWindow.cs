namespace CoderCommander.WinForms.Shell;

/// <summary>
/// The window <c>TrackPopupMenuEx</c> is given as its owner, and the piece that actually makes
/// owner-drawn shell extension submenus (7-Zip, TortoiseGit) render at all.
///
/// <para><b>Why a dedicated <see cref="NativeWindow"/>, not <see cref="IMessageFilter"/> or the
/// panel's own <c>WndProc</c>.</b> <c>WM_INITMENUPOPUP</c>/<c>WM_DRAWITEM</c>/<c>WM_MEASUREITEM</c>/
/// <c>WM_MENUCHAR</c> are <b>sent</b> by USER32 straight into the owner window's <c>WndProc</c>
/// from inside <c>TrackPopupMenuEx</c>'s own modal loop - they never pass through
/// <c>Application.DoEvents</c>/<c>GetMessage</c>, so an <see cref="IMessageFilter"/> (which only
/// sees messages pulled off the queue) can never observe them; this is exactly why a naive
/// implementation renders an owner-drawn submenu blank. The panel's own <c>WndProc</c> is not a
/// safe alternative either: <c>FilePanelUserControl</c> owner-draws its own <c>ListView</c>, and
/// claiming <c>WM_DRAWITEM</c>/<c>WM_MEASUREITEM</c> there risks colliding with that; it can also
/// be re-pointed at a different <c>PanelViewModel</c> (tab switching) while a native menu is open.
/// A small, throwaway, purpose-built window sidesteps both.</para>
///
/// <para><b>Why <c>WS_POPUP</c>, not a message-only (<c>HWND_MESSAGE</c>) window.</b> A
/// message-only window can never become the foreground window - and <c>TrackPopupMenuEx</c> needs
/// an owner that can, or the menu will not dismiss correctly on an outside click and the modal
/// loop can hang. This window is a real (if invisible, zero-size) popup owned by
/// <paramref name="ownerHwnd"/>, not a true child - a popup can be given an owner via
/// <see cref="CreateParams.Parent"/> without being one.</para>
/// </summary>
internal sealed class ShellMenuMessageWindow : NativeWindow, IDisposable
{
    private const int WsPopup = unchecked((int)0x80000000);

    private IContextMenu2? _contextMenu2;
    private IContextMenu3? _contextMenu3;

    public ShellMenuMessageWindow(IntPtr ownerHwnd)
    {
        CreateHandle(new CreateParams
        {
            Parent = ownerHwnd,
            Style = WsPopup,
            Width = 0,
            Height = 0
        });
    }

    /// <summary>Sets which interface (if any) this window forwards owner-draw messages to for the
    /// menu currently being shown - <see langword="null"/>/<see langword="null"/> once it closes.
    /// <paramref name="contextMenu3"/> is preferred when both are available (see
    /// <see cref="IContextMenu3"/>'s own doc comment on why).</summary>
    public void Attach(IContextMenu2? contextMenu2, IContextMenu3? contextMenu3)
    {
        _contextMenu2 = contextMenu2;
        _contextMenu3 = contextMenu3;
    }

    protected override void WndProc(ref Message m)
    {
        switch ((uint)m.Msg)
        {
            case ShellMenuConstants.WmInitMenuPopup:
            case ShellMenuConstants.WmDrawItem:
            case ShellMenuConstants.WmMeasureItem:
            case ShellMenuConstants.WmMenuChar:
                if (_contextMenu3 != null)
                {
                    // Non-S_OK here is routine (the extension declining a message it doesn't
                    // care about), not a fault - deliberately not thrown or logged per call.
                    _contextMenu3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var result);
                    m.Result = result;
                    return;
                }
                if (_contextMenu2 != null)
                {
                    _contextMenu2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam);
                    // HandleMenuMsg (unlike HandleMenuMsg2) has no out-result parameter; WM_MEASUREITEM
                    // still needs *some* non-zero handled signal or the shell falls back to a default
                    // (often zero) size for the item.
                    m.Result = (uint)m.Msg == ShellMenuConstants.WmInitMenuPopup ? IntPtr.Zero : (IntPtr)1;
                    return;
                }
                break;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        _contextMenu2 = null;
        _contextMenu3 = null;
        if (Handle != IntPtr.Zero)
            DestroyHandle();
    }
}
