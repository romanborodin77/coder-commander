using System.Collections.Concurrent;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote.Ftp;

/// <summary>
/// A small pool of FTP control connections, and the thing that guarantees only one caller uses each.
///
/// <para><b>Why a pool and not one connection.</b> A control connection can carry exactly one
/// conversation, and a transfer occupies it from the moment the command is sent until the data
/// connection closes. With a single connection, refreshing a panel would block behind a download of
/// a large file. With no serialisation at all, two callers would read each other's replies. A few
/// connections, each used by one caller at a time, is the arrangement that avoids both.</para>
///
/// <para><b>Why idle connections are checked before reuse.</b> FTP servers close idle sessions,
/// typically after a few minutes, and a socket closed by the peer still reports itself connected
/// until something is written to it. A pooled connection that has been sitting is therefore pinged
/// before it is handed out, so a stale one becomes a fresh connection rather than a failed
/// operation the user sees.</para>
/// </summary>
internal sealed class FtpConnectionPool : IDisposable
{
    /// <summary>How long a pooled connection may sit before it is pinged rather than trusted.
    /// Well under the shortest idle timeout servers use in practice.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private readonly Func<FtpControlConnection> _factory;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentBag<FtpControlConnection> _idle = new();
    private volatile bool _disposed;

    public FtpConnectionPool(Func<FtpControlConnection> factory, int maxConnections)
    {
        _factory = factory;
        _slots = new SemaphoreSlim(maxConnections, maxConnections);
    }

    /// <summary>
    /// Takes a connection, opening one if the pool has room and none is idle. The caller must return
    /// it - see <see cref="FtpRental"/>, which does that in a <c>using</c>.
    /// </summary>
    public async Task<FtpControlConnection> RentAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (_idle.TryTake(out var pooled))
            {
                if (await IsAliveAsync(pooled, ct).ConfigureAwait(false)) return pooled;
                pooled.Dispose();
            }

            var fresh = _factory();
            try
            {
                await fresh.OpenAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                fresh.Dispose();
                throw;
            }
            return fresh;
        }
        catch
        {
            // The slot must come back on every failure path, or a run of failed connections
            // permanently shrinks the pool to nothing.
            try { _slots.Release(); } catch (ObjectDisposedException) { /* pool disposed during shutdown */ }
            throw;
        }
    }

    public void Return(FtpControlConnection connection)
    {
        if (_disposed || !connection.IsUsable)
        {
            try { connection.Dispose(); }
            catch { /* best-effort — one failing disposal must not leak the slot */ }
        }
        else
        {
            connection.ResetReadBuffer();
            _idle.Add(connection);
        }

        try
        {
            _slots.Release();
        }
        catch (ObjectDisposedException)
        {
            // The filesystem was disposed while a transfer was still running - closing the app
            // during a download does exactly this. The connection has just been torn down above and
            // nobody is waiting on the semaphore, so there is nothing left to release; throwing here
            // would surface as a crash inside a Dispose.
        }
    }

    private static async Task<bool> IsAliveAsync(FtpControlConnection connection, CancellationToken ct)
    {
        if (!connection.IsUsable) return false;
        if (DateTime.UtcNow - connection.LastUsedUtc < StaleAfter) return true;

        try
        {
            var reply = await connection.SendAsync("NOOP", ct).ConfigureAwait(false);
            return reply.IsSuccess;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Debug($"FTP: dropping a stale pooled connection ({ex.GetType().Name})");
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        while (_idle.TryTake(out var connection))
        {
            try { connection.Dispose(); }
            catch { /* best-effort — one failing connection must not leak the rest */ }
        }
        _slots.Dispose();
    }
}

/// <summary>A rented connection that returns itself. Exists so every call site is a <c>using</c>
/// and no path can forget to return one - a leaked rental permanently costs a pool slot.</summary>
internal readonly struct FtpRental : IDisposable
{
    private readonly FtpConnectionPool _pool;
    public FtpControlConnection Connection { get; }

    private FtpRental(FtpConnectionPool pool, FtpControlConnection connection)
    {
        _pool = pool;
        Connection = connection;
    }

    public static async Task<FtpRental> TakeAsync(FtpConnectionPool pool, CancellationToken ct) =>
        new(pool, await pool.RentAsync(ct).ConfigureAwait(false));

    public void Dispose() => _pool.Return(Connection);
}
