using CoderCommander.FileSystem;

namespace CoderCommander.Operations;

/// <summary>
/// Presents an ordered list of part files (already resolved, e.g. by <see cref="CombineOperation"/>)
/// as one logical read-only stream: reads drain the currently-open part, then lazily open the next
/// one in sequence once the current part is exhausted. Never seeks, never requires the destination
/// to know part boundaries - a single <see cref="IFileSystem.CopyFromStreamAsync"/> call against
/// this stream is what lets Combine reuse the same whole-file-write API every other operation uses,
/// including on providers with no <c>OpenWriteAsync</c>/no <c>Seek</c> at all.
/// </summary>
internal sealed class ConcatenatingReadStream : Stream
{
    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<string> _partPaths;
    private readonly Action<ReadOnlyMemory<byte>>? _onData;
    private readonly CancellationToken _ct;
    private int _index = -1;
    private Stream? _current;

    /// <param name="onData">Invoked with each chunk actually read, e.g. to both track byte counts
    /// for progress reporting and feed a running <see cref="System.IO.Hashing.Crc32"/> - a single
    /// callback so a caller needing both doesn't have to read the same bytes twice.</param>
    public ConcatenatingReadStream(IFileSystem fs, IReadOnlyList<string> partPaths, Action<ReadOnlyMemory<byte>>? onData, CancellationToken ct)
    {
        _fs = fs;
        _partPaths = partPaths;
        _onData = onData;
        _ct = ct;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        while (true)
        {
            if (_current == null && !await AdvanceAsync(ct).ConfigureAwait(false))
                return 0; // no more parts - EOF for the whole logical stream

            var read = await _current!.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read > 0)
            {
                _onData?.Invoke(buffer[..read]);
                return read;
            }

            // Current part exhausted - close it and loop to open the next one.
            await _current.DisposeAsync().ConfigureAwait(false);
            _current = null;
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    private async Task<bool> AdvanceAsync(CancellationToken ct)
    {
        _index++;
        if (_index >= _partPaths.Count)
            return false;

        _current = await _fs.OpenReadAsync(_partPaths[_index], ct).ConfigureAwait(false);
        return true;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _current?.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_current != null)
            await _current.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
