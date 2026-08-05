namespace CoderCommander.FileSystem;

public interface IFileSystem
{
    /// <summary>Human-readable name of this file system provider (e.g. "Local", "ZIP", "TAR").</summary>
    string Name { get; }

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
