using System.Threading;
using CoderCommander.FileSystem;

namespace CoderCommander.Viewers;

/// <summary>
/// How a loader reads bytes, routed through <see cref="IFileSystem"/> rather than
/// <c>System.IO</c> directly - this is what makes F3 work on a file inside an archive or on an
/// SFTP/FTP/WebDAV connection, where the old <c>ViewerForm.LoadFileCore</c> called
/// <c>File.ReadAllBytes</c>/<c>File.Exists</c> straight on the path string.
/// </summary>
public sealed class ViewerSource
{
    public IFileSystem FileSystem { get; }
    public string Path { get; }

    /// <summary>True when <paramref name="Path"/> is a genuine OS path <c>System.IO</c> may touch
    /// directly - see <see cref="FileSystemCapabilities.NativePaths"/>. No loader in this phase
    /// needs it (every read goes through <see cref="FileSystem"/>), but it's the fact later
    /// phases (WebView2 materialization) will need to make their own decision, so it's exposed
    /// here rather than re-derived at each call site.</summary>
    public bool IsNative => FileSystem.Capabilities.HasFlag(FileSystemCapabilities.NativePaths);

    public ViewerSource(IFileSystem fileSystem, string path)
    {
        FileSystem = fileSystem;
        Path = path;
    }

    /// <summary>File size in bytes, or 0 if the entry can't be resolved (deleted out from under
    /// the viewer, a transient network failure, ...) - callers treat that the same way the old
    /// code treated a missing file at this point, as "nothing to show", not as a crash.</summary>
    public async Task<long> GetSizeAsync(CancellationToken ct)
    {
        var entry = await FileSystem.GetFileInfoAsync(Path, ct).ConfigureAwait(false);
        return entry?.Size ?? 0;
    }

    /// <summary>Reads the whole file into memory. Callers are expected to have already checked
    /// <see cref="GetSizeAsync"/> against a size limit before calling this - mirrors the old
    /// <c>LoadFileCore</c>'s own "check fileSize, then read" order, just spread across an async
    /// boundary.</summary>
    public async Task<byte[]> ReadAllBytesAsync(CancellationToken ct)
    {
        var stream = await FileSystem.OpenReadAsync(Path, ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            return ms.ToArray();
        }
    }

    /// <summary>Reads at most <paramref name="maxBytes"/> bytes from the start of the file,
    /// strictly forward - never seeks, because several providers (WebDAV, FTP) hand back a
    /// forward-only stream. Feeds the hex loader, replacing the old <c>ReadBoundedBytes</c>'s
    /// direct <c>new FileStream(...)</c>.</summary>
    public async Task<byte[]> ReadPrefixAsync(int maxBytes, CancellationToken ct)
    {
        if (maxBytes <= 0) return [];

        var stream = await FileSystem.OpenReadAsync(Path, ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var buffer = new byte[maxBytes];
            var total = 0;
            int read;
            while (total < maxBytes &&
                   (read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct).ConfigureAwait(false)) > 0)
            {
                total += read;
            }
            return total == maxBytes ? buffer : buffer[..total];
        }
    }
}
