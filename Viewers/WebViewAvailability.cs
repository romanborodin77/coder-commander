using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace CoderCommander.Viewers;

/// <summary>
/// Whether the WebView2 Runtime is installed at all - probed once per process and cached, because
/// <see cref="CoreWebView2Environment.GetAvailableBrowserVersionString()"/> throws on a machine
/// with no runtime (a bare Windows Server, LTSC, or a stripped-down Windows 10 image; Windows 11
/// ships it pre-installed, but this app does not require Windows 11).
///
/// <para>Consumed by <see cref="ViewerFormatRegistry"/> is deliberately NOT where this is checked -
/// a format's registration is static and process-wide, while runtime availability could in theory
/// change while the app is running (an admin installing the Evergreen runtime mid-session). Instead
/// every <c>NeedsWebView</c> format's <c>IViewerFormat.CreateContent</c>/loader path checks this
/// directly, and <c>ViewerForm</c>'s format-offering logic (mirroring
/// <c>ViewerFormatRegistry.Detect</c>) filters a WebView format out before it can ever be selected
/// when the runtime is missing - see each WebView-backed format's own doc comment for its specific
/// fallback (always: degrade to a universal format, never crash the viewer).</para>
/// </summary>
internal static class WebViewAvailability
{
    private static bool? _available;

    /// <summary>True once, then cached for the process's lifetime - matches the "probe is cheap
    /// enough to redo, but there is no reason to" reasoning already used elsewhere in this app
    /// (e.g. <c>ShellCatalog</c>'s discovery cache).</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_available.HasValue) return _available.Value;

            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                _available = !string.IsNullOrEmpty(version);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _available = false;
            }
            catch (COMException)
            {
                // Covers the handful of other native-loader failure shapes (fixed-version folder
                // misconfigured, corrupted install) without treating them as a crash-worthy
                // condition - the viewer degrading to hex/text is the correct outcome either way.
                _available = false;
            }

            return _available.Value;
        }
    }
}
