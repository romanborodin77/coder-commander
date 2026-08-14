using System.Threading;

namespace CoderCommander.Viewers;

/// <summary>
/// The UI-thread half of one viewer format: owns a content <see cref="Control"/> and whatever
/// toolbar items that format contributes. Created once per format id and cached for the
/// <c>ViewerForm</c> window's lifetime (see its own <c>GetOrCreateContent</c>) - Text/ASCII/
/// Binary/Hex each get their own instance today rather than sharing one, since four lightweight
/// <see cref="System.Windows.Forms.RichTextBox"/>es is cheap; that changes once a format needs a
/// genuinely expensive shared surface (WebView2, phase 2).
/// </summary>
public interface IViewerContent : IDisposable
{
    /// <summary>Dock=Fill, added to the shared content host on first use. Only one content's
    /// <see cref="View"/> is <see cref="Control.Visible"/> at a time.</summary>
    Control View { get; }

    /// <summary>Toolbar items this format owns (e.g. Image's zoom/rotate cluster, the shared
    /// Word Wrap/Find pair for text-family formats). Created once in the content's constructor;
    /// <c>ViewerForm</c> inserts them into the shared <see cref="ToolStrip"/> once and thereafter
    /// only flips their <see cref="ToolStripItem.Visible"/> - it never re-inserts or removes
    /// them, so the content owns disposing them (via <see cref="IDisposable.Dispose"/>).</summary>
    IReadOnlyList<ToolStripItem> ToolbarItems { get; }

    /// <summary>If this content can be searched, the target the shared find bar should search -
    /// null for content with no meaningful text search (Image).</summary>
    IViewerSearchTarget? SearchTarget { get; }

    /// <summary>The status-bar mode label for whatever was last rendered (e.g. "Text mode — 12.4
    /// KB", "Image mode — 1024x768px, 100%") - set by <see cref="RenderAsync"/>, read by
    /// <c>ViewerForm</c> immediately after it completes. Null before the first successful render.</summary>
    string? StatusText { get; }

    /// <summary>Raised when <see cref="StatusText"/> changes outside of a
    /// <see cref="RenderAsync"/> call - Image raises this on zoom/rotate, which change the label
    /// without a new load. <c>ViewerForm</c> only applies it while this content is the active one.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Applies a payload produced by this format's <see cref="IViewerLoader"/>. Runs on
    /// the UI thread. Implementations pattern-match on the concrete <see cref="ViewerPayload"/>
    /// subtype; an unrecognized subtype (defensive only - a format's loader and content are
    /// always written as a matched pair) is simply ignored rather than throwing.</summary>
    Task RenderAsync(ViewerPayload payload, CancellationToken ct);

    /// <summary>Re-applies the current theme to this content's controls - called on every
    /// <c>ThemeService.ThemeChanged</c>, mirroring what <c>ViewerForm.OnThemeChanged</c> used to
    /// do by hand for <c>_textView</c>/<c>_pictureBox</c>/<c>_imageScrollPanel</c> directly.</summary>
    void ApplyTheme();
}
