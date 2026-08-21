using CoderCommander.FileSystem;

namespace CoderCommander.Archives.SharpCompress;

/// <summary>Read-only support for xz-compressed TAR via SharpCompress. Unlike TAR.BZ2, this stays
/// read-only for a real reason, not just because nobody wired it up yet: SharpCompress 0.50.3's own
/// <see cref="global::SharpCompress.Writers.Tar.TarWriter"/> explicitly rejects Xz/LZMA/LZMA2 with
/// an <c>InvalidFormatException</c> (confirmed by hand), and the library ships no XZ encoder at all
/// (<c>SharpCompress.Compressors.Xz</c> only has read-side types) - see
/// <see cref="SharpCompressTarWriter"/>'s doc comment for the same finding.</summary>
public sealed class TarXzArchiveFormat : IArchiveFormat
{
    public static readonly TarXzArchiveFormat Instance = new();

    public string Id => "tar.xz";
    public string DisplayNameKey => "Archive.Format.TarXz";
    public IReadOnlyList<string> Extensions { get; } = new[] { ".tar.xz", ".txz" };
    public string DefaultExtension => ".tar.xz";

    public ArchiveCapabilities Capabilities => ArchiveCapabilities.Read | ArchiveCapabilities.Browse;

    public IReadOnlyList<CompressionPreset> SupportedPresets { get; } = Array.Empty<CompressionPreset>();

    public bool MatchesSignature(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> magic = new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 };
        return header.Length >= magic.Length && header[..magic.Length].SequenceEqual(magic);
    }

    public IArchiveReader OpenRead(string archivePath, string? password = null) =>
        new SharpCompressReader(archivePath, SharpCompressKind.TarXz, password);

    public IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options) =>
        throw new NotSupportedException($"\"{archivePath}\" is a TAR.XZ archive, which is read-only and cannot be modified.");

    public IFileSystem? CreateFileSystem(string archivePath) => new ArchiveFileSystem(this, archivePath);
}
