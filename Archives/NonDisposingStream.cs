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
            try { _inner.CopyTo(Stream.Null); }
            catch { /* best effort - a broken reader can't do much worse than skip forward wrong */ }
        }
        // the real reader (TarReader/SharpCompress IReader) owns actually closing _inner
        base.Dispose(disposing);
    }
}
