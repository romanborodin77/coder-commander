using System.Net;
using System.Threading;
using CoderCommander.Services;
using CoderCommander.Viewers;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// Markdown content - renders Markdig's output via the shared <see cref="WebViewHost"/> and
/// offers a render/source toggle, unlike <see cref="HtmlViewerContent"/>: Markdown always
/// materializes into its own isolated temp folder (never the original file's real directory, so
/// there's nowhere unsafe to write), which means both the rendered page and a plain-text view of
/// the source can live side by side in that same folder and the toggle is just a re-navigation
/// between the two - no re-mapping, no re-reading the file.
///
/// Also doubles as the <see cref="IViewerSearchTarget"/> the shared find bar drives, over
/// <see cref="MarkdownPayload.SourceText"/> - searching the rendered HTML markup would be
/// useless to a user. This works (unlike <see cref="HtmlViewerContent"/>'s own arbitrary-markup
/// pages) specifically because <see cref="BuildSourceHtml"/> wraps that exact text, HTML-encoded
/// but otherwise untouched, in a single <c>&lt;pre&gt;</c> with no other markup: the browser
/// decodes entities back on parse, so the resulting text node's character offsets line up
/// 1:1 with <see cref="MarkdownPayload.SourceText"/>'s own offsets, and a match can be located
/// with a plain <c>Range</c> instead of needing an innerText-to-DOM offset mapping (which HTML's
/// arbitrary nested markup, and DOM-vs-innerText whitespace normalization, would make unreliable).
/// </summary>
internal sealed class MarkdownViewerContent : IViewerContent, IViewerSearchTarget
{
    private const string RenderedFileName = "index.html";
    private const string SourceFileName = "source.html";

    private readonly Panel _wrapper;
    private readonly Label _errorLabel;
    private readonly ViewerContentContext _ctx;
    private readonly ToolStripButton _findBtn;
    private readonly ToolStripButton _sourceToggleBtn;
    private readonly ToolStripButton _printBtn;
    private readonly ToolStripItem[] _toolbarItems;

    private string? _folder;
    private bool _showingSource;
    private bool _disposed;
    private string _sourceText = "";

    public Control View => _wrapper;
    public IReadOnlyList<ToolStripItem> ToolbarItems => _toolbarItems;
    public IViewerSearchTarget? SearchTarget => this;
    public string? StatusText { get; private set; }
    public event EventHandler? StatusChanged { add { } remove { } }

    public MarkdownViewerContent(ViewerContentContext ctx)
    {
        _ctx = ctx;
        var p = ThemeService.Current;

        _wrapper = new Panel { Dock = DockStyle.Fill, BackColor = p.Background, Visible = false };
        _errorLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = p.GridFont,
            ForeColor = p.Danger,
            Padding = new Padding(24),
            Visible = false,
        };
        _wrapper.Controls.Add(_errorLabel);

