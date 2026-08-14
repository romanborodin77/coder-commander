namespace CoderCommander.Viewers;

/// <summary>Size caps shared by every loader - moved out of <c>ViewerForm</c> verbatim so new
/// formats (and their loaders, which live in separate files) can reuse the same numbers instead
/// of redeclaring them.</summary>
internal static class ViewerLimits
{
    public const long TextSizeLimit = 16 * 1024 * 1024; // 16MB - also the ASCII/Binary cap
    public const int HexBytesPerRow = 16;
    public const int HexMaxBytes = 1024 * 1024; // 1MB
    public const long ImageMaxFileBytes = 100 * 1024 * 1024; // 100MB
    public const long ImageMaxPixels = 64_000_000; // ~64 megapixels

    /// <summary>Cap on reading a non-local file fully into memory before writing it out to a
    /// materialized temp copy for Html/Pdf/Media (see <see cref="MaterializedFilePayload"/>) - a
    /// higher ceiling than <see cref="TextSizeLimit"/> because PDFs and media are legitimately
    /// much larger than any text file this app expects to display, but still a bound: nothing
    /// short of it stops "F3 a multi-GB remote video" from reading the whole thing into a
    /// <c>byte[]</c> before ever cancelling.</summary>
    public const long MaterializeMaxBytes = 256 * 1024 * 1024; // 256MB
}
