using System.Threading;
using CoderCommander.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// The single <see cref="WebView2"/> instance shared by every WebView-backed format
/// (Markdown/Html/Pdf/Media) in one <c>ViewerForm</c> window - five browser processes per window
/// is not acceptable, so there is exactly one <see cref="Control"/> and each format's own
/// <c>IViewerContent</c> reparents it into its own wrapper panel via <see cref="AttachTo"/> during
/// <c>RenderAsync</c>, rather than each format owning a distinct WebView2 of its own.
///
/// <para><b>Initialization</b> is lazy and cached (<see cref="EnsureInitializedAsync"/>), never
/// triggered at construction - a file manager that spins up a browser process on startup, before
/// the user has ever pressed F3 on a Markdown/HTML/PDF/media file, would be a regression. The user
/// data folder lives under <see cref="DataDirectory.Root"/> (not the OS default), which is what
/// lets <c>CODERCOMMANDER_DATA_DIR</c> isolate a sandboxed <c>UiTests</c> run - two instances
/// sharing one real UDF hit a hard WebView2 failure.</para>
///
/// <para><b>Security.</b> Every format that uses this host renders either content this app
/// generated itself (Markdown → HTML, a materialized local/remote file mapped read-only) or an
/// untrusted local HTML file (browser mode) - never a live, arbitrary internet page. The lockdown
/// applied in <see cref="ConfigureSecurityBaseline"/> reflects that: scripts off by default (only
/// HTML format's explicit, user-visible toggle turns them on), no DevTools/host objects/autofill/
/// password-save/default dialogs, every non-GET-to-our-own-origin navigation cancelled at both the
/// top-level and frame level (<see cref="VirtualHostName"/> is the only origin ever allowed to
/// navigate to), and every subresource fetch outside that same origin answered with 403 - a
/// same-origin allow-list rather than a same-origin navigation gate alone, because a page loaded
/// from our own origin could still reference an absolute external URL as an image/script/iframe
/// src, which <see cref="CoreWebView2.NavigationStarting"/> does not see at all.</para>
/// </summary>
public sealed class WebViewHost : IDisposable
{
    /// <summary>Reserved, non-routable virtual host name (RFC 6761 <c>.example</c>, per WebView2's
    /// own guidance) that every materialized file is mapped under - never <c>.local</c>, which the
    /// same guidance calls out as adding navigation delay. Fixed for the process; only the folder
    /// behind it changes per navigation (see <see cref="MapFolder"/>).</summary>
    public const string VirtualHostName = "cc-viewer.example";

    private readonly WebView2 _webView;
    private Task? _initTask;
    private Panel? _currentOwner;
    private bool _disposed;

    /// <summary>Serializes <see cref="NavigateAndWaitAsync"/> calls - see that method's own doc
    /// comment for why event-id correlation alone isn't safe on a host shared by every format in
    /// the window.</summary>
    private readonly SemaphoreSlim _navLock = new(1, 1);

    public Control Control => _webView;

    public bool IsInitialized => _webView.CoreWebView2 != null;

    /// <summary>The initialized <see cref="CoreWebView2"/>, or null before
    /// <see cref="EnsureInitializedAsync"/> has completed - exposed for the one format (HTML
    /// browser mode) that needs live browser actions (<c>GoBack</c>/<c>GoForward</c>/<c>Reload</c>/
    /// <c>Stop</c>/<c>ShowPrintUI</c>) and history-changed notifications beyond what
    /// <see cref="NavigateAndWaitAsync"/> covers. Every other member on this class stays the
    /// narrow, format-agnostic surface the rest of this app's formats actually need.</summary>
    public CoreWebView2? Core => _webView.CoreWebView2;

    public WebViewHost()
    {
        _webView = new WebView2 { Dock = DockStyle.Fill };
    }

