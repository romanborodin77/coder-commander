using CoderCommander.Services;
using CoderCommander.Utils;

namespace CoderCommander.FileSystem;

/// <summary>
/// Local filesystem implementation of IFileSystem.
/// </summary>
public sealed class LocalFileSystem : IFileSystem
{
    /// <inheritdoc/>
    public string Name => "Local";

    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
        {
            var result = new List<FileEntry>();

            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
                return result;

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden
            };

            foreach (var entry in dir.EnumerateFileSystemInfos("*", enumOptions))
            {
                ct.ThrowIfCancellationRequested();
                result.Add(FileEntry.FromFileSystemInfo(entry.FullName, entry));
            }

            return result;
        }, ct);

    // Flat View walks the entire subtree - potentially the whole drive if navigated to its root -
    // so this genuinely needs to run off the calling thread (usually the UI thread, for a
    // navigation triggered directly from a keypress/double-click) rather than just look async.
    public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
        {
            var result = new List<FileEntry>();

            var dir = new DirectoryInfo(path);
            if (!dir.Exists)
                return result;

            var enumOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden
            };

            foreach (var entry in dir.EnumerateFileSystemInfos("*", enumOptions))
            {
                ct.ThrowIfCancellationRequested();
                result.Add(FileEntry.FromFileSystemInfo(entry.FullName, entry));
            }

            return result;
        }, ct);

    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) =>
        Task.Run<FileEntry?>(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                return FileEntry.FromFileSystemInfo(path, di);
            }

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                return FileEntry.FromFileSystemInfo(path, fi);
            }

            return null;
        }, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return File.Exists(path) || Directory.Exists(path);
        }, ct);

    public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(source, destination, overwrite);
        }, ct);

    public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (Directory.Exists(source))
                    Directory.Move(source, destination);
                else
                    File.Move(source, destination, overwrite);
            }
            catch (IOException) when (overwrite)
            {
                MoveWithBackupSwap(source, destination);
            }
        }, ct);

    /// <summary>
    /// Overwrite fallback once the straightforward attempt above has already failed (e.g. a
    /// cross-volume File.Move(overwrite:true) that copies-then-deletes internally and can throw
    /// mid-way, commonly from disk space pressure while both copies transiently coexist).
    /// Renames the existing destination out of the way instead of deleting it outright - a
    /// retry that also fails restores the backup, so the operation never ends with the
    /// destination permanently gone and nothing having replaced it (the same "don't destroy the
    /// original until the replacement is confirmed" principle <see cref="CopyFromStreamAsync"/>
    /// already applies via its own temp-file-then-rename).
    /// </summary>
    private static void MoveWithBackupSwap(string source, string destination)
    {
        var destExists = File.Exists(destination) || Directory.Exists(destination);
        if (!destExists)
        {
            // The first attempt's failure wasn't actually about a conflicting destination -
            // nothing to preserve, just retry once plainly.
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
            return;
        }

        var backupPath = destination + ".bak-" + Guid.NewGuid().ToString("N");
        var destIsDirectory = Directory.Exists(destination);
        if (destIsDirectory)
            Directory.Move(destination, backupPath);
        else
            File.Move(destination, backupPath);

        try
        {
            if (Directory.Exists(source))
                Directory.Move(source, destination);
            else
                File.Move(source, destination);
        }
        catch
        {
            // The retry failed too - restore the original destination content instead of
            // leaving it renamed away with nothing having replaced it.
            try
            {
                if (destIsDirectory) Directory.Move(backupPath, destination);
                else File.Move(backupPath, destination);
            }
            catch { /* best effort - if even the restore fails, the content is still intact under backupPath */ }
            throw;
        }

        try
        {
            if (destIsDirectory) Directory.Delete(backupPath, recursive: true);
            else File.Delete(backupPath);
        }
        catch { /* best-effort cleanup - a leftover .bak-* file is harmless */ }
    }

    // Recursive delete of a large directory tree is just as capable of blocking the UI thread as
    // EnumerateDeepAsync above - same reasoning applies.
    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (Directory.Exists(path))
                Directory.Delete(path, recursive);
            else if (File.Exists(path))
                File.Delete(path);
        }, ct);

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(path);
        }, ct);

    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            File.SetAttributes(path, attributes);
        }, ct);

    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && DriveInfo.GetDrives().Any(d => string.Equals(d.Name, root, StringComparison.OrdinalIgnoreCase)))
                {
                    var drive = new DriveInfo(root);
                    return (drive.AvailableFreeSpace, drive.TotalSize);
                }
            }
            catch (Exception ex)
            {
                LogService.Warning($"Failed to get drive space for {path}: {ex.Message}");
            }
            return (0L, 0L);
        }, ct);

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    public async Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Write to a temp file and rename into place only once the copy fully succeeds - writing
        // directly into destinationPath with FileMode.Create truncates it immediately, so a
        // cancelled/failed copy while overwriting an existing file used to destroy the original
        // before any of the new content had actually landed.
        var tempPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var bufferSize = source.CanSeek ? BufferSizing.ForSize(source.Length) : 81920;
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(fs, bufferSize, ct).ConfigureAwait(false);
            }
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort cleanup */ }
            throw;
        }
    }

    public string GetRootPath(string path) => Path.GetPathRoot(path) ?? path;
}
