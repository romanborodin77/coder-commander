using System.Net.Sockets;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote.Ftp;

/// <summary>
/// The data connection of one FTP transfer, as a <see cref="Stream"/>.
///
/// <para><b>Closing it is part of the protocol, not just cleanup.</b> The server does not send its
/// "transfer complete" reply until it sees the end of the data, and that reply sits on the control
/// connection waiting to be read. Leaving it there would make the <i>next</i> command read this
/// transfer's reply as its own answer, and every command after that would be one step out of phase -
/// the classic way a hand-written FTP client appears to work and then corrupts a later operation.
/// So disposing this stream closes the data connection and then reads that reply.</para>
///
/// <para>The control connection stays rented for the whole life of this stream, because it cannot
/// carry anything else until the transfer ends.</para>
/// </summary>
internal sealed class FtpDataStream : Stream
{
    private readonly FtpControlConnection _control;
    private readonly TcpClient _client;
    private readonly Stream _inner;
    private readonly bool _expectFinalReply;
    private bool _finished;

    /// <summary>Invoked once the transfer is fully settled, so the owner can return the control
    /// connection to the pool. Set by the filesystem, which owns the rental.</summary>
    internal Action? Released { get; set; }

    internal FtpDataStream(FtpControlConnection control, TcpClient client, Stream inner, bool expectFinalReply)
    {
        _control = control;
        _client = client;
        _inner = inner;
        _expectFinalReply = expectFinalReply;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanWrite => _inner.CanWrite;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        _inner.ReadAsync(buffer, ct);

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
        _inner.WriteAsync(buffer, ct);

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!disposing) return;
        FinishAsync().GetAwaiter().GetResult();
    }

    public override async ValueTask DisposeAsync() => await FinishAsync().ConfigureAwait(false);

    /// <summary>
    /// Ends the transfer: closes the data connection, then reads the server's verdict.
    ///
    /// <para>A failing verdict is raised as an exception, because it is the only place the server can
    /// say an upload was rejected after it accepted every byte - out of quota, refused by a filter.
    /// Ignoring it would report a successful copy for a file that is not on the server.</para>
    /// </summary>
    private async Task FinishAsync()
    {
        if (_finished) return;
        _finished = true;

        try
        {
            // Closing the data connection is what tells the server the transfer is over.
            await _inner.DisposeAsync().ConfigureAwait(false);
            _client.Dispose();

            if (!_expectFinalReply) return;

            var reply = await _control.ReadTransferResultAsync(CancellationToken.None).ConfigureAwait(false);
            if (!reply.IsSuccess)
                throw new IOException($"FTP: transfer failed: {reply}");
        }
        finally
        {
            Released?.Invoke();
        }
    }
}
