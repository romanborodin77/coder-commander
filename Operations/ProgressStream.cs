namespace CoderCommander.Operations;

/// <summary>Read-only pass-through stream that reports how many bytes flowed through it.</summary>
internal sealed class ProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<int> _onRead;

    /// <summary>Creates a pass-through stream that invokes <paramref name="onRead"/> with the byte count of each read operation.</summary>
    public ProgressStream(Stream inner, Action<int> onRead)
    {
        _inner = inner;
        _onRead = onRead;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => _inner.Length;

    /// <inheritdoc/>
    public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _inner.Read(buffer, offset, count);
        if (read > 0) _onRead(read);
        return read;
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        if (read > 0) _onRead(read);
        return read;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    /// <inheritdoc/>
    public override void Flush() { }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
