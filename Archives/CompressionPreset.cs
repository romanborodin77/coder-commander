namespace CoderCommander.Archives;

/// <summary>
/// Format-neutral compression intent. Each <see cref="IArchiveFormat"/> maps these onto its own
/// native levels (e.g. ZIP's <see cref="System.IO.Compression.CompressionLevel"/>, gzip's 0-9,
/// LZMA2's level) via <see cref="IArchiveFormat.SupportedPresets"/> and its writer implementation.
/// </summary>
public enum CompressionPreset
{
    /// <summary>No compression - just container the bytes.</summary>
    Store = 0,
    Fastest = 1,
    Balanced = 2,
    /// <summary>Smallest output, slowest. Not reachable by every format/writer.</summary>
    Maximum = 3
}

/// <summary>
/// A compression request passed to <see cref="IArchiveWriter.WriteFileAsync"/>. <see cref="Preset"/>
/// is always present as the portable fallback; <see cref="NativeLevel"/>/<see cref="Method"/> let a
/// specific writer honor a more precise, format-specific choice when one was supplied.
/// </summary>
public sealed record ArchiveCompressionSpec(CompressionPreset Preset)
{
    /// <summary>Format-specific numeric level (e.g. LZMA2 0-9). Null = derive from <see cref="Preset"/>.</summary>
    public int? NativeLevel { get; init; }

    /// <summary>Format-specific method name (e.g. "LZMA2", "BZip2"). Null = format's default method.</summary>
    public string? Method { get; init; }

    public static readonly ArchiveCompressionSpec Store = new(CompressionPreset.Store);
    public static readonly ArchiveCompressionSpec Balanced = new(CompressionPreset.Balanced);
}
