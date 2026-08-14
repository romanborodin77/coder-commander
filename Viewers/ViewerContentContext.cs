using CoderCommander.Services;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers;

/// <summary>
/// What an <see cref="IViewerFormat.CreateContent"/> implementation needs from its host
/// <c>ViewerForm</c> in order to build shared chrome (Word Wrap toggle, Find button) without
/// depending on <c>ViewerForm</c> itself - the "small injected surface instead of a back reference
/// to the whole owner" shape this was always meant to grow into once a format needed a shared
/// <see cref="WebViewHost"/> (phase 2's Markdown/Html/Pdf/Media).
/// </summary>
public sealed class ViewerContentContext
{
    public AppSettings Settings { get; }

    /// <summary>Shows the shared find bar for this content, if it implements
    /// <see cref="IViewerSearchTarget"/> - a no-op for content that doesn't.</summary>
    public Action ShowFindBar { get; }

    /// <summary>Re-runs the load pipeline for the currently active format against the current
    /// file - for a toolbar control whose change requires re-parsing from bytes (CSV's delimiter
    /// picker changes what <c>CsvViewerLoader</c> produces; a has-header toggle, by contrast,
    /// re-interprets the same already-parsed rows and does NOT need this).</summary>
    public Action Reload { get; }

    /// <summary>The one <see cref="WebViewHost"/> shared by every WebView-backed content in this
    /// window, created on first access - a window whose file never triggers a WebView format
    /// never touches this property, so the wrapper object (and, once it calls
    /// <see cref="WebViewHost.EnsureInitializedAsync"/>, the browser process itself) is never
    /// allocated at all.</summary>
    public WebViewHost WebViewHost => _webViewHost.Value;

    /// <summary>The per-window materialization folder every WebView-backed format writes its
    /// current file (or Markdown's rendered HTML) into before mapping/navigating - see
    /// <see cref="ViewerTempSession"/>'s own doc comment for the folder shape and cleanup story.</summary>
    public ViewerTempSession TempSession => _tempSession.Value;

    private readonly Lazy<WebViewHost> _webViewHost;
    private readonly Lazy<ViewerTempSession> _tempSession;

    public ViewerContentContext(AppSettings settings, Action showFindBar, Action reload,
                                 Lazy<WebViewHost> webViewHost, Lazy<ViewerTempSession> tempSession)
    {
        Settings = settings;
        ShowFindBar = showFindBar;
        Reload = reload;
        _webViewHost = webViewHost;
        _tempSession = tempSession;
    }
}
