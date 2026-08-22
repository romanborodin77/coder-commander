using System.Threading;
using CoderCommander.Services;
using CoderCommander.Viewers;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// Shared content for all three Office formats (Word/Sheet/Slides) - a Word document always
/// renders as one page; Sheet/Slides can be many, which is what the page-navigation toolbar pair
/// is for (hidden entirely when there's only one page, so a Word document's toolbar doesn't show
/// a pointless "1 / 1"). Each page is written to its own file in the package's isolated temp
/// folder and navigated between exactly the way <c>MarkdownViewerContent</c>'s render/source
/// toggle works - no re-mapping, just a fresh <c>NavigateAndWaitAsync</c> to a sibling file already
/// sitting in the same mapped folder.
///
/// <para><b>No search target.</b> Unlike <c>MarkdownViewerContent</c> (which searches its own
/// plain <see cref="Viewers.MarkdownPayload.SourceText"/> against a single flat <c>&lt;pre&gt;</c>
/// that decodes back to identical character offsets), the OOXML/ODF converters
/// (<c>Viewers.Office.*</c>) emit real structural HTML - headings, tables, runs, inline images -
/// with no accompanying flat-text-with-offsets representation to search against, and each page is
/// its own separately-navigated document. Reliably mapping a plain-text search hit back onto that
/// nested markup (across whichever page currently happens to be shown) would need each converter
/// to additionally emit an offset index alongside its HTML, which none of them do today - a
/// bigger, separate undertaking than adding a find bar here.</para>
/// </summary>
internal sealed class OfficeViewerContent : IViewerContent
{
    private readonly Panel _wrapper;
    private readonly Label _errorLabel;
    private readonly ViewerContentContext _ctx;
    private readonly ToolStripButton _prevBtn;
    private readonly ToolStripButton _nextBtn;
    private readonly ToolStripLabel _pageLabel;
    private readonly ToolStripButton _printBtn;
    private readonly ToolStripItem[] _toolbarItems;

    private string? _folder;
    private IReadOnlyList<OfficeDocumentPage> _pages = [];
    private int _currentPage;
    private bool _disposed;

    public Control View => _wrapper;
    public IReadOnlyList<ToolStripItem> ToolbarItems => _toolbarItems;
    public IViewerSearchTarget? SearchTarget => null;
    public string? StatusText { get; private set; }
    public event EventHandler? StatusChanged { add { } remove { } }

    public OfficeViewerContent(ViewerContentContext ctx)
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

        _prevBtn = ViewerToolbarFactory.CreateIconButton("nav_back", "View.Office.PreviousPage", (_, _) => _ = GoToPageAsync(_currentPage - 1));
        _pageLabel = new ToolStripLabel("");
        _nextBtn = ViewerToolbarFactory.CreateIconButton("nav_forward", "View.Office.NextPage", (_, _) => _ = GoToPageAsync(_currentPage + 1));
        _printBtn = ViewerToolbarFactory.CreateIconButton("print", "View.Office.Print", (_, _) => _ctx.WebViewHost.Core?.ShowPrintUI());
        _toolbarItems = [_prevBtn, _pageLabel, _nextBtn, _printBtn];
        UpdatePageNav();
    }

    public async Task RenderAsync(ViewerPayload payload, CancellationToken ct)
    {
        switch (payload)
        {
            case OfficeDocumentPayload doc:
                _errorLabel.Visible = false;
                await ShowDocumentAsync(doc, ct);
                StatusText = doc.StatusText;
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

    private async Task ShowDocumentAsync(OfficeDocumentPayload doc, CancellationToken ct)
    {
        var host = _ctx.WebViewHost;
        await host.EnsureInitializedAsync();
        if (ct.IsCancellationRequested) return;

        CleanupFolder();
        var folder = _ctx.TempSession.AllocateFileFolder();
        // Recorded immediately, before the page-writing loop - see MarkdownViewerContent.ShowAsync's
        // identical fix. A multi-page document superseded mid-write otherwise leaves a real folder
        // (with however many pages had been written so far) that nothing ever cleans up until the
        // next app startup's orphan sweep.
        _folder = folder;
        for (var i = 0; i < doc.Pages.Count; i++)
            await File.WriteAllTextAsync(Path.Combine(folder, PageFileName(i)), doc.Pages[i].Html, ct);
        if (ct.IsCancellationRequested) return;

        _pages = doc.Pages;
        _currentPage = 0;

        host.SetScriptEnabled(false);
        host.MapFolder(folder);
        host.AttachTo(_wrapper);
        UpdatePageNav();
        await host.NavigateAndWaitAsync($"https://{WebViewHost.VirtualHostName}/{PageFileName(0)}", ct);
    }

    private async Task GoToPageAsync(int index)
    {
        try
        {
            if (_folder == null || index < 0 || index >= _pages.Count || index == _currentPage) return;
            _currentPage = index;
            UpdatePageNav();
            await _ctx.WebViewHost.NavigateAndWaitAsync($"https://{WebViewHost.VirtualHostName}/{PageFileName(index)}", CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogService.Error($"GoToPageAsync failed: {ex.Message}", ex);
        }
    }

    private static string PageFileName(int index) => $"page{index}.html";

    private void UpdatePageNav()
    {
        var multi = _pages.Count > 1;
        _prevBtn.Visible = multi;
        _nextBtn.Visible = multi;
        _pageLabel.Visible = multi;
        if (!multi) return;

        _prevBtn.Enabled = _currentPage > 0;
        _nextBtn.Enabled = _currentPage < _pages.Count - 1;
        var title = _pages[_currentPage].Title;
        _pageLabel.Text = string.IsNullOrEmpty(title)
            ? $"{_currentPage + 1} / {_pages.Count}"
            : $"{title} ({_currentPage + 1}/{_pages.Count})";
    }

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
            // Best-effort - same reasoning as every other WebView-backed content's temp cleanup.
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
        _prevBtn.Dispose();
        _nextBtn.Dispose();
        _pageLabel.Dispose();
        _printBtn.Dispose();
        GC.SuppressFinalize(this);
    }
}
