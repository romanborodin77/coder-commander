using CoderCommander.FileSystem;

namespace CoderCommander.Archives.SharpCompress;

/// <summary>Read-only RAR4/RAR5 support via SharpCompress. RAR write support is legally
/// unavailable without a license from RarLab, so this format has no writer at all, ever.</summary>
public sealed class RarArchiveFormat : IArchiveFormat
{
    public static readonly RarArchiveFormat Instance = new();

    public string Id => "rar";
    public string DisplayNameKey => "Archive.Format.Rar";
    public IReadOnlyList<string> Extensions { get; } = new[] { ".rar" };
    public string DefaultExtension => ".rar";

    public ArchiveCapabilities Capabilities =>
        ArchiveCapabilities.Read | ArchiveCapabilities.Browse | ArchiveCapabilities.PasswordProtectedRead;

    public IReadOnlyList<CompressionPreset> SupportedPresets { get; } = Array.Empty<CompressionPreset>();

    public bool MatchesSignature(ReadOnlySpan<byte> header)
    {
        // First 6 bytes are shared between the RAR4 ("...\x07\x00") and RAR5 ("...\x07\x01\x00")
        // signatures; checking just the common prefix covers both without telling them apart.
        ReadOnlySpan<byte> magic = new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07 };
        return header.Length >= magic.Length && header[..magic.Length].SequenceEqual(magic);
    }

    public IArchiveReader OpenRead(string archivePath, string? password = null) =>
        new SharpCompressReader(archivePath, SharpCompressKind.Rar, password);

    public IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options) =>
        throw new NotSupportedException($"\"{archivePath}\" is a RAR archive, which is read-only and cannot be modified.");

    public IFileSystem? CreateFileSystem(string archivePath) => new ArchiveFileSystem(this, archivePath);
}
