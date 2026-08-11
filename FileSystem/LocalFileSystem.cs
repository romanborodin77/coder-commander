using System.Runtime.InteropServices;
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

    /// <inheritdoc/>
    /// <remarks>
    /// The one provider that offers everything. Note that <see cref="FileSystemCapabilities.RecycleBin"/>
    /// is claimed for all local paths including UNC, which matches the previous behaviour exactly -
    /// the network-share case is handled inside <see cref="RecycleBinHelper"/>, which detects it and
    /// refuses rather than deleting permanently.
    /// </remarks>
    public FileSystemCapabilities Capabilities => FileSystemCapabilities.Local;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

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
                // ReparsePointGuard.SkipRecursion: a junction inside the tree is listed as itself,
                // never followed - see that type for why this matters for a recursive walk
                // specifically (a single-level listing, above, has no such flag and is unaffected).
                AttributesToSkip = (includeHidden ? 0 : FileAttributes.Hidden) | ReparsePointGuard.SkipRecursion
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
            // The OS call gets the (possibly \\?\-prefixed) accessible path; the FileEntry
            // returned to the caller always carries the original, unprefixed one - see LongPath's
            // own doc comment for why leaking the prefix into a path shown/reused elsewhere
            // would be its own bug, not a fix.
            var accessible = LongPath.EnsureAccessible(path);

            if (Directory.Exists(accessible))
            {
                var di = new DirectoryInfo(accessible);
                return FileEntry.FromFileSystemInfo(path, di);
            }

            if (File.Exists(accessible))
            {
                var fi = new FileInfo(accessible);
                return FileEntry.FromFileSystemInfo(path, fi);
            }

            return null;
        }, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var accessible = LongPath.EnsureAccessible(path);
            return File.Exists(accessible) || Directory.Exists(accessible);
        }, ct);

    public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(LongPath.EnsureAccessible(source), LongPath.EnsureAccessible(destination), overwrite);
        }, ct);

    public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var s = LongPath.EnsureAccessible(source);
            var d = LongPath.EnsureAccessible(destination);

            try
            {
                if (Directory.Exists(s))
                    Directory.Move(s, d);
                else
                    File.Move(s, d, overwrite);
            }
            catch (IOException) when (overwrite)
            {
                MoveWithBackupSwap(s, d);
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
            path = LongPath.EnsureAccessible(path);

            if (Directory.Exists(path))
            {
                // File.Delete/Directory.Delete throw UnauthorizedAccessException on a read-only
                // entry. Without this, a recursive delete over hundreds of files aborts the
                // instant it reaches the first read-only one - since .NET's traversal order is
                // unspecified, that leaves an unpredictable subset of the tree destroyed and no
                // way to tell what survived. Clearing ReadOnly first (same policy Explorer/Shift+Del
                // applies) lets the whole tree go in one pass instead.
                if (recursive)
                    ClearReadOnlyRecursive(path);

                try
                {
                    Directory.Delete(path, recursive);
                }
                catch (UnauthorizedAccessException) when (recursive)
                {
                    // .NET 8's Directory.Delete(recursive: true) can throw UnauthorizedAccessException
                    // for a directory containing a junction or a symlinked directory, even though it
                    // has by then already finished removing every child correctly - including safely
                    // un-linking the reparse-point child rather than following it into whatever it
                    // points at (confirmed empirically with a real junction: the linked target
                    // survives, untouched, either way). What the exception actually signals is that
                    // the now-empty top-level directory itself was not removed - also confirmed
                    // empirically: a second, non-recursive delete of it always succeeds immediately
                    // afterward. Retried here rather than swallowed blindly, so a genuine permission
                    // problem (a file that could not actually be removed, leaving the directory
                    // non-empty) still fails this retry and surfaces as a real error instead of being
                    // hidden by this catch.
                    Directory.Delete(path, recursive: false);
                }
            }
            else if (File.Exists(path))
            {
                ClearReadOnlyIfSet(path);
                File.Delete(path);
            }
        }, ct);

    /// <summary>Best-effort: a file whose attributes can't even be read/set (e.g. permission
    /// denied, not just read-only) is left alone here and surfaces its real error from the
    /// subsequent delete instead.</summary>
    private static void ClearReadOnlyIfSet(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leave it - Directory.Delete's own exception for this entry is the real signal.
        }
    }

    private static void ClearReadOnlyRecursive(string root)
    {
        try
        {
            // The SearchOption.AllDirectories shorthand has no way to skip reparse points, so this
            // is spelled out as EnumerationOptions instead - see ReparsePointGuard. Without it, a
            // junction inside the tree being deleted had its target's files silently stripped of
            // ReadOnly, which is a real attribute change outside the tree the caller asked to
            // delete, confirmed with a real junction before this fix.
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | ReparsePointGuard.SkipRecursion
            };
            foreach (var file in Directory.EnumerateFiles(root, "*", options))
                ClearReadOnlyIfSet(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Enumeration itself failed partway (e.g. an inaccessible subdirectory) - the
            // subsequent Directory.Delete still runs and surfaces whatever it hits.
        }
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(LongPath.EnsureAccessible(path));
        }, ct);

    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            File.SetAttributes(LongPath.EnsureAccessible(path), attributes);
        }, ct);

    /// <summary>
    /// Free/total bytes at <paramref name="path"/>'s volume. Goes through
    /// <c>GetDiskFreeSpaceExW</c> rather than <see cref="DriveInfo"/> because DriveInfo only
    /// enumerates lettered drives - a UNC destination (<c>\\server\share\...</c>) never matches
    /// any of them, so the old lookup silently fell through to (0,0) for every network
    /// destination. <see cref="Operations.UnpackOperation.RejectIfWouldExhaustDisk"/> treats
    /// freeBytes &lt;= 0 as "couldn't determine, skip the check" - so that fallback was quietly
    /// disabling the decompression-bomb guard for network destinations rather than reporting it
    /// as unknown. GetDiskFreeSpaceExW handles UNC paths directly and needs no drive-letter lookup.
    /// </summary>
    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // GetDiskFreeSpaceExW needs an existing directory - walk up to the nearest
                // ancestor that exists (the destination itself may not have been created yet).
                var existing = FindExistingAncestor(LongPath.EnsureAccessible(path));
                if (existing != null &&
                    GetDiskFreeSpaceEx(existing, out var freeAvailable, out var total, out _))
                {
                    return ((long)freeAvailable, (long)total);
                }
            }
            catch (Exception ex)
            {
                LogService.Warning($"Failed to get drive space for {path}: {ex.Message}");
            }
            return (0L, 0L);
        }, ct);

    private static string? FindExistingAncestor(string path)
    {
        var current = path;
        while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
            current = Path.GetDirectoryName(current);
        return string.IsNullOrEmpty(current) ? null : current;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        path = LongPath.EnsureAccessible(path);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan));
    }

    public async Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        destinationPath = LongPath.EnsureAccessible(destinationPath);
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
                // Hidden, not just oddly-named: without this, a large in-progress copy showed a
                // ".tmp-<32 hex chars>" entry sitting right next to the real files in the
                // destination folder for as long as the transfer took.
                try { File.SetAttributes(tempPath, FileAttributes.Hidden); } catch { /* cosmetic only */ }
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
