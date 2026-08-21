using System.Formats.Tar;
using System.IO.Compression;
using CoderCommander.Utils;

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
        ct.ThrowIfCancellationRequested();

        // TarWriter.ValidateStreamsSeekability (verified directly against System.Formats.Tar, not
        // just inferred from docs) requires AT LEAST ONE of {entry DataStream, archive output
        // stream} to be seekable, throwing IOException otherwise. The archive output stream here
        // is _gzip ?? the caller's stream; GZipStream.CanSeek is always false, so writing into a
        // gzip-wrapped archive with non-seekable content leaves no side able to satisfy that
        // requirement except a genuinely buffered entry stream.
        //
        // Both fast-path branches below avoid the temp file this method used to buffer EVERY
        // entry through unconditionally - a 300,000-entry archive rewrite used to create, write,
        // read and delete 300,000 temp files on the system drive for unchanged survivors alone:
        if (content.CanSeek)
        {
            // Already seekable (e.g. a MemoryStream from a gzip-sourced ArchiveReader's
            // copyData:true buffering after this round's TarArchiveReader fix, or a local file
            // opened directly) - satisfies TarWriter on its own, no wrapping needed at all.
            var entry = new PaxTarEntry(TarEntryType.RegularFile, NormalizeName(entryName, isDirectory: false))
            {
                ModificationTime = ToTimestamp(lastWriteTimeUtc),
                DataStream = content
            };
            _writer.WriteEntry(entry);
            return;
        }

        if (_gzip == null)
        {
            // Archive output isn't gzip-wrapped (plain TAR - the common case for a staging file
            // before it's gzip-wrapped, and for TarArchiveFormat itself): the archive side already
            // satisfies TarWriter's seekability requirement, so the entry side can stay a cheap
            // Length-reporting, non-owning wrapper around the caller's own (borrowed) content -
            // never disposed here, matching who owned `content` before this method ever ran.
            var entry = new PaxTarEntry(TarEntryType.RegularFile, NormalizeName(entryName, isDirectory: false))
            {
                ModificationTime = ToTimestamp(lastWriteTimeUtc),
                DataStream = new KnownLengthStream(content, size)
            };
            _writer.WriteEntry(entry);
            return;
        }

        // Both content and the archive output are non-seekable - TarWriter refuses that
        // combination outright, so buffer through a temp file (this method's original behavior,
        // now the fallback rather than the unconditional path).
        var tempPath = TempFileNaming.InSystemTemp("tarwrite");
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

    /// <summary>
    /// Reports a caller-supplied <see cref="Length"/> instead of asking the wrapped stream for one
    /// - <see cref="CanSeek"/> is always false, so nothing should ever call <see cref="Seek"/> or
    /// the <see cref="Position"/> setter; both throw if something unexpectedly does, the same
    /// fail-loud contract every other non-seekable stream wrapper in this codebase follows
    /// (<see cref="Operations.ProgressStream"/>, <see cref="NonDisposingStream"/>).
    /// </summary>
    private sealed class KnownLengthStream : Stream
    {
#pragma warning disable CA2213 // pass-through: the caller owns _inner's lifetime, same as ProgressStream/NonDisposingStream
        private readonly Stream _inner;
#pragma warning restore CA2213
        private readonly long _length;

        public KnownLengthStream(Stream inner, long length)
        {
            _inner = inner;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            _inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            _inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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
