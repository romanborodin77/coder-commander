namespace CoderCommander.FileSystem;

public interface IFileSystem
{
    /// <summary>Human-readable name of this file system provider (e.g. "Local", "ZIP", "TAR").</summary>
    string Name { get; }

    /// <summary>
    /// What this provider supports beyond the methods below - see
    /// <see cref="FileSystemCapabilities"/>. Callers must ask this rather than test for a concrete
    /// provider type: a type test silently answers "no" for any provider the author didn't think
    /// of, which is exactly how the archive guards came to be blind to every format except ZIP.
    ///
    /// Declared on the interface rather than probed through an optional one (the way
    /// <see cref="IBatchDeletableFileSystem"/> is) because every provider has an answer, and a
    /// missing implementation should be a compile error rather than a silent "supports nothing".
    /// </summary>
    FileSystemCapabilities Capabilities { get; }

    /// <summary>Enumerates immediate children of <paramref name="path"/>.</summary>
    Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default);

    /// <summary>Recursively enumerates all descendants of <paramref name="path"/>.</summary>
    Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default);

    /// <summary>Returns a single <see cref="FileEntry"/> for <paramref name="path"/>, or null if not found.</summary>
    Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default);

    /// <summary>Returns true when <paramref name="path"/> exists as a file or directory.</summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>Copies a file from <paramref name="source"/> to <paramref name="destination"/>.</summary>
    Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default);

    /// <summary>Moves or renames a file/directory from <paramref name="source"/> to <paramref name="destination"/>.</summary>
    Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default);

    /// <summary>Deletes a file or directory at <paramref name="path"/>.</summary>
    Task DeleteAsync(string path, bool recursive, CancellationToken ct = default);

    /// <summary>Creates all directories in <paramref name="path"/> that do not yet exist.</summary>
    Task CreateDirectoryAsync(string path, CancellationToken ct = default);

    /// <summary>Sets file-system attributes on the entry at <paramref name="path"/>.</summary>
    Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default);

    /// <summary>Returns free and total bytes for the drive that contains <paramref name="path"/>.</summary>
    Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default);

    /// <summary>Opens the file at <paramref name="path"/> for reading.</summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken ct = default);

    /// <summary>Writes <paramref name="source"/> to <paramref name="destinationPath"/>, creating parent directories as needed.</summary>
    Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default);

    /// <summary>Returns the root path (drive root or archive root) that contains <paramref name="path"/>.</summary>
    string GetRootPath(string path);
}

/// <summary>
/// Optional interface for file systems that support batch deletion more efficiently
/// than calling <see cref="IFileSystem.DeleteAsync"/> repeatedly.
/// </summary>
public interface IBatchDeletableFileSystem
{
    /// <summary>Deletes multiple entries in a single operation for better performance.</summary>
    Task DeleteBatchAsync(IReadOnlyList<string> paths, bool recursive, CancellationToken ct = default);
}

/// <summary>
/// Optional interface for file systems that can copy multiple entries out to another file system
/// in one sequential pass, more efficiently than calling <see cref="IFileSystem.OpenReadAsync"/>
/// once per file. The concrete motivating case is a sequential-only archive (TAR/TAR.GZ/7z/RAR
/// without random-access entry opening): each independent <c>OpenReadAsync</c> call there scans
/// from the start of the archive and discards everything before the target entry, so copying N
/// files out one at a time costs O(N x archive size) instead of the one O(archive size) pass this
/// interface allows.
/// </summary>
public interface IBatchReadableFileSystem
{
    /// <summary>
    /// Copies every entry in <paramref name="items"/> (this file system's own source path paired
    /// with its destination path on <paramref name="destFs"/>) in a single pass, in whatever order
    /// is cheapest for this provider - not necessarily <paramref name="items"/>' own order. A
    /// source path with no matching entry is skipped (logged, not thrown) rather than aborting the
    /// whole batch, matching every other partial-failure-tolerant operation in this codebase.
    /// <paramref name="onFileCopied"/> is invoked once a file's bytes have been fully written to
    /// its destination, with the entry's own source path and byte count, so the caller can report
    /// progress and apply attributes/timestamps the same way it would for a per-file copy.
    /// </summary>
    Task CopyManyToAsync(
        IReadOnlyList<(string SourcePath, string DestPath)> items,
        IFileSystem destFs,
        Func<string, long, CancellationToken, Task>? onFileCopied,
        CancellationToken ct = default);
}
