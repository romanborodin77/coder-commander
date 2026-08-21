// CA2213: _inner is intentionally not disposed — the owning reader (TarReader/SharpCompress)
// manages its lifetime. See class doc comment.
#pragma warning disable CA2213

namespace CoderCommander.Archives;

/// <summary>
/// Wraps a stream whose lifetime is owned by something other than the caller - e.g.
/// <see cref="System.Formats.Tar.TarReader"/> or a SharpCompress <c>IReader</c>, both of which
/// inspect the previous entry's stream when asked for the next entry and so require it to stay
/// alive until then. Every <see cref="IArchiveReader.ScanAsync"/> consumer disposes the yielded
/// <see cref="ArchiveEntryStream.Content"/> as a matter of course (it has no way to know which
/// formats need this), so sequential readers wrap their entry streams in this before yielding
/// them, and the real underlying reader manages the actual disposal once it moves on.
/// <para>
/// Disposing also drains any unread bytes first: <c>TarReader</c> auto-skips leftover data on its
/// own when asked for the next entry (draining here is redundant-but-harmless for it), but
/// SharpCompress's <c>IReader</c> does NOT - if a consumer skips an entry (wrong index, encrypted,
/// not selected) and disposes its stream without reading it, <c>MoveToNextEntry()</c> silently
/// fails to advance afterwards and the rest of the archive is lost. Draining here means consumer
/// code never needs to know which formats have this quirk.
/// </para>
/// </summary>
internal sealed class NonDisposingStream : Stream
{
    private readonly Stream _inner;

    public NonDisposingStream(Stream inner) => _inner = inner;

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }

    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        _inner.ReadAsync(buffer, offset, count, ct);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        _inner.ReadAsync(buffer, ct);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Drain remaining data from the inner stream — SharpCompress IReader doesn't advance
            // past the current entry until it's fully read, so skipping without draining leaves
            // the reader stuck. This is NOT actually bounded (an earlier version of this comment
            // claimed it was): skipping a large entry in a solid/compressed archive means fully
            // decompressing it either way, whether via this loop or CopyTo(Stream.Null) - there is
            // no cheaper way to advance a forward-only decompressor past data it hasn't read yet.
            // ArrayPool avoids at least the one incidental cost that WAS avoidable: an 80 KB array
            // allocated fresh on every single skipped entry (encrypted/unselected/wrong-index),
            // which for a large selection is one allocation per entry never even opened.
            var drainBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                while (_inner.Read(drainBuffer, 0, drainBuffer.Length) > 0) { /* drain */ }
            }
            catch { /* best effort - a broken reader can't do much worse than skip forward wrong */ }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(drainBuffer);
            }
        }
        // the real reader (TarReader/SharpCompress IReader) owns actually closing _inner
        base.Dispose(disposing);
    }
}
