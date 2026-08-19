using System.Net.Http;

namespace CoderCommander.FileSystem.Remote;

/// <summary>
/// Wraps a response body stream with a per-read timeout, closing a gap left by
/// <see cref="HttpClient.SendAsync(HttpRequestMessage, HttpCompletionOption, CancellationToken)"/>
/// with <see cref="HttpCompletionOption.ResponseHeadersRead"/>: the <c>HttpClient.Timeout</c>
/// covers only up to the headers, so a server that trickles one byte per minute hangs the body
/// read indefinitely. This stream applies <see cref="RemoteLimits.RequestTimeout"/> to each
/// individual read via a linked <see cref="CancellationTokenSource"/>.
/// </summary>
internal sealed class TimeoutStream : Stream
{
    private readonly Stream _inner;
    private readonly TimeSpan _timeout;
    private readonly CancellationToken _externalCt;
    private readonly IDisposable? _extraDispose;
    private bool _disposed;

    public TimeoutStream(Stream inner, TimeSpan timeout, CancellationToken externalCt = default, IDisposable? extraDispose = null)
    {
        _inner = inner;
        _timeout = timeout;
        _externalCt = externalCt;
        _extraDispose = extraDispose;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanWrite => _inner.CanWrite;
    public override bool CanSeek => _inner.CanSeek;
    public override long Length => _inner.Length;
    public override long Position { get => _inner.Position; set => _inner.Position = value; }

    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_externalCt, cancellationToken);
        cts.CancelAfter(_timeout);
        try
        {
            return await _inner.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_externalCt.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"Remote: the server stopped sending data (no data within {_timeout.TotalSeconds:0} s)");
        }
    }

    public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_externalCt, cancellationToken);
        cts.CancelAfter(_timeout);
        try
        {
            await _inner.WriteAsync(buffer, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_externalCt.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new IOException($"Remote: write timed out after {_timeout.TotalSeconds:0} s");
        }
    }
    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => _inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                _inner.Dispose();
                _extraDispose?.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _inner.DisposeAsync().ConfigureAwait(false);
            _extraDispose?.Dispose();
        }
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
