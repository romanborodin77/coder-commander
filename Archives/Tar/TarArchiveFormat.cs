using CoderCommander.FileSystem;

namespace CoderCommander.Archives.Tar;

/// <summary>Plain (uncompressed) TAR. Sequential-access only; add/delete goes through
/// <see cref="RewritingArchiveWriter"/> since TAR has no central directory to update in place.</summary>
public sealed class TarArchiveFormat : IArchiveFormat
{
    public static readonly TarArchiveFormat Instance = new();

    public string Id => "tar";
    public string DisplayNameKey => "Archive.Format.Tar";
    public IReadOnlyList<string> Extensions { get; } = new[] { ".tar" };
    public string DefaultExtension => ".tar";

    public ArchiveCapabilities Capabilities =>
        ArchiveCapabilities.Read | ArchiveCapabilities.Create | ArchiveCapabilities.AddEntries |
        ArchiveCapabilities.DeleteEntries | ArchiveCapabilities.Browse;

    /// <summary>TAR has no compression of its own - Store is the only meaningful option.</summary>
    public IReadOnlyList<CompressionPreset> SupportedPresets { get; } = new[] { CompressionPreset.Store };

    public bool MatchesSignature(ReadOnlySpan<byte> header)
    {
        // "ustar" magic at offset 257 - catches POSIX ustar/PAX/GNU (everything
        // System.Formats.Tar itself writes). Plain legacy V7 tar has no magic at all and isn't
        // sniffable; extension-based detection covers that case instead.
        const int magicOffset = 257;
        ReadOnlySpan<byte> magic = "ustar"u8;
        if (header.Length < magicOffset + magic.Length) return false;
        return header.Slice(magicOffset, magic.Length).SequenceEqual(magic);
    }

    // password is unused: plain TAR has no entry-level encryption scheme at all.
    public IArchiveReader OpenRead(string archivePath, string? password = null) => new TarArchiveReader(archivePath, gzip: false);

    public IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options) =>
        new RewritingArchiveWriter(archivePath, this, stream => new TarSequentialWriter(stream, gzip: false));

    public IFileSystem? CreateFileSystem(string archivePath) => new ArchiveFileSystem(this, archivePath);
}
