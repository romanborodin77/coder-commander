using CoderCommander.FileSystem;

namespace CoderCommander.Archives.SharpCompress;

/// <summary>Read-only 7z support via SharpCompress. Writing 7z requires a native 7z.dll and is
/// out of scope (see the plan's deferred, optional Phase 6).</summary>
public sealed class SevenZipArchiveFormat : IArchiveFormat
{
    public static readonly SevenZipArchiveFormat Instance = new();

    public string Id => "7z";
    public string DisplayNameKey => "Archive.Format.SevenZip";
    public IReadOnlyList<string> Extensions { get; } = new[] { ".7z" };
    public string DefaultExtension => ".7z";

    public ArchiveCapabilities Capabilities => ArchiveCapabilities.Read | ArchiveCapabilities.Browse;

    public IReadOnlyList<CompressionPreset> SupportedPresets { get; } = Array.Empty<CompressionPreset>();

    public bool MatchesSignature(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> magic = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };
        return header.Length >= magic.Length && header[..magic.Length].SequenceEqual(magic);
    }

    public IArchiveReader OpenRead(string archivePath) => new SharpCompressReader(archivePath, SharpCompressKind.SevenZip);

    public IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options) =>
        throw new NotSupportedException($"\"{archivePath}\" is a 7z archive, which is read-only and cannot be modified.");

    public IFileSystem? CreateFileSystem(string archivePath) => new ArchiveFileSystem(this, archivePath);
}
