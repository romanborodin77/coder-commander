namespace CoderCommander.FileSystem;

/// <summary>
/// What an <see cref="IFileSystem"/> can actually do, so callers ask about the capability they
/// need instead of testing for a concrete provider type.
///
/// The type tests this replaces were not merely inelegant, they were wrong. Seven call sites asked
/// <c>is LocalFileSystem</c>, and <see cref="ViewModels.PanelViewModel.IsInsideArchive"/> asked
/// <c>is ZipArchiveFileSystem</c> - which is blind to <see cref="Archives.ArchiveFileSystem"/>, the
/// provider every non-ZIP format uses. Inside a TAR, 7z or RAR archive that check returned false,
/// so the guards on secure wipe, folder-size calculation and packing never fired and those
/// operations proceeded as though they were looking at a real disk.
///
/// Design follows the .NET guidance for flag enums: plural name (CA1714), zero value named
/// <see cref="None"/> (CA1008), powers of two, and a named combination for the common case
/// (<see cref="Local"/>). <c>HasFlag</c> is used at the call sites rather than hand-written bit
/// tests: since .NET Core 2.1 the JIT recognises it and emits the same bit test when the enum type
/// is known statically, which it always is here.
///
/// Flags are added when a provider genuinely differs, not in advance. Anything that would only
/// ever be a synonym for <see cref="NativePaths"/> is deliberately absent - a flag that always has
/// the same answer as another one is pretend generality, and it makes the next reader wonder which
/// of the two they should have used.
/// </summary>
[Flags]
public enum FileSystemCapabilities
{
    /// <summary>A purely virtual tree: nothing outside the provider's own API may touch it.
    /// Archives are this.</summary>
    None = 0,

    /// <summary>
    /// Paths handed to this provider are genuine OS filesystem paths, so <c>System.IO</c> and Win32
    /// calls may be used on them directly alongside the provider's own methods.
    ///
    /// This is what gates every side-channel operation in the app: stamping timestamps with
    /// <c>File.SetLastWriteTimeUtc</c>, walking a tree with <c>DirectoryInfo</c> to total its size,
    /// overwriting bytes in place to wipe a file, writing a new archive next to its sources, and
    /// renaming across two providers without copying. A provider without this flag may still be
    /// perfectly capable - it just cannot be operated on behind its own back.
    /// </summary>
    NativePaths = 1 << 0,

    /// <summary>
    /// Deletion can go to the Recycle Bin instead of being permanent. Deliberately separate from
    /// <see cref="NativePaths"/>: a network share has real OS paths yet no Recycle Bin, and
    /// <c>SHFileOperation</c> deletes there permanently while reporting success - a trap this
    /// codebase has already been caught by once.
    /// </summary>
    RecycleBin = 1 << 1,

    /// <summary>A <c>FileSystemWatcher</c> can be pointed at this provider's paths to get change
    /// notifications, rather than the panel having to poll or refresh manually.</summary>
    FileWatch = 1 << 2,

    /// <summary>Running <c>git</c> against a path from this provider is meaningful, so the panel
    /// can colour entries by their working-tree status.</summary>
    GitStatus = 1 << 3,

    /// <summary>Everything a real local filesystem offers. Named combination per the .NET flag-enum
    /// guidance, so the common case doesn't require callers to OR the parts together.</summary>
    Local = NativePaths | RecycleBin | FileWatch | GitStatus,
}
