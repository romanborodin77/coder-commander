using CoderCommander.Services;
using CoderCommander.Viewers;
using Microsoft.Web.WebView2.Core;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// HTML "browser mode" content - back/forward/refresh/stop over whatever page navigation the user
/// (or a relative link within the mapped folder - see <see cref="HtmlViewerFormat"/>'s own doc
/// comment) drives, plus the one security-relevant per-format toggle: whether script execution is
/// allowed at all (<c>AppSettings.ViewerHtmlAllowScripts</c>, off by default). "Show source" and
/// text search are deliberately not implemented for this format in this pass - both would need a
/// second navigable surface (a plain-text rendering of the raw markup) the way
/// <c>MarkdownViewerContent</c> has one, and HTML's "expose the file's own real directory" mapping
/// (rather than an isolated temp copy) makes writing that surface into the same mapped folder the
/// wrong move; see that content's own doc comment for the approach this format doesn't take.
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

        _scriptsBtn = new ToolStripButton(LocalizationService.Current.GetString("View.Html.Scripts"))
        {
            CheckOnClick = false,
            Checked = _ctx.Settings.ViewerHtmlAllowScripts,
            ForeColor = p.Danger,
        };
        _scriptsBtn.Click += (_, _) =>
        {
            _scriptsBtn.Checked = !_scriptsBtn.Checked;
            _ctx.Settings.ViewerHtmlAllowScripts = _scriptsBtn.Checked;
            SettingsService.Save(_ctx.Settings);
            Host.SetScriptEnabled(_scriptsBtn.Checked);
            // WebView2 applies IsScriptEnabled at the next navigation, not to the page already
            // loaded - without a reload here, turning the toggle OFF looks like it disabled
            // scripts (the button un-checks, the setting persists as false) while whatever
            // scripts the page already started (timers, event handlers) keep running. Reload
            // makes the toggle's visible state always match what's actually executing.
            Host.Core?.Reload();
        };

        _printBtn = ViewerToolbarFactory.CreateIconButton("print", "View.Html.Print", (_, _) => Host.Core?.ShowPrintUI());

        _toolbarItems = [_backBtn, _forwardBtn, _refreshBtn, _stopBtn, _scriptsBtn, _printBtn];
    }

    private WebViewHost Host => _ctx.WebViewHost;

    protected override void ConfigureScripting(WebViewHost host)
    {
        host.SetScriptEnabled(_ctx.Settings.ViewerHtmlAllowScripts);

        // First call after EnsureInitializedAsync inside the base class's ShowFileAsync - Core is
        // only non-null from here on, so this is the earliest point live back/forward state can
        // be wired. Persistent for this content's lifetime; HistoryChanged firing while this
        // format isn't the visible one is harmless (the buttons just aren't on screen yet).
        if (_wired || host.Core is not { } core) return;
        _wired = true;
        core.HistoryChanged += (_, _) => UpdateHistoryButtons(core);
        UpdateHistoryButtons(core);
    }

    private void UpdateHistoryButtons(CoreWebView2 core)
    {
        _backBtn.Enabled = core.CanGoBack;
        _forwardBtn.Enabled = core.CanGoForward;
    }

    public override void Dispose()
    {
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
