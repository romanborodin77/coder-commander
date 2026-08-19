using System.Threading;
using CoderCommander.Services;
using CoderCommander.Viewers;
using CoderCommander.WinForms;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// Shared shape for every format that hands the shared <see cref="WebViewHost"/> a file to
/// navigate to directly - Html (browser mode), Pdf, Media. Each still gets its own
/// <see cref="IViewerContent"/> instance (own wrapper <see cref="Panel"/>, own toolbar) rather
/// than sharing one; only the underlying <see cref="WebViewHost.Control"/> is shared, reparented
/// into whichever format's wrapper is active via <see cref="WebViewHost.AttachTo"/> - see that
/// class's own doc comment for why (one browser process per window, not five).
/// </summary>
internal abstract class WebFileViewerContentBase : IViewerContent
{
    private readonly Panel _wrapper;
    private readonly Label _errorLabel;
    private readonly ViewerContentContext _ctx;
    private string? _ownTempFolder;
    private bool _disposed;

    public Control View => _wrapper;
    public virtual IReadOnlyList<ToolStripItem> ToolbarItems => [];
    public virtual IViewerSearchTarget? SearchTarget => null;
    public string? StatusText { get; protected set; }
    public event EventHandler? StatusChanged { add { } remove { } }

    protected WebFileViewerContentBase(ViewerContentContext ctx)
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
    }

    public async Task RenderAsync(ViewerPayload payload, CancellationToken ct)
    {
        switch (payload)
        {
            case MaterializedFilePayload file:
                _errorLabel.Visible = false;
                await ShowFileAsync(file, ct);
                StatusText = file.StatusText;
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

    private async Task ShowFileAsync(MaterializedFilePayload file, CancellationToken ct)
    {
        var host = _ctx.WebViewHost;
        await host.EnsureInitializedAsync();
        if (ct.IsCancellationRequested) return;

        string folder;
        if (file.IsOwnDirectory)
        {
            folder = file.Directory!;
        }
        else
        {
            CleanupOwnTempFolder();
            folder = _ctx.TempSession.AllocateFileFolder();
            _ownTempFolder = folder;
            await File.WriteAllBytesAsync(Path.Combine(folder, file.FileName), file.Bytes!, ct);
        }
        if (ct.IsCancellationRequested) return;

        ConfigureScripting(host);
        host.MapFolder(folder);
        host.AttachTo(_wrapper);

        var url = $"https://{WebViewHost.VirtualHostName}/{Uri.EscapeDataString(file.FileName)}";
        await host.NavigateAndWaitAsync(url, ct);
    }

    /// <summary>Script execution defaults to off for every format that uses this base; only
    /// <c>HtmlViewerContent</c> overrides it, to pass through the user's explicit
    /// <c>AppSettings.ViewerHtmlAllowScripts</c> toggle.</summary>
    protected virtual void ConfigureScripting(WebViewHost host) => host.SetScriptEnabled(false);

    private void CleanupOwnTempFolder()
    {
        if (_ownTempFolder == null) return;
        try
        {
            if (Directory.Exists(_ownTempFolder))
                Directory.Delete(_ownTempFolder, recursive: true);
        }
        catch
        {
            // Best-effort - same reasoning as ViewerTempSession's own cleanup paths. The whole
            // session folder is swept again at next startup regardless.
        }
        _ownTempFolder = null;
    }

    public virtual void ApplyTheme()
    {
        var p = ThemeService.Current;
        _wrapper.BackColor = p.Background;
        _errorLabel.ForeColor = p.Danger;
        _errorLabel.Font = p.GridFont;
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupOwnTempFolder();
        _wrapper.Dispose();
        _errorLabel.Dispose();
        GC.SuppressFinalize(this);
    }
}
