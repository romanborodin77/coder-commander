using CoderCommander.Services;
using CoderCommander.Viewers;
using Microsoft.Web.WebView2.Core;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// HTML "browser mode" content - back/forward/refresh/stop over whatever page navigation the user
/// (or a relative link within the mapped folder - see <see cref="HtmlViewerFormat"/>'s own doc
/// comment) drives, plus the one security-relevant per-format toggle: whether script execution is
/// allowed at all (<c>AppSettings.ViewerHtmlAllowScripts</c>, off by default). "Show source" is
/// deliberately not implemented for this format - it would need a second navigable surface (a
/// plain-text rendering of the raw markup) the way <c>MarkdownViewerContent</c> has one, and
/// HTML's "expose the file's own real directory" mapping (rather than an isolated temp copy)
/// makes writing that surface into the same mapped folder the wrong move. Text search inherits
/// the base class's default (none) for a related but distinct reason - see
/// <see cref="WebFileViewerContentBase"/>'s own doc comment.
/// </summary>
internal sealed class HtmlViewerContent : WebFileViewerContentBase
{
    private readonly ToolStripButton _backBtn;
    private readonly ToolStripButton _forwardBtn;
    private readonly ToolStripButton _refreshBtn;
    private readonly ToolStripButton _stopBtn;
    private readonly ToolStripButton _scriptsBtn;
    private readonly ToolStripButton _printBtn;
    private readonly ToolStripItem[] _toolbarItems;
    private readonly ViewerContentContext _ctx;
    private bool _wired;
    private EventHandler<object>? _historyChangedHandler;

    /// <summary>Session-scoped, deliberately never written back to <see cref="AppSettings"/> -
    /// see <see cref="ConfigureScripting"/>'s doc comment for why persisting a toggle here would
    /// silently grant script execution to every future HTML file, not just the one it was turned
    /// on for.</summary>
    private bool _scriptsAllowedThisSession;

    public override IReadOnlyList<ToolStripItem> ToolbarItems => _toolbarItems;

    public HtmlViewerContent(ViewerContentContext ctx) : base(ctx)
    {
        _ctx = ctx;
        var p = ThemeService.Current;

        _backBtn = ViewerToolbarFactory.CreateIconButton("nav_back", "View.Html.Back", (_, _) => Host.Core?.GoBack());
        _forwardBtn = ViewerToolbarFactory.CreateIconButton("nav_forward", "View.Html.Forward", (_, _) => Host.Core?.GoForward());
        _refreshBtn = ViewerToolbarFactory.CreateIconButton("refresh", "View.Html.Refresh", (_, _) => Host.Core?.Reload());
        _stopBtn = ViewerToolbarFactory.CreateIconButton("stop", "View.Html.Stop", (_, _) => Host.Core?.Stop());
        _backBtn.Enabled = false;
        _forwardBtn.Enabled = false;

        _scriptsAllowedThisSession = _ctx.Settings.ViewerHtmlAllowScripts;
        _scriptsBtn = new ToolStripButton(LocalizationService.Current.GetString("View.Html.Scripts"))
        {
            CheckOnClick = false,
            Checked = _scriptsAllowedThisSession,
            ForeColor = p.Danger,
        };
        _scriptsBtn.Click += (_, _) =>
        {
            // In-memory only for this open file - see ConfigureScripting's doc comment. Not
            // written to AppSettings/SettingsService.Save: this used to persist here, which meant
            // one deliberate "yes, run scripts in this page I trust" silently became "run scripts
            // in every HTML file I ever open with F3" for the rest of the app's life.
            _scriptsAllowedThisSession = !_scriptsAllowedThisSession;
            _scriptsBtn.Checked = _scriptsAllowedThisSession;
            Host.SetScriptEnabled(_scriptsAllowedThisSession);
            // WebView2 applies IsScriptEnabled at the next navigation, not to the page already
            // loaded - without a reload here, turning the toggle OFF looks like it disabled
            // scripts (the button un-checks) while whatever scripts the page already started
            // (timers, event handlers) keep running. Reload makes the toggle's visible state
            // always match what's actually executing.
            Host.Core?.Reload();
        };

        _printBtn = ViewerToolbarFactory.CreateIconButton("print", "View.Html.Print", (_, _) => Host.Core?.ShowPrintUI());

        _toolbarItems = [_backBtn, _forwardBtn, _refreshBtn, _stopBtn, _scriptsBtn, _printBtn];
    }

    private WebViewHost Host => _ctx.WebViewHost;

    protected override void ConfigureScripting(WebViewHost host)
    {
        // Reset to the app's persisted baseline on every fresh render (ShowFileAsync calls this
        // once per file RenderAsync delivers - not on the toggle button's own Reload(), which
        // revisits the same file). Without this reset, toggling scripts on for one HTML file and
        // then opening a different one in the same window would carry the "yes" over to content
        // the user never actually consented to running scripts for.
        _scriptsAllowedThisSession = _ctx.Settings.ViewerHtmlAllowScripts;
        _scriptsBtn.Checked = _scriptsAllowedThisSession;
        host.SetScriptEnabled(_scriptsAllowedThisSession);

        // First call after EnsureInitializedAsync inside the base class's ShowFileAsync - Core is
        // only non-null from here on, so this is the earliest point live back/forward state can
        // be wired. Persistent for this content's lifetime; HistoryChanged firing while this
        // format isn't the visible one is harmless (the buttons just aren't on screen yet). The
        // handler is stored so Dispose() can unsubscribe it before this instance's own buttons are
        // disposed - core is the shared, window-lifetime CoreWebView2, so an unsubscribed handler
        // otherwise outlives _backBtn/_forwardBtn and fires into disposed controls during teardown.
        if (_wired || host.Core is not { } core) return;
        _wired = true;
        _historyChangedHandler = (_, _) => UpdateHistoryButtons(core);
        core.HistoryChanged += _historyChangedHandler;
        UpdateHistoryButtons(core);
    }

    private void UpdateHistoryButtons(CoreWebView2 core)
    {
        _backBtn.Enabled = core.CanGoBack;
        _forwardBtn.Enabled = core.CanGoForward;
    }

    public override void Dispose()
    {
        // Unsubscribe from the shared, window-lifetime CoreWebView2 before the buttons it updates
        // are disposed below - ViewerForm.Dispose disposes contents (this) before the WebViewHost,
        // so a still-subscribed handler firing during WebView2's own teardown hit an already-
        // disposed ToolStripButton (ObjectDisposedException on the UI thread while closing).
        if (_historyChangedHandler is { } handler && Host.Core is { } core)
            core.HistoryChanged -= handler;
        _historyChangedHandler = null;

        _backBtn.Dispose();
        _forwardBtn.Dispose();
        _refreshBtn.Dispose();
        _stopBtn.Dispose();
        _scriptsBtn.Dispose();
        _printBtn.Dispose();
        base.Dispose();
    }

    public override void ApplyTheme()
    {
        base.ApplyTheme();
        _scriptsBtn.ForeColor = ThemeService.Current.Danger;
    }
}