        _findBtn = ViewerToolbarFactory.CreateToolButton("View.Search", "search", (_, _) => ctx.ShowFindBar());
        _sourceToggleBtn = ViewerToolbarFactory.CreateToolButton("View.Md.Source", "source_view", (_, _) => _ = ToggleSourceAsync());
        _printBtn = ViewerToolbarFactory.CreateIconButton("print", "View.Md.Print", (_, _) => _ctx.WebViewHost.Core?.ShowPrintUI());
        _toolbarItems = [_findBtn, _sourceToggleBtn, _printBtn];
    }

    public async Task RenderAsync(ViewerPayload payload, CancellationToken ct)
    {
        switch (payload)
        {
            case MarkdownPayload md:
                _errorLabel.Visible = false;
                await ShowAsync(md, ct);
                StatusText = md.StatusText;
                break;

            case ViewerErrorPayload err:
                _sourceText = "";
                _errorLabel.Text = err.Message;
                _errorLabel.Visible = true;
                StatusText = "";
                if (err.Modal)
                {
                    StyledMessageBox.Show(err.Message, LocalizationService.Current.GetString("View.Error"),
                        MsgBoxButtons.OK, MsgBoxIcon.Error);
                }
                break;
        }
    }

    private async Task ShowAsync(MarkdownPayload md, CancellationToken ct)
    {
        var host = _ctx.WebViewHost;
        await host.EnsureInitializedAsync();
        if (ct.IsCancellationRequested) return;

        CleanupFolder();
        var folder = _ctx.TempSession.AllocateFileFolder();
        // Recorded immediately, before either write - not after both, like the original code did.
        // A load superseded mid-write (the two awaits above) still leaves a real folder on disk;
        // recording it here means the NEXT ShowAsync's CleanupFolder() (or Dispose) actually finds
        // and deletes it, instead of orphaning it until TempSessionRoot's next-startup sweep.
        _folder = folder;
        _sourceText = md.SourceText;
        await File.WriteAllTextAsync(Path.Combine(folder, RenderedFileName), md.RenderedHtml, ct);
        await File.WriteAllTextAsync(Path.Combine(folder, SourceFileName), BuildSourceHtml(md.SourceText), ct);
        if (ct.IsCancellationRequested) return;

        _showingSource = false;
        _sourceToggleBtn.Checked = false;
        host.SetScriptEnabled(false);
        host.MapFolder(folder);
        host.AttachTo(_wrapper);
        await host.NavigateAndWaitAsync($"https://{WebViewHost.VirtualHostName}/{RenderedFileName}", ct);
    }

    private Task ToggleSourceAsync() => NavigateToAsync(!_showingSource);

    /// <summary>Navigates to the rendered or source page, no-op if already showing the requested
    /// one - used both by the toolbar toggle button (always flips) and by <see cref="SelectRange"/>
    /// (only navigates when a match needs the source page and it isn't already up, so repeated
    /// Next/Previous clicks while already on source don't each trigger a pointless reload).</summary>
    private async Task NavigateToAsync(bool showSource)
    {
        try
        {
            if (_folder == null || _showingSource == showSource) return;
            _showingSource = showSource;
            _sourceToggleBtn.Checked = _showingSource;
            var file = _showingSource ? SourceFileName : RenderedFileName;
            await _ctx.WebViewHost.NavigateAndWaitAsync($"https://{WebViewHost.VirtualHostName}/{file}", CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogService.Error($"NavigateToAsync failed: {ex.Message}", ex);
        }
    }

    private static string BuildSourceHtml(string source) =>
        ViewerHtmlTemplate.WrapDocument($"<pre style=\"white-space:pre-wrap;word-break:break-word;\">{WebUtility.HtmlEncode(source)}</pre>");

    private void CleanupFolder()
    {
        if (_folder == null) return;
        try
        {
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // Best-effort - same reasoning as ViewerTempSession's own cleanup paths.
        }
        _folder = null;
    }

    public void ApplyTheme()
    {
        var p = ThemeService.Current;
        _wrapper.BackColor = p.Background;
        _errorLabel.ForeColor = p.Danger;
        _errorLabel.Font = p.GridFont;
    }

    // ── IViewerSearchTarget ─────────────────────────────────────────────────────────────────

    public string GetSearchText() => _sourceText;

    /// <summary>Always 0 - a WebView page has no caret-position equivalent to resume from the
    /// way <see cref="TextViewerContent"/> does via <c>RichTextBox.SelectionStart</c>; every
    /// re-search restarts from the top of the source text instead.</summary>
    public int CurrentOffset => 0;

    public void SelectRange(int start, int length) => _ = SelectRangeAsync(start, length);

    private async Task SelectRangeAsync(int start, int length)
    {
        try
        {
            await NavigateToAsync(showSource: true).ConfigureAwait(true);
            var core = _ctx.WebViewHost.Core;
            if (core == null) return;

            // start/length are ints computed by ViewerFindBar's own IndexOf scan against the
            // exact string GetSearchText() returned - never user-controlled text, so interpolating
            // them directly into the script is safe (no injection surface).
            var script = $$"""
                (function() {
                    var pre = document.querySelector('pre');
                    if (!pre || !pre.firstChild) return false;
                    var node = pre.firstChild;
                    var max = node.length;
                    var s = Math.max(0, Math.min({{start}}, max));
                    var e = Math.max(s, Math.min({{start + length}}, max));
                    var range = document.createRange();
                    range.setStart(node, s);
                    range.setEnd(node, e);
                    var sel = window.getSelection();
                    sel.removeAllRanges();
                    sel.addRange(range);
                    var rect = range.getBoundingClientRect();
                    window.scrollBy({ top: rect.top - window.innerHeight / 2, left: 0, behavior: 'instant' });
                    return true;
                })();
                """;
            await core.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            LogService.Warning($"Markdown SelectRange failed: {ex.Message}");
        }
    }

    public void FocusContent() => _ctx.WebViewHost.Control.Focus();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupFolder();
        _wrapper.Dispose();
        _errorLabel.Dispose();
        _findBtn.Dispose();
        _sourceToggleBtn.Dispose();
        _printBtn.Dispose();
        GC.SuppressFinalize(this);
    }
}
