using System.Formats.Tar;
using System.IO.Compression;

namespace CoderCommander.Archives.Tar;

/// <summary>
/// Writes TAR entries to an output <see cref="Stream"/>, optionally gzip-wrapped - the primitive
/// <see cref="RewritingArchiveWriter"/> uses for both the staging file and the final rewritten
/// archive. New entries are always written in PAX format (unicode names, arbitrary length,
/// unclamped timestamps), regardless of what format the entries being copied across were
/// originally written in - <see cref="TarReader"/> reads any of V7/Ustar/PAX/GNU transparently.
/// </summary>
internal sealed class TarSequentialWriter : ISequentialArchiveWriter
{
    private readonly Stream? _gzip;
    private readonly TarWriter _writer;

    public TarSequentialWriter(Stream output, bool gzip)
    {
        _gzip = gzip ? new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true) : null;
        _writer = new TarWriter(_gzip ?? output, TarEntryFormat.Pax, leaveOpen: true);
    }

    public void WriteDirectory(string entryName, DateTime lastWriteTimeUtc)
    {
        var entry = new PaxTarEntry(TarEntryType.Directory, NormalizeName(entryName, isDirectory: true))
        {
            ModificationTime = ToTimestamp(lastWriteTimeUtc)
        };
        _writer.WriteEntry(entry);
    }

    public async Task WriteFileAsync(
        string entryName,
        Stream content,
        long size,
        DateTime lastWriteTimeUtc,
        ArchiveCompressionSpec compression,
        CancellationToken ct)
    {
        // TarEntry needs a seekable DataStream to determine its length; the caller's stream (a
        // ProgressStream wrapping a possibly non-seekable source) isn't guaranteed to be one, so
        // buffer through a temp file rather than assume.
        var tempPath = Path.GetTempFileName();
        try
        {
            using (var tempStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await content.CopyToAsync(tempStream, ct).ConfigureAwait(false);

            var entry = new PaxTarEntry(TarEntryType.RegularFile, NormalizeName(entryName, isDirectory: false))
            {
                ModificationTime = ToTimestamp(lastWriteTimeUtc)
            };
            using (var readStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                entry.DataStream = readStream;
                _writer.WriteEntry(entry);
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort */ }
        }
    }

    private static string NormalizeName(string name, bool isDirectory)
    {
        var normalized = name.Replace('\\', '/').Trim('/');
        return isDirectory ? normalized + "/" : normalized;
    }

    private static DateTimeOffset ToTimestamp(DateTime value) =>
        new(value == default ? DateTime.UtcNow : DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public void Dispose()
    {
        _writer.Dispose();
        _gzip?.Dispose();
    }
}
