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
/// </summary>
internal sealed class MarkdownViewerContent : IViewerContent
{
    private const string RenderedFileName = "index.html";
    private const string SourceFileName = "source.html";

    private readonly Panel _wrapper;
    private readonly Label _errorLabel;
    private readonly ViewerContentContext _ctx;
    private readonly ToolStripButton _sourceToggleBtn;
    private readonly ToolStripButton _printBtn;
    private readonly ToolStripItem[] _toolbarItems;

    private string? _folder;
    private bool _showingSource;
    private bool _disposed;

    public Control View => _wrapper;
    public IReadOnlyList<ToolStripItem> ToolbarItems => _toolbarItems;
    public IViewerSearchTarget? SearchTarget => null; // deferred - see class doc comment on HtmlViewerContent
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

        _sourceToggleBtn = ViewerToolbarFactory.CreateToolButton("View.Md.Source", "source_view", (_, _) => _ = ToggleSourceAsync());
        _printBtn = ViewerToolbarFactory.CreateIconButton("print", "View.Md.Print", (_, _) => _ctx.WebViewHost.Core?.ShowPrintUI());
        _toolbarItems = [_sourceToggleBtn, _printBtn];
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

    private async Task ToggleSourceAsync()
    {
        try
        {
            if (_folder == null) return;
            _showingSource = !_showingSource;
            _sourceToggleBtn.Checked = _showingSource;
            var file = _showingSource ? SourceFileName : RenderedFileName;
            await _ctx.WebViewHost.NavigateAndWaitAsync($"https://{WebViewHost.VirtualHostName}/{file}", CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogService.Error($"ToggleSourceAsync failed: {ex.Message}", ex);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupFolder();
        _wrapper.Dispose();
        _errorLabel.Dispose();
        _sourceToggleBtn.Dispose();
        _printBtn.Dispose();
        GC.SuppressFinalize(this);
    }
}
