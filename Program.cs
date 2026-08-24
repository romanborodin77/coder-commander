using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.FileSystem.Remote;
using CoderCommander.FileSystem.Remote.Ftp;
using CoderCommander.FileSystem.Remote.Sftp;
using CoderCommander.FileSystem.Remote.Smb;
using CoderCommander.Archives.SharpCompress;
using CoderCommander.Archives.Tar;
using CoderCommander.Archives.Zip;
using CoderCommander.Services;
using CoderCommander.WinForms;
using CoderCommander.ViewModels;
using CoderCommander.Views;
using CoderCommander.Viewers;
using CoderCommander.Viewers.Formats;
using System.Text;

namespace CoderCommander;

/// <summary>
/// WinForms application entry point.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        ArchiveFormatRegistry.Register(ZipArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarGzArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(SevenZipArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(RarArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarBz2ArchiveFormat.Instance);
        ArchiveFormatRegistry.Register(TarXzArchiveFormat.Instance);

        FileSystemProviderRegistry.Register(WebDavProvider.Instance);
        FileSystemProviderRegistry.Register(FtpProvider.Instance);
        FileSystemProviderRegistry.Register(SftpProvider.Instance);
        FileSystemProviderRegistry.Register(SmbProvider.Instance);

        // Universal formats first (registration order = toolbar button order), then matched
        // formats - see ViewerFormatRegistry's own doc comment for the extension/signature
        // detection rules this feeds.
        ViewerFormatRegistry.Register(TextViewerFormat.Instance);
        ViewerFormatRegistry.Register(AsciiViewerFormat.Instance);
        ViewerFormatRegistry.Register(BinaryViewerFormat.Instance);
        ViewerFormatRegistry.Register(HexViewerFormat.Instance);
        ViewerFormatRegistry.Register(ImageViewerFormat.Instance);
        ViewerFormatRegistry.Register(CsvViewerFormat.Instance);
        ViewerFormatRegistry.Register(MarkdownViewerFormat.Instance);
        ViewerFormatRegistry.Register(HtmlViewerFormat.Instance);
        ViewerFormatRegistry.Register(PdfViewerFormat.Instance);
        ViewerFormatRegistry.Register(MediaViewerFormat.Instance);
        ViewerFormatRegistry.Register(OfficeWordViewerFormat.Instance);
        ViewerFormatRegistry.Register(OfficeSheetViewerFormat.Instance);
        ViewerFormatRegistry.Register(OfficeSlidesViewerFormat.Instance);

        // Best-effort cleanup of any viewer/materialize temp-session folder a previous run left
        // behind (crash, kill -9, a killed UiTests host) - see ViewerTempSession.SweepOrphans's own
        // doc comment; "materialize" is PanelViewModel's own per-panel session category (archives
        // browsed/packed/unpacked from a non-local container).
        ViewerTempSession.SweepOrphans();
        TempSessionRoot.SweepOrphans("materialize");

        LogCrash("=== App startup ===");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogCrash("UNHANDLED: " + args.ExceptionObject);
            try
            {
                MessageBox.Show(args.ExceptionObject?.ToString(), "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                LogCrash("MessageBox failed: " + ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("UNOBSERVED TASK: " + args.Exception);
            args.SetObserved();
        };
        // Gated behind the same debug-mode env var as the UI-tree dump filter below - unlike
        // UnhandledException/UnobservedTaskException above, FirstChanceException fires for every
        // *handled* exception anywhere in the process, including the dozens of deliberate
        // try/catch-and-continue blocks scattered through this codebase (TempSessionRoot,
        // MtpFileSystem, FtpControlConnection, per-item copy loops, ...). Left unconditional, a
        // single large operation could serialise every worker thread on LogService's process-
        // global lock and file-append for exceptions nobody needed to see, and would write full
        // stack traces (routinely containing file paths and server-returned text) into app.log -
        // exactly the file users attach to bug reports.
        if (Environment.GetEnvironmentVariable(DiagnosticCommandChannel.EnvironmentVariable) == "1")
            AppDomain.CurrentDomain.FirstChanceException += (_, args) => LogFirstChanceException(args.Exception);

        // UI-tree dump for fast layout inspection (JSON, not a screenshot) - gated behind an env
        // var so it never activates outside a debug-mode launch; F12 is unmodified because
        // modifier-combo key injection through UI-automation tooling has been empirically
        // unreliable (see DiagnosticCommandChannel's own doc comment for the same reasoning).
        if (Environment.GetEnvironmentVariable(DiagnosticCommandChannel.EnvironmentVariable) == "1")
            Application.AddMessageFilter(new UiDumpMessageFilter());

        // Live layout tuning (F11) - click a control in the active dialog, nudge its geometry with
        // arrow keys, Ctrl+C a ready-to-paste snippet. Same debug-mode gate as everything else here.
        if (Environment.GetEnvironmentVariable(DiagnosticCommandChannel.EnvironmentVariable) == "1")
            Application.AddMessageFilter(new LayoutEditModeMessageFilter());

        ApplicationConfiguration.Initialize();

        // Apply theme
        ThemeService.ApplyTheme(SettingsService.GetEffectiveTheme());

        // Load saved language
        var settings = SettingsService.Load();
        if (!string.IsNullOrEmpty(settings.Language))
            LocalizationService.Current.LoadLanguage(settings.Language);

        // Prune credential-store entries for connection profiles that no longer exist -
        // ConnectionsForm.RemoveSelected already deletes the matching credential directly when a
        // profile is removed through the UI; this is the backstop CredentialStore.RemoveOrphans'
        // own doc comment describes for the cases that bypass that path (a hand-edited
        // settings.json, a settings file restored from an older backup, a crash between the two
        // writes) - without it a removed connection's password lives on disk forever.
        CredentialStore.Instance.RemoveOrphans(settings.Connections.Select(c => c.Id));

        // Create ViewModel and main form
        var vm = new MainViewModel();
        using var mainForm = new MainForm(vm);
        mainForm.FormClosed += (_, _) => vm.Dispose();

        // invoke_command channel for automated UI tests - see DiagnosticCommandChannel's own doc
        // comment. Gated by the same env var as the UI-tree dump above; a no-op call outside a
        // debug-mode launch.
        DiagnosticCommandChannel.Start(vm, mainForm);
        mainForm.FormClosed += (_, _) => DiagnosticCommandChannel.Stop();
        mainForm.FormClosed += (_, _) => LayoutEditModeService.Shutdown();

        Application.Run(mainForm);
    }

    private static readonly string CrashLogPath = Path.Combine(Path.GetTempPath(), "CoderCommander_crash.log");

    /// <summary>Crash log is rotated to <c>.old</c> once it passes this size, the same one-generation
    /// scheme <see cref="LogService"/> already uses for app.log - previously this file was never
    /// rotated at all and grew without bound for as long as %TEMP% kept it around, which on a
    /// machine that runs the app for months (or crashes in a loop) is an unbounded-size file
    /// nothing ever cleans up.</summary>
    private const long MaxCrashLogSizeBytes = 5 * 1024 * 1024; // 5 MB, matching LogService

    private static void LogCrash(string msg)
    {
        lock (_crashLogLock)
        {
            try
            {
                RotateCrashLogIfTooLarge();

                // Full date, not just time-of-day: even with rotation, one generation can still span
                // more than a day, so a bare HH:mm:ss.fff timestamp would make entries from different
                // days indistinguishable when merged with app.log by time.
                File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {msg}\r\n");
            }
            catch (Exception ex)
            {
                // Log write failure — cannot do anything
                System.Diagnostics.Debug.WriteLine($"LogCrash failed: {ex}");
            }
        }
    }
    private static readonly object _crashLogLock = new();

    /// <summary>Best-effort, deliberately as simple as <see cref="LogCrash"/> itself has to be:
    /// this runs on the crash path, potentially before the app has reached any themed or otherwise
    /// working state, so a failure here must never throw back into the caller - a failed rotation
    /// should cost nothing worse than an unrotated log entry.</summary>
    private static void RotateCrashLogIfTooLarge()
    {
        try
        {
            Utils.LogRotation.RotateIfTooLarge(CrashLogPath, MaxCrashLogSizeBytes);
        }
        catch
        {
            // Best-effort - a failed rotation must not stop crash logging, which is itself the
            // last-resort diagnostic when everything else has already gone wrong.
        }
    }

    // Throttles FirstChanceException logging: a caught-and-swallowed exception thrown in a
    // tight loop (e.g. a per-item try/catch during a large copy) would otherwise flood
    // app.log with a full stack trace per iteration. Keyed by exception type + originating
    // stack frame, one log line per key per window.
    private static readonly Dictionary<string, DateTime> _firstChanceLastLogged = new();
    private static readonly TimeSpan _firstChanceThrottleWindow = TimeSpan.FromSeconds(2);
    private static readonly object _firstChanceLock = new();

    private static void LogFirstChanceException(Exception ex)
    {
        try
        {
            var key = ex.GetType().FullName + "|" + (ex.StackTrace?.Split('\n').FirstOrDefault() ?? "");
            lock (_firstChanceLock)
            {
                var now = DateTime.Now;
                if (_firstChanceLastLogged.TryGetValue(key, out var last) && now - last < _firstChanceThrottleWindow)
                    return;
                _firstChanceLastLogged[key] = now;
                // Cap the dictionary to prevent unbounded growth from diverse exception sources.
                if (_firstChanceLastLogged.Count > 256)
                    _firstChanceLastLogged.Clear();
            }
            LogService.Debug($"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", "FirstChance");
        }
        catch
        {
            // Logging a first-chance exception must never itself throw back into the
            // exception machinery it's observing.
        }
    }
}

