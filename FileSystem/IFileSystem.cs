namespace CoderCommander.FileSystem;

public interface IFileSystem
{
    string Name { get; }

    Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default);

    Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default);

    Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default);

    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default);

    Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default);

    Task DeleteAsync(string path, bool recursive, CancellationToken ct = default);

    Task CreateDirectoryAsync(string path, CancellationToken ct = default);

    Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default);

    Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string path, CancellationToken ct = default);

    Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default);

    string GetRootPath(string path);
}

/// <summary>
/// Optional interface for file systems that support batch deletion more efficiently
/// than calling <see cref="IFileSystem.DeleteAsync"/> repeatedly.
/// </summary>
public interface IBatchDeletableFileSystem
{
    Task DeleteBatchAsync(IReadOnlyList<string> paths, bool recursive, CancellationToken ct = default);
}
