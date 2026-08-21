using CoderCommander.FileSystem;

namespace CoderCommander.Archives;

/// <summary>
/// Descriptor + factory for one archive container format. Registered once at startup with
/// <see cref="ArchiveFormatRegistry.Register"/>; every other layer (Pack dialog, panel VFS
/// dispatch, settings UI) goes through the registry rather than referencing a format directly.
/// </summary>
public interface IArchiveFormat
{
    /// <summary>Stable identifier used as a settings key ("zip", "tar", "tar.gz", "7z", "rar").
    /// Not localized, never shown to the user directly.</summary>
    string Id { get; }

    /// <summary>Localization key for the format's display name.</summary>
    string DisplayNameKey { get; }

    /// <summary>Recognized extensions, longest-match first (e.g. ".tar.gz" before ".gz").</summary>
    IReadOnlyList<string> Extensions { get; }

    string DefaultExtension { get; }

    ArchiveCapabilities Capabilities { get; }

    /// <summary>Compression choices meaningful for this format; a Store-only format returns a
    /// single-element list.</summary>
    IReadOnlyList<CompressionPreset> SupportedPresets { get; }

    /// <summary>Sniffs a leading chunk of the file for this format's magic signature. Used as a
    /// fallback when extension-based detection doesn't resolve a format - see
    /// <see cref="ArchiveFormatRegistry.Detect"/>.</summary>
    bool MatchesSignature(ReadOnlySpan<byte> header);

    /// <summary>Opens the archive for reading. <paramref name="password"/> decrypts entries when
    /// <see cref="Capabilities"/> includes <see cref="ArchiveCapabilities.PasswordProtectedRead"/>;
    /// otherwise it is ignored (never persisted - the caller owns the string's lifetime).</summary>
    IArchiveReader OpenRead(string archivePath, string? password = null);

    /// <summary>Throws <see cref="NotSupportedException"/> if <see cref="Capabilities"/> doesn't
    /// include <see cref="ArchiveCapabilities.Create"/> or <see cref="ArchiveCapabilities.AddEntries"/>.</summary>
    IArchiveWriter OpenWrite(string archivePath, ArchiveWriteOptions options);

    /// <summary>Returns a panel-browsable <see cref="IFileSystem"/> over the archive, or null if
    /// <see cref="Capabilities"/> doesn't include <see cref="ArchiveCapabilities.Browse"/>.</summary>
    IFileSystem? CreateFileSystem(string archivePath);
}