    /// <summary>Triggers (once) and awaits initialization of the underlying <see cref="CoreWebView2"/>.
    /// Safe to call repeatedly - the second and later callers just await the same cached task.
    /// Must be called on the UI thread (WebView2's own requirement); every caller in this codebase
    /// is a content's <c>RenderAsync</c>, already guaranteed to run there.</summary>
    public Task EnsureInitializedAsync()
    {
        _initTask ??= InitAsync();
        return _initTask;
    }

    private async Task InitAsync()
    {
        var userDataFolder = Path.Combine(DataDirectory.Root, "webview2");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, null).ConfigureAwait(true);
        await _webView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
        ConfigureSecurityBaseline(_webView.CoreWebView2);
    }

    private static void ConfigureSecurityBaseline(CoreWebView2 core)
    {
        var s = core.Settings;
        s.IsScriptEnabled = false;
        s.IsWebMessageEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.AreDevToolsEnabled = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsZoomControlEnabled = true;
        s.IsStatusBarEnabled = false;
        // Chromium's built-in accelerators (Ctrl+O open-file, Ctrl+S save, Ctrl+P print, Ctrl+R
        // reload, ...) stay live by default even with scripts and DevTools off - Ctrl+O in
        // particular pops a native file-open dialog that then navigates to whatever local file the
        // user picks. IsOwnOrigin + the WebResourceRequested 403 backstop above still block that
        // navigation, but the stray OS dialog and silently-cancelled load are not what a "locked
        // down" viewer should ever present. Formats that need a specific one of these already have
        // their own explicit toolbar button calling the CoreWebView2 API directly (Print via
        // ShowPrintUI() in HtmlViewerContent/OfficeViewerContent/MarkdownViewerContent).
        s.AreBrowserAcceleratorKeysEnabled = false;

        core.NavigationStarting += (_, e) => { if (!IsOwnOrigin(e.Uri)) e.Cancel = true; };
        core.FrameNavigationStarting += (_, e) => { if (!IsOwnOrigin(e.Uri)) e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;

        // The filter is what makes WebResourceRequested fire at all - a documented, easy-to-miss
        // requirement (see the event's own remarks). "*" + All covers every request kind
        // (document, subresource, XHR, ...) so the 403 backstop below applies uniformly.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (IsOwnOrigin(e.Request.Uri)) return;
            e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
    }

    private static bool IsOwnOrigin(string uri) =>
        uri.StartsWith($"https://{VirtualHostName}/", StringComparison.Ordinal) ||
        uri.StartsWith("about:", StringComparison.Ordinal);

    /// <summary>Enables or disables script execution - every format resets this to <c>false</c>
    /// before navigating except HTML format, which passes through the user's explicit
    /// <c>AppSettings.ViewerHtmlAllowScripts</c> toggle. Everything else in
    /// <see cref="ConfigureSecurityBaseline"/> (host objects, DevTools, dialogs, autofill,
    /// navigation/resource origin lock) never varies by format - only script execution is ever a
    /// user-visible, per-format choice.</summary>
    public void SetScriptEnabled(bool enabled)
    {
        if (_webView.CoreWebView2 is { } core) core.Settings.IsScriptEnabled = enabled;
    }

    /// <summary>Maps <see cref="VirtualHostName"/> onto <paramref name="folderPath"/>, replacing
    /// whatever it previously pointed to - called immediately before every navigation, since each
    /// format materializes its current file into a fresh folder (see
    /// <see cref="Viewers.ViewerTempSession.AllocateFileFolder"/>) rather than reusing one.
    /// <see cref="CoreWebView2HostResourceAccessKind.Deny"/> because nothing this host renders
    /// needs cross-origin fetch/XHR access to its own mapped folder from another origin.</summary>
    public void MapFolder(string folderPath) =>
        _webView.CoreWebView2!.SetVirtualHostNameToFolderMapping(
            VirtualHostName, folderPath, CoreWebView2HostResourceAccessKind.Deny);

    /// <summary>Navigates to a URL under <see cref="VirtualHostName"/> and awaits exactly one
    /// <see cref="CoreWebView2.NavigationCompleted"/> - the Stage B half of the load pipeline
    /// described on <c>Viewers.IViewerLoader</c>'s own doc comment. <paramref name="ct"/> only
    /// abandons the wait on this side; it cannot cancel the underlying browser navigation
    /// (WebView2 exposes no such API), which is why <c>ViewerForm.LoadFileAsync</c>'s own
    /// staleness guard - checking <c>ct</c> again after this returns - is still required.
    ///
    /// <para><b>Serialized via <see cref="_navLock"/>, not just matched by navigation id.</b> An
    /// earlier version of this method matched <see cref="CoreWebView2.NavigationStarting"/> to
    /// <see cref="CoreWebView2.NavigationCompleted"/> purely by <c>NavigationId</c>, reasoning that
    /// "the first <c>NavigationStarting</c> to fire after we subscribe is guaranteed to be the one
    /// <c>Navigate(url)</c> causes, since that call happens synchronously, before any await". That
    /// reasoning covers only ONE call's own subscribe-then-navigate window; it does not hold once a
    /// SECOND, overlapping call exists. This host is shared, and more than one caller can trigger a
    /// navigation close together (Prev/Next racing a toolbar action like Markdown's source toggle or
    /// HTML's Back button): call A subscribes and calls <c>Navigate(urlA)</c>, then <c>await</c>s -
    /// yielding the UI thread back to the message pump BEFORE the browser process has actually
    /// dispatched <c>NavigationStarting</c> for that request (WebView2 raises it asynchronously, not
    /// inside the synchronous <c>Navigate()</c> call itself). If a second UI event fires call B in
    /// that window, B subscribes its OWN <c>OnStarting</c> before A's event has been delivered - so
    /// when it finally arrives, BOTH handlers see it and both record A's id as "their own",
    /// including B, which never actually caused it. A lock removes the ambiguity structurally
    /// instead of trying to correlate it more cleverly: at most one <c>NavigateAndWaitAsync</c> call
    /// is ever subscribed to these events at a time, so there is nothing left for a second call to
    /// misattribute. Acceptable because this is one shared, single-visible-page control per window -
    /// two navigations were never going to show simultaneously anyway, so a caller waiting briefly
    /// for the lock loses nothing a would-be "concurrent" navigation could have given it.</para>
    /// </summary>
    public async Task NavigateAndWaitAsync(string url, CancellationToken ct)
    {
        await _navLock.WaitAsync(ct).ConfigureAwait(true);
        try
        {
            var core = _webView.CoreWebView2!;
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var navId = 0UL;
            var gotId = false;
            void OnStarting(object? s, CoreWebView2NavigationStartingEventArgs e)
            {
                if (gotId) return;
                gotId = true;
                navId = e.NavigationId;
            }
            void OnCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (gotId && e.NavigationId == navId) tcs.TrySetResult();
            }

            core.NavigationStarting += OnStarting;
            core.NavigationCompleted += OnCompleted;
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            try
            {
                core.Navigate(url);
                await tcs.Task;
            }
            finally
            {
                core.NavigationStarting -= OnStarting;
                core.NavigationCompleted -= OnCompleted;
            }
        }
        finally
        {
            _navLock.Release();
        }
    }

    /// <summary>Reparents the shared <see cref="Control"/> into <paramref name="owner"/>, removing
    /// it from whichever wrapper panel last held it. A no-op if it's already there - called on
    /// every <c>RenderAsync</c>, not just format switches, so this must be cheap in the common
    /// case of navigating within the same format (Prev/Next through several PDFs).</summary>
    public void AttachTo(Panel owner)
    {
        if (ReferenceEquals(_currentOwner, owner)) return;
        _currentOwner?.Controls.Remove(_webView);
        owner.Controls.Add(_webView);
        _currentOwner = owner;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _webView.Dispose();
        _navLock.Dispose();
    }
}
