namespace CoderCommander.Viewers;

/// <summary>
/// What a viewer format's content is capable of - decides which shared toolbar groups
/// (<c>ViewerForm</c>'s Find button, Word Wrap toggle) apply before a content instance even
/// exists. Follows the same flag-enum rules as <see cref="FileSystem.FileSystemCapabilities"/>:
/// plural name, <see cref="None"/> = 0, powers of two, flags added only when a format genuinely
/// differs.
/// </summary>
[Flags]
public enum ViewerCapabilities
{
    None = 0,

    /// <summary>Participates in the viewer's find bar via <see cref="IViewerSearchTarget"/>.</summary>
    TextSearch = 1 << 0,

    /// <summary>The shared Word Wrap toolbar toggle applies to this format's content.</summary>
    WordWrap = 1 << 1,

    /// <summary>The format's content contributes its own zoom controls (Image today).</summary>
    Zoom = 1 << 2,

    /// <summary>The format's content contributes its own rotate controls (Image today).</summary>
    Rotate = 1 << 3,

    /// <summary>The format needs the shared <c>WinForms.Viewers.WebViewHost</c> - Markdown, Html,
    /// Pdf, Media. <see cref="ViewerFormatRegistry"/> filters these out of both extension and
    /// signature matching whenever <c>WebViewAvailability.IsAvailable</c> is false, so a machine
    /// with no WebView2 Runtime installed degrades straight to a universal format (hex/text)
    /// instead of ever offering a format whose content would fail to initialize.</summary>
    NeedsWebView = 1 << 4,

    /// <summary>Convenience combination for the plain-text family (Text/ASCII/Binary/Hex).</summary>
    TextLike = TextSearch | WordWrap,
}
