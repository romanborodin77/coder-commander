using CoderCommander.FileSystem;

namespace CoderCommander.Archives.Tar;

/// <summary>Gzip-compressed TAR. Compression applies to the whole container, not per entry, so
/// unlike ZIP there's exactly one usable preset until per-format settings (a later phase) let the
/// gzip level itself be chosen.</summary>
public sealed class TarGzArchiveFormat : IArchiveFormat
{
    public static readonly TarGzArchiveFormat Instance = new();

    public string Id => "tar.gz";
    public string DisplayNameKey => "Archive.Format.TarGz";
    public IReadOnlyList<string> Extensions { get; } = new[] { ".tar.gz", ".tgz" };
    public string DefaultExtension => ".tar.gz";

    public ArchiveCapabilities Capabilities =>
        ArchiveCapabilities.Read | ArchiveCapabilities.Create | ArchiveCapabilities.AddEntries |
        ArchiveCapabilities.DeleteEntries | ArchiveCapabilities.Browse;

    public IReadOnlyList<CompressionPreset> SupportedPresets { get; } = new[] { CompressionPreset.Balanced };

    public bool MatchesSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 2 && header[0] == 0x1F && header[1] == 0x8B;

    public IArchiveReader OpenRead(string archivePath) => new TarArchiveReader(archivePath, gzip: true);

    public IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options) =>
        new RewritingArchiveWriter(archivePath, this, stream => new TarSequentialWriter(stream, gzip: true));

    public IFileSystem? CreateFileSystem(string archivePath) => new ArchiveFileSystem(this, archivePath);
}
