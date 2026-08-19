using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using CoderCommander.FileSystem;

#pragma warning disable CA1308 // Checksum hex strings are returned lowercase per documented API contract
namespace CoderCommander.Services;

/// <summary>
/// Computes CRC32, MD5, SHA1, SHA256 checksums over any <see cref="IFileSystem"/>, and exports
/// the results in standard <c>.sfv</c>/<c>.md5</c>/<c>.sha1</c>/<c>.sha256</c> formats.
///
/// <para><b>VFS-aware.</b> Unlike the previous inline <c>File.OpenRead</c> implementation, every
/// method opens the file through <see cref="IFileSystem.OpenReadAsync"/>, so checksums work inside
/// archives and remote connections, not only on local native paths.</para>
///
/// <para><b>Streamed, never buffered whole.</b> A multi-gigabyte file is read in 64&nbsp;KB chunks;
/// the hash is updated incrementally and the stream is never held in memory in its entirety. This
/// matches the streaming discipline already established by <see cref="Search.ContentSearcher"/> and
/// <c>SplitOperation</c>.</para>
///
/// <para><b>MD5/SHA1 are file-identity checksums, not a security boundary.</b> CA5350/CA5351 assume
/// every use of these algorithms is cryptographic; here they are user-selectable verification hashes
/// for file contents, so the warnings are suppressed rather than the user-facing choice removed.</para>
/// </summary>
public static class ChecksumService
{
    /// <summary>Chunk size for streaming reads — large enough that per-read overhead disappears,
    /// small enough that the buffer is a rounding error next to any real file.</summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// Computes a CRC32 checksum of the file at <paramref name="path"/> on <paramref name="fs"/>.
    /// </summary>
    /// <returns>Lowercase hex string, e.g. <c>"a1b2c3d4"</c>.</returns>
    public static async Task<string> ComputeCrc32Async(
        IFileSystem fs, string path, CancellationToken ct = default)
    {
        using var stream = await fs.OpenReadAsync(path, ct).ConfigureAwait(false);
        var crc = new Crc32();
        var buffer = new byte[ChunkSize];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct).ConfigureAwait(false)) > 0)
        {
            crc.Append(buffer.AsSpan(0, read));
        }
        return Convert.ToHexString(crc.GetCurrentHash()).ToLowerInvariant();
    }

    /// <summary>
    /// Computes an MD5 checksum of the file at <paramref name="path"/> on <paramref name="fs"/>.
    /// </summary>
    /// <returns>Lowercase hex string.</returns>
#pragma warning disable CA5350, CA5351
    public static async Task<string> ComputeMd5Async(
        IFileSystem fs, string path, CancellationToken ct = default)
        => await ComputeHashAlgorithmAsync(fs, path, MD5.Create, ct).ConfigureAwait(false);
#pragma warning restore CA5350, CA5351

    /// <summary>
    /// Computes a SHA-1 checksum of the file at <paramref name="path"/> on <paramref name="fs"/>.
    /// </summary>
    /// <returns>Lowercase hex string.</returns>
#pragma warning disable CA5350, CA5351
    public static async Task<string> ComputeSha1Async(
        IFileSystem fs, string path, CancellationToken ct = default)
        => await ComputeHashAlgorithmAsync(fs, path, SHA1.Create, ct).ConfigureAwait(false);
#pragma warning restore CA5350, CA5351

    /// <summary>
    /// Computes a SHA-256 checksum of the file at <paramref name="path"/> on <paramref name="fs"/>.
    /// </summary>
    /// <returns>Lowercase hex string.</returns>
    public static async Task<string> ComputeSha256Async(
        IFileSystem fs, string path, CancellationToken ct = default)
        => await ComputeHashAlgorithmAsync(fs, path, SHA256.Create, ct).ConfigureAwait(false);

    /// <summary>
    /// Computes a checksum using the specified <see cref="HashAlgorithm"/> factory, streaming the
    /// file in chunks rather than loading it whole.
    /// </summary>
    private static async Task<string> ComputeHashAlgorithmAsync(
        IFileSystem fs, string path, Func<HashAlgorithm> factory, CancellationToken ct)
    {
        using var stream = await fs.OpenReadAsync(path, ct).ConfigureAwait(false);
        using var algorithm = factory();
        var buffer = new byte[ChunkSize];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct).ConfigureAwait(false)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            algorithm.TransformBlock(buffer, 0, read, null, 0);
        }
        algorithm.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(algorithm.Hash ?? []).ToLowerInvariant();
    }

    // ── Export ──

    /// <summary>
    /// Writes a <c>.sfv</c>-format file (one <c>filename crc32</c> line per entry) to
    /// <paramref name="destPath"/> on <paramref name="fs"/>.
    /// </summary>
    public static async Task ExportSfvAsync(
        IFileSystem fs, string destPath, IReadOnlyList<(string Name, string Crc32)> entries,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("; Generated by CoderCommander");
        foreach (var (name, crc) in entries)
            sb.Append(name).Append(' ').AppendLine(crc);
        await WriteTextAsync(fs, destPath, sb.ToString(), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an <c>.md5</c>/<c>.sha1</c>/<c>.sha256</c>-format file (standard
    /// <c>md5sum</c>/<c>sha256sum</c> layout: <c>hash  *filename</c>) to <paramref name="destPath"/>
    /// on <paramref name="fs"/>.
    /// </summary>
    /// <param name="algoLabel">Algorithm name for the header comment (e.g. <c>"MD5"</c>,
    /// <c>"SHA256"</c>).</param>
    public static async Task ExportHashAsync(
        IFileSystem fs, string destPath, string algoLabel,
        IReadOnlyList<(string Name, string Hash)> entries,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.Append("; Generated by CoderCommander (").Append(algoLabel).AppendLine(")");
        foreach (var (name, hash) in entries)
            sb.Append(hash).Append(" *").AppendLine(name);
        await WriteTextAsync(fs, destPath, sb.ToString(), ct).ConfigureAwait(false);
    }

    /// <summary>Writes UTF-8 text to <paramref name="destPath"/> via
    /// <see cref="IFileSystem.CopyFromStreamAsync"/>, the VFS-neutral write path.</summary>
    private static async Task WriteTextAsync(
        IFileSystem fs, string destPath, string text, CancellationToken ct)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await fs.CopyFromStreamAsync(destPath, ms, ct).ConfigureAwait(false);
    }
}
