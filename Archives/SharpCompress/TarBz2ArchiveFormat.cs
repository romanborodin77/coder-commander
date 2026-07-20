using CoderCommander.FileSystem;
using SharpCompress.Common;

namespace CoderCommander.Archives.SharpCompress;

/// <summary>Bzip2-compressed TAR, read via SharpCompress and - unlike the other three
/// SharpCompress-backed formats - also writable through it: SharpCompress's own
/// <see cref="global::SharpCompress.Writers.Tar.TarWriter"/> supports BZip2 natively (see
/// <see cref="SharpCompressTarWriter"/>), so add/delete goes through the same
/// <see cref="RewritingArchiveWriter"/> plumbing TAR/TAR.GZ use, since bzip2-over-tar has no central
/// directory to update in place either.</summary>
public sealed class TarBz2ArchiveFormat : IArchiveFormat
{
    public static readonly TarBz2ArchiveFormat Instance = new();

    public string Id => "tar.bz2";
    public string DisplayNameKey => "Archive.Format.TarBz2";
    public IReadOnlyList<string> Extensions { get; } = new[] { ".tar.bz2", ".tbz2", ".tbz" };
    public string DefaultExtension => ".tar.bz2";

    public ArchiveCapabilities Capabilities =>
        ArchiveCapabilities.Read | ArchiveCapabilities.Create | ArchiveCapabilities.AddEntries |
        ArchiveCapabilities.DeleteEntries | ArchiveCapabilities.Browse;

    /// <summary>Compression applies to the whole container, not per entry - same one-option
    /// situation as TAR.GZ.</summary>
    public IReadOnlyList<CompressionPreset> SupportedPresets { get; } = new[] { CompressionPreset.Balanced };

    public bool MatchesSignature(ReadOnlySpan<byte> header)
    {
        // "BZh" + a block-size digit ('1'-'9'); the digit varies by how the file was compressed.
        ReadOnlySpan<byte> magic = "BZh"u8;
        return header.Length >= magic.Length + 1 &&
               header[..magic.Length].SequenceEqual(magic) &&
               header[magic.Length] is >= (byte)'1' and <= (byte)'9';
    }

    public IArchiveReader OpenRead(string archivePath) => new SharpCompressReader(archivePath, SharpCompressKind.TarBz2);

    public IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options) =>
        new RewritingArchiveWriter(archivePath, this, stream => new SharpCompressTarWriter(stream, CompressionType.BZip2));

    public IFileSystem? CreateFileSystem(string archivePath) => new ArchiveFileSystem(this, archivePath);
}
