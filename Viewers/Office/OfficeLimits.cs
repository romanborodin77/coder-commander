namespace CoderCommander.Viewers.Office;

/// <summary>Every bound an Office-document conversion runs under, in one file - the same "no
/// single check on a random line, every ceiling visible at a glance" convention as
/// <c>Viewers.ViewerLimits</c>/<c>FileSystem.Remote.RemoteLimits</c>. All of these apply
/// <b>before</b> the corresponding data is decompressed/parsed, not after - a zip bomb's whole
/// danger is in the decompression itself, so a check that runs after decompressing has already
/// lost.</summary>
internal static class OfficeLimits
{
    /// <summary>Refuse a package with more parts than this outright - a legitimate document has at
    /// most a few hundred; anything past a few thousand is either pathological or hostile.</summary>
    public const int MaxEntries = 5000;

    /// <summary>Cap on one XML part's declared uncompressed size before it's read into memory.</summary>
    public const long MaxPartBytes = 32 * 1024 * 1024; // 32MB

    /// <summary>Cap on the whole package's declared total uncompressed size, summed across every
    /// entry, checked before any entry is opened.</summary>
    public const long MaxTotalUncompressedBytes = 512 * 1024 * 1024; // 512MB

    /// <summary>An entry whose declared uncompressed size exceeds its packed size by more than this
    /// ratio is treated as a zip bomb and rejected, exactly like <c>ArchiveEntryRecord.Size</c>/
    /// <c>PackedSize</c> already let <c>ArchiveFileSystem</c> detect elsewhere in this app - checked
    /// from the central directory's own reported sizes, never by decompressing first to find out.</summary>
    public const double MaxCompressionRatio = 200.0;

    /// <summary>One embedded image, as a data: URI budget.</summary>
    public const long MaxImageBytes = 8 * 1024 * 1024; // 8MB

    /// <summary>Running total across every image embedded in one rendered document.</summary>
    public const long MaxTotalImageBytes = 64 * 1024 * 1024; // 64MB

    /// <summary>Spreadsheet dimensions, after ODS's <c>table:number-columns/rows-repeated</c>
    /// clamping (see <c>OdfSheetConverter</c>'s own doc comment) or an XLSX sheet's own declared
    /// dimension - the second-layer cap once the "last non-empty cell/row" trim has already run.</summary>
    public const int MaxRows = 20_000;
    public const int MaxColumns = 500;
}
