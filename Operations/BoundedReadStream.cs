// CA2213: _inner is intentionally not disposed — the caller owns the underlying stream
// and may read further slices from it after this wrapper reports EOF.
#pragma warning disable CA2213

namespace CoderCommander.Operations;

/// <summary>
/// Read-only view over a slice of an already-open stream: reads at most <c>length</c> bytes from
/// wherever the inner stream's position currently is, then reports EOF - the inner stream itself
/// is never disposed (<see cref="Dispose(bool)"/> is a no-op on it), so a caller can keep reading
/// further slices off the same stream afterward. This is what lets <see cref="SplitOperation"/>
/// open the source file exactly once and hand each part a bounded view over the same sequential
/// read position, instead of re-opening/seeking per part - which also makes it work over a source
/// <see cref="FileSystem.IFileSystem"/> whose <c>OpenReadAsync</c> stream isn't seekable.
/// </summary>
internal sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    public BoundedReadStream(Stream inner, long length)
    {
        _inner = inner;
        _remaining = length;
    }

    /// <summary>Bytes left to read before this slice reports EOF.</summary>
    public long Remaining => _remaining;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(count, _remaining);
        var read = _inner.Read(buffer, offset, toRead);
        _remaining -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_remaining <= 0) return 0;
        var toRead = (int)Math.Min(buffer.Length, _remaining);
        var read = await _inner.ReadAsync(buffer[..toRead], ct).ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Deliberately does NOT dispose _inner - see class doc comment. Still calls base.Dispose so
    // Stream's own disposed-state bookkeeping runs (CA2215) - Stream.Dispose(bool) itself is a
    // no-op body, this is purely about satisfying the base-call contract.
    protected override void Dispose(bool disposing) => base.Dispose(disposing);
}
