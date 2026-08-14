namespace CoderCommander.Viewers;

/// <summary>
/// Whether a viewer format is always offered, or only when it actually matches the file.
/// </summary>
public enum ViewerAvailability
{
    /// <summary>Always offered in the toolbar's mode group for any file, and part of the
    /// fall-back chain when nothing more specific matches - Text, ASCII, Binary, Hex.</summary>
    Universal,

    /// <summary>Offered only when <see cref="IViewerFormat.Extensions"/> or
    /// <see cref="IViewerFormat.MatchesSignature"/> claims the file - Image today, more formats
    /// in later phases (CSV, Markdown, HTML, PDF, media, Office documents).</summary>
    Matched,
}
