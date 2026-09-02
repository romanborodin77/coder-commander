using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.WinForms;

/// <summary>
/// File viewer (F3) window. All the actual format-switching/loading/content machinery lives in
/// <see cref="ViewerHostControl"/> (Ф4 plan, step 1 - extracted so Quick View can reuse it inside a
/// compact panel-hosted control instead of a full window); this class is now just the window frame
/// around one: title (kept in sync with <see cref="ViewerHostControl.PathChanged"/>), size,
/// <c>KeyPreview</c> forwarding keystrokes into <see cref="ViewerHostControl.HandleKeyDown"/>, and
/// closing the window when the host raises <see cref="ViewerHostControl.CloseRequested"/> (Escape,
/// with nothing else left for the host itself to do with it).
/// </summary>
public sealed class ViewerForm : ThemedForm
{
    private readonly ViewerHostControl _host;

    /// <summary>
    /// Initializes the viewer window around a <see cref="ViewerHostControl"/>, then loads the
    /// specified file in the resolved initial format (a matched format like Image always wins for
    /// a file it recognizes; otherwise the last-used universal format preference).
    /// </summary>
    public ViewerForm(IFileSystem fileSystem, string path,
                       List<string>? files = null, int currentIndex = 0)
    {
        _host = new ViewerHostControl(fileSystem, path, files, currentIndex, SettingsService.Load());

        Text = TitleFor(_host.CurrentPath);
        var settings = SettingsService.Load();
        ClientSize = new Size(settings.ViewerWindowWidth, settings.ViewerWindowHeight);
        Resizable = true;
        MinimumSize = new Size(500, 400);
        // Form sees every key first (Escape/arrows/F5/Ctrl+F/1-4/etc.) regardless of which child
        // control currently has focus - the read-only content view would otherwise swallow arrow
        // keys for its own (useless, given ReadOnly) caret movement instead of them reaching
        // ViewerHostControl.HandleKeyDown. That method explicitly steps aside while the find bar
        // holds focus so typing/arrow-editing a search term still works normally.
        KeyPreview = true;

        Controls.Add(_host);

        _host.PathChanged += (_, _) => Text = TitleFor(_host.CurrentPath);
        _host.CloseRequested += (_, _) => Close();

        KeyDown += (_, e) => _host.HandleKeyDown(e);
        Load += (_, _) => _ = _host.LoadCurrentAsync();
    }

    private static string TitleFor(string path) =>
        $"{LocalizationService.Current.GetString("View.Title")} — {VfsPath.GetName(path)}";

    /// <summary>
    /// Remembers the window's size. Saved whichever way the window is closed, the same way the
    /// Settings dialog does it - this window used to open at a hardcoded 1000x700 every time, so
    /// resizing it lasted only as long as the window itself.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || WindowState != FormWindowState.Normal) return;

        var s = SettingsService.Load();
        s.ViewerWindowWidth = ClientSize.Width;
        s.ViewerWindowHeight = ClientSize.Height;
        SettingsService.Save(s);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _host.Dispose();
        base.Dispose(disposing);
    }
}
