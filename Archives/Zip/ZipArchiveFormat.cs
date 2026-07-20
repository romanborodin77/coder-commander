using CoderCommander.FileSystem;

namespace CoderCommander.Archives.Zip;

/// <summary>
/// Registry descriptor for ZIP. Thin wrapper: all the actual work is still
/// <see cref="ZipArchiveFileSystem"/> (central-directory parsing, legacy code-page handling,
/// locked-file retry) - this and its sibling reader/writer only adapt that existing
/// implementation to the format-neutral <see cref="IArchiveFormat"/> shape.
/// </summary>
public sealed class ZipArchiveFormat : IArchiveFormat
{
    public static readonly ZipArchiveFormat Instance = new();

    private static readonly byte[] LocalFileHeaderSignature = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] EmptyArchiveSignature = { 0x50, 0x4B, 0x05, 0x06 };

    public string Id => "zip";
    public string DisplayNameKey => "Archive.Format.Zip";
    /// <summary>".jar" is a plain ZIP container under a different name (Java archives).</summary>
    public IReadOnlyList<string> Extensions { get; } = new[] { ".zip", ".jar" };
    public string DefaultExtension => ".zip";

    public ArchiveCapabilities Capabilities =>
        ArchiveCapabilities.Read | ArchiveCapabilities.RandomAccessRead |
        ArchiveCapabilities.Create | ArchiveCapabilities.AddEntries | ArchiveCapabilities.DeleteEntries |
        ArchiveCapabilities.Browse;

    public IReadOnlyList<CompressionPreset> SupportedPresets { get; } = new[]
    {
        CompressionPreset.Store, CompressionPreset.Fastest, CompressionPreset.Balanced, CompressionPreset.Maximum
    };

    public bool MatchesSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 4 &&
        (header[..4].SequenceEqual(LocalFileHeaderSignature) || header[..4].SequenceEqual(EmptyArchiveSignature));

    public IArchiveReader OpenRead(string archivePath) => new ZipArchiveReader(archivePath);

    public IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options) =>
        new ZipArchiveWriter(archivePath, options);

    public IFileSystem? CreateFileSystem(string archivePath) => new ZipArchiveFileSystem(archivePath);
}