/// <summary>Watches for an unmodified F12 keydown and dumps the active form's control tree
/// via <see cref="UiDumpService"/> - see Program.Main for the debug-mode env var gate.</summary>
internal sealed class UiDumpMessageFilter : IMessageFilter
{
    private const int WM_KEYDOWN = 0x0100;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg == WM_KEYDOWN && (Keys)m.WParam.ToInt32() == Keys.F12)
            UiDumpService.DumpActiveFormToFile();
        return false;
    }
}

/// <summary>
/// Drives <see cref="LayoutEditModeService"/> from raw window messages - see Program.Main for the
/// debug-mode env var gate. F11 toggles regardless of active state; every other key only acts while
/// active, and clicks are only intercepted while active.
///
/// <para>Selection uses <c>Control.FromHandle(m.HWnd)</c> rather than a hand-rolled recursive
/// point-to-control walk: Windows has already resolved <c>m.HWnd</c> to the exact, z-order-correct
/// child window under the cursor before this filter ever sees the message (almost every control in
/// this app - Label, Button, Panel, TableLayoutPanel, RoundedButton, ... - owns its own native HWND),
/// so there is nothing left to hit-test.</para>
///
/// <para>Alt+arrow arrives as <c>WM_SYSKEYDOWN</c>, not <c>WM_KEYDOWN</c> - handled explicitly and
/// always swallowed while active, otherwise it falls through to <c>DefWindowProc</c> and activates
/// the system menu / produces an error beep instead of nudging a TableLayoutPanel row/column.</para>
///
/// <para>Mouse drag (move the selected control's body, resize via one of its 8 handles) needs
/// WM_MOUSEMOVE/WM_LBUTTONUP too, not just WM_LBUTTONDOWN - but only swallowed while a drag is
/// actually in progress (<see cref="LayoutEditModeService.BeginDrag"/> was called), so idle mouse
/// traffic elsewhere in the app is never touched by this filter.</para>
/// </summary>
internal sealed class LayoutEditModeMessageFilter : IMessageFilter
{
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_MOUSEMOVE = 0x0200;

    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            return HandleKey((Keys)m.WParam.ToInt32());
        if (!LayoutEditModeService.IsActive)
            return false;
        return m.Msg switch
        {
            WM_LBUTTONDOWN => HandleMouseDown(m.HWnd, m.LParam),
            WM_MOUSEMOVE => HandleMouseMove(m.HWnd, m.LParam),
            WM_LBUTTONUP => HandleMouseUp(),
            _ => false,
        };
    }

    private static bool HandleKey(Keys key)
    {
        if (key == Keys.F11)
        {
            LayoutEditModeService.Toggle();
            return true;
        }
        if (!LayoutEditModeService.IsActive)
            return false;

        if (key == Keys.Escape)
        {
            LayoutEditModeService.Toggle();
            return true;
        }

        var mods = Control.ModifierKeys;
        var ctrl = (mods & Keys.Control) != 0;
        var alt = (mods & Keys.Alt) != 0;
        var step = (mods & Keys.Shift) != 0 ? 10 : 1;

        if (ctrl && key == Keys.C)
        {
            LayoutEditModeService.ExportToClipboard();
            return true;
        }

        if (key is not (Keys.Up or Keys.Down or Keys.Left or Keys.Right))
            return false;

        int dx = key switch { Keys.Left => -step, Keys.Right => step, _ => 0 };
        int dy = key switch { Keys.Up => -step, Keys.Down => step, _ => 0 };

        if (LayoutEditModeService.SelectedItem != null)
        {
            // A ToolStripItem has no TableLayoutPanel cell/Dock=None Location to nudge - Margin and
            // Padding are all it has (see LayoutEditModeService.SelectedItem's own doc comment).
            // Alt+Arrow (table row/column resize) simply doesn't apply and is a silent no-op here.
            if (ctrl) LayoutEditModeService.NudgeItemPadding(dx, dy);
            else if (!alt) LayoutEditModeService.NudgeItemMargin(dx, dy);
            return true;
        }

        if (alt)
        {
            if (dx != 0) LayoutEditModeService.NudgeTableColumn(dx);
            if (dy != 0) LayoutEditModeService.NudgeTableRow(dy);
        }
        else if (ctrl)
        {
            LayoutEditModeService.NudgePadding(dx, dy);
        }
        else
        {
            var sel = LayoutEditModeService.Selected;
            if (sel?.Parent is TableLayoutPanel or FlowLayoutPanel)
                LayoutEditModeService.NudgeMargin(dx, dy);
            else if (sel != null && sel.Dock == DockStyle.None)
                LayoutEditModeService.NudgeLocation(dx, dy);
            // else: position is Dock-computed on a plain container - not directly nudgeable.
        }
        return true;
    }

    /// <summary>Decodes a mouse message's client-relative point from its LParam (exact at the
    /// instant Windows generated the message, unlike <c>Cursor.Position</c> which is read slightly
    /// later when this filter processes it) and converts it to screen coordinates via the window
    /// the message was actually sent to.</summary>
    private static Point ScreenPointOf(IntPtr hwnd, IntPtr lParam)
    {
        var raw = unchecked((int)(long)lParam);
        var clientPt = new Point(unchecked((short)raw), unchecked((short)(raw >> 16)));
        var control = Control.FromHandle(hwnd);
        return control?.PointToScreen(clientPt) ?? Cursor.Position;
    }

    /// <summary>True if <paramref name="ancestor"/> is somewhere in <paramref name="control"/>'s
    /// parent chain (not counting <paramref name="control"/> itself). Used to tell "the click landed
    /// on the selected control's own parent panel/background, which a resize handle's outward reach
    /// naturally spills into" apart from "the click landed on a genuinely different, separately-
    /// selectable sibling control" - see <see cref="HandleMouseDown"/>.</summary>
    private static bool IsAncestorOf(Control ancestor, Control? control)
    {
        for (var cur = control?.Parent; cur is not null; cur = cur.Parent)
        {
            if (ReferenceEquals(cur, ancestor)) return true;
        }
        return false;
    }

    private static bool HandleMouseDown(IntPtr hwnd, IntPtr lParam)
    {
        var screenPt = ScreenPointOf(hwnd, lParam);
        var control = Control.FromHandle(hwnd);

        // A handle's hit box deliberately reaches a few pixels past the selected control's own edge
        // (see LayoutEditHighlight's own doc comment) so it's easy to grab from just outside - but
        // that same reach can spill into a DIFFERENT, real, separately-selectable control sitting
        // close by (this app packs controls with only a few px of margin between neighbors, e.g.
        // DifferForm's two Browse buttons). A handle hit only wins when the click didn't land on some
        // other real control entirely - landing on a plain container (the parent panel/background the
        // handle's outward reach naturally extends into) still lets the handle win, since that's not
        // a control the user could have meant to switch selection to instead.
        var clickedOtherControl = control is not null
            && !ReferenceEquals(control, LayoutEditModeService.Selected)
            && !IsAncestorOf(control, LayoutEditModeService.Selected);

        if (!clickedOtherControl && LayoutEditHighlight.TryHitTestHandle(screenPt, out var handle))
        {
            LayoutEditModeService.BeginDrag(handle, isBodyMove: false, screenPt);
            return true;
        }

        if (control is null || control.FindForm() is LayoutEditHud)
            return false; // let clicks on the HUD itself (its Copy button) through normally

        // ToolStripButton/Label/etc. have no Win32 window of their own - a click anywhere inside a
        // ToolStrip/MenuStrip/StatusStrip always resolves here to the STRIP itself via
        // Control.FromHandle, never the individual item under the cursor. Resolve the real item
        // before falling through to "select the whole strip as if it were one big control", which
        // made switching between two different toolbar buttons look like nothing happened (the
        // highlight/HUD kept showing the same strip-wide Bounds no matter which icon was clicked).
        if (control is ToolStrip strip)
        {
            var item = strip.GetItemAt(strip.PointToClient(screenPt));
            if (item is not null)
            {
                if (ReferenceEquals(item, LayoutEditModeService.SelectedItem))
                {
                    LayoutEditModeService.BeginDrag(HandleKind.None, isBodyMove: true, screenPt);
                    return true; // body drag on the already-selected item - move, not reselect
                }
                LayoutEditModeService.SelectToolStripItem(item);
                return true;
            }
            // click landed on empty strip background (no item there) - fall through to selecting
            // the strip itself, same as clicking empty space in any other plain container.
        }

        if (control == LayoutEditModeService.Selected)
        {
            LayoutEditModeService.BeginDrag(HandleKind.None, isBodyMove: true, screenPt);
            return true; // body drag on the already-selected control - move, not reselect
        }

        LayoutEditModeService.Select(control);
        return true; // swallow - a real click on the target would also focus/press/toggle it
    }

    private static bool HandleMouseMove(IntPtr hwnd, IntPtr lParam)
    {
        if (!LayoutEditModeService.IsDragging) return false;
        LayoutEditModeService.ContinueDrag(ScreenPointOf(hwnd, lParam));
        return true;
    }

    private static bool HandleMouseUp()
    {
        if (!LayoutEditModeService.IsDragging) return false;
        LayoutEditModeService.EndDrag();
        return true;
    }
}
