using CoderCommander.FileSystem;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>Where a configured connection currently stands.</summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>The last attempt failed; <see cref="ConnectionStatus.Error"/> says why. Kept as a
    /// distinct state from <see cref="Disconnected"/> so the places bar can offer a retry instead
    /// of looking as though nothing was ever tried.</summary>
    Failed,
}

/// <summary>A profile plus its live state, as the places bar needs it.</summary>
public sealed record ConnectionStatus(
    Guid ProfileId,
    string Name,
    string Scheme,
    ConnectionState State,
    string Error,
    string RootPath);

/// <summary>
/// Owns every live remote connection and the state machine around it.
///
/// <para><b>Nothing here ever runs on the UI thread.</b> A connection attempt talks to a server
/// that may accept the socket and then say nothing; doing that on the UI thread is how an app
/// freezes on startup. <see cref="Changed"/> therefore fires on a thread-pool thread and
/// subscribers must marshal - the same contract <see cref="DriveCatalog"/> and
/// <c>TerminalSession</c> already state.</para>
///
/// <para><b>Auto-connect is best-effort and never blocks launch.</b> Every eligible profile is
/// attempted in parallel with its own timeout; a dead server costs nothing but a
/// <see cref="ConnectionState.Failed"/> entry with a message.</para>
/// </summary>
public sealed class ConnectionManager : IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, ConnectionState> _states = new();
    private readonly Dictionary<Guid, string> _errors = new();
    private readonly Dictionary<Guid, IFileSystem> _live = new();
    private readonly CredentialStore _credentials;
    private readonly CancellationTokenSource _shutdown = new();

    public static ConnectionManager Instance { get; } = new();

    /// <summary>Internal so tests can supply a credential store over a temp file.</summary>
    internal ConnectionManager(CredentialStore? credentials = null) =>
        _credentials = credentials ?? CredentialStore.Instance;

    /// <summary>Raised whenever any connection's state changes. **Fires on a thread-pool thread** -
    /// subscribers touching WinForms controls must marshal with <c>Control.BeginInvoke</c>.</summary>
    public event EventHandler? Changed;

    /// <summary>Every configured profile with its current state, in settings order.</summary>
    public IReadOnlyList<ConnectionStatus> Current
    {
        get
        {
            var profiles = SettingsService.Load().Connections;
            lock (_lock)
            {
                return profiles.Select(p => new ConnectionStatus(
                    p.Id,
                    p.DisplayName,
                    p.Scheme,
                    _states.GetValueOrDefault(p.Id, ConnectionState.Disconnected),
                    _errors.GetValueOrDefault(p.Id, ""),
                    RemotePath.Make(p.Scheme, AuthorityOf(p)))).ToList();
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="fs"/> is one of the filesystems this manager currently has open.
    ///
    /// <para>Asked by a panel deciding whether the filesystem it is holding belongs to a connection,
    /// which cannot be answered from the path: a panel can be holding a connection's filesystem
    /// while its path is still the local one it had before, and that combination is precisely the
    /// broken state worth detecting - it lists a server's contents under a local path.</para>
    /// </summary>
    public bool IsConnectionFileSystem(IFileSystem? fs)
    {
        if (fs is null) return false;
        lock (_lock) return _live.Values.Any(live => ReferenceEquals(live, fs));
    }

    /// <summary>The live filesystem for a connected profile, or <c>null</c>.</summary>
    public IFileSystem? GetConnected(Guid profileId)
    {
        lock (_lock) return _live.GetValueOrDefault(profileId);
    }

    /// <summary>
    /// The live filesystem that serves <paramref name="path"/>, matched by its <c>scheme://host</c>
    /// root, or <c>null</c> when nothing is connected there.
    ///
    /// <para>This is what lets a remote path arriving from anywhere - a copy destination, a
    /// hand-typed address - find the connection it belongs to, instead of being handed to the local
    /// filesystem because it happens to be the default. Two profiles pointing at the same host and
    /// port are indistinguishable by path and the first connected one wins; that ambiguity is
    /// inherent in addressing by host, and the alternative (a synthetic per-profile authority) would
    /// put an opaque identifier in every path the user sees.</para>
    /// </summary>
    public IFileSystem? GetConnectedForPath(string? path)
    {
        if (!RemotePath.IsRemote(path)) return null;

        var root = RemotePath.GetRoot(path!);
        foreach (var status in Current)
        {
            if (!string.Equals(status.RootPath, root, StringComparison.OrdinalIgnoreCase)) continue;
            if (GetConnected(status.ProfileId) is { } fs) return fs;
        }
        return null;
    }

    /// <summary>
    /// Connects, or returns the existing connection if there already is one.
    ///
    /// Concurrent calls for the same profile do not each open a connection: the second sees
    /// <see cref="ConnectionState.Connecting"/> and declines rather than racing. That matters
    /// because both panels and auto-connect can ask at the same moment.
    /// </summary>
    public async Task<IFileSystem?> ConnectAsync(Guid profileId, CancellationToken ct = default)
    {
        var profile = SettingsService.Load().Connections.FirstOrDefault(c => c.Id == profileId);
        if (profile is null) return null;

        lock (_lock)
        {
            if (_live.TryGetValue(profileId, out var existing)) return existing;
            if (_states.GetValueOrDefault(profileId) == ConnectionState.Connecting) return null;
            _states[profileId] = ConnectionState.Connecting;
            _errors.Remove(profileId);
        }
        Changed?.Invoke(this, EventArgs.Empty);

        try
        {
            var provider = FileSystemProviderRegistry.ByScheme(profile.Scheme)
                ?? throw new InvalidOperationException($"No provider for \"{profile.Scheme}\"");

            // The password is fetched here, not by the provider: the credential store stays the
            // one place that decides how a secret is obtained.
            var password = profile.SavePassword ? _credentials.TryGet(profileId) : null;

            // RequestTimeout, not ConnectTimeout, for the whole attempt. Opening a connection is one
            // request for WebDAV but a conversation for FTP - greeting, FEAT, AUTH TLS, the TLS
            // handshake, login, then a listing to verify - and holding all of that to the
            // ten-second budget meant for reaching a host would report a live but slow server as
            // failed. The fast-fail for a host that is not there comes from the provider's own TCP
            // connect budget, which is still ConnectTimeout, so a dead server still costs ten
            // seconds rather than thirty.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            linked.CancelAfter(FileSystem.Remote.RemoteLimits.RequestTimeout);

            var fs = await provider.ConnectAsync(profile, password, linked.Token).ConfigureAwait(false);

            lock (_lock)
            {
                _live[profileId] = fs;
                _states[profileId] = ConnectionState.Connected;
            }
            LogService.Info($"Connected: {profile.Scheme} \"{profile.DisplayName}\"");
            Changed?.Invoke(this, EventArgs.Empty);
            return fs;
        }
        catch (Exception ex)
        {
            // The message is kept for the tooltip, but never the URL or anything credential-shaped.
            var message = ex is OperationCanceledException ? "timed out" : ex.Message;
            lock (_lock)
            {
                _states[profileId] = ConnectionState.Failed;
                _errors[profileId] = message;
            }
            LogService.Warning($"Connection failed for \"{profile.DisplayName}\": {ex.GetType().Name}");
            Changed?.Invoke(this, EventArgs.Empty);
            return null;
        }
    }

    /// <summary>Closes a connection and disposes its provider. Idempotent.</summary>
    public void Disconnect(Guid profileId)
    {
        IFileSystem? fs;
        lock (_lock)
        {
            if (!_live.Remove(profileId, out fs)) return;
            _states[profileId] = ConnectionState.Disconnected;
            _errors.Remove(profileId);
        }

        (fs as IDisposable)?.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens every profile marked for auto-connect, in parallel.
    ///
    /// Failures are absorbed on purpose: this runs during startup, and one unreachable server must
    /// not produce an error dialog in front of an app the user just launched. The failure is
    /// visible in the places bar, where it can be retried deliberately.
    /// </summary>
    public async Task AutoConnectAllAsync(CancellationToken ct = default)
    {
        var eligible = SettingsService.Load().Connections
            .Where(c => c.AutoConnect)
            .Select(c => c.Id)
            .ToList();
        if (eligible.Count == 0) return;

        LogService.Info($"Auto-connecting {eligible.Count} connection(s)");
        await Task.WhenAll(eligible.Select(id => ConnectAsync(id, ct))).ConfigureAwait(false);
    }

    /// <summary>
    /// Drops live connections whose profile no longer exists, and forgets the state of the rest.
    /// Called after the connections dialog saves, so deleting a profile actually closes its
    /// connection instead of leaving an orphan holding a socket.
    /// </summary>
    public void SyncWithProfiles()
    {
        var live = SettingsService.Load().Connections.Select(c => c.Id).ToHashSet();
        List<Guid> removed;
        lock (_lock)
        {
            removed = _live.Keys.Where(id => !live.Contains(id)).ToList();
        }

        foreach (var id in removed)
            Disconnect(id);

        lock (_lock)
        {
            foreach (var id in _states.Keys.Where(id => !live.Contains(id)).ToList())
            {
                _states.Remove(id);
                _errors.Remove(id);
            }
        }
    }

    /// <summary>Authority used in this connection's app-side paths. Derived from the URL's host so
    /// the path stays readable; the connection's own base URL lives in its filesystem instance, so
    /// two profiles sharing a host still address their own roots correctly.</summary>
    private static string AuthorityOf(ConnectionProfile profile) =>
        Uri.TryCreate(profile.Url, UriKind.Absolute, out var uri)
            ? (uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}")
            : profile.Id.ToString("N");

    public void Dispose()
    {
        _shutdown.Cancel();

        List<IFileSystem> live;
        lock (_lock)
        {
            live = _live.Values.ToList();
            _live.Clear();
            _states.Clear();
        }
        foreach (var fs in live)
            (fs as IDisposable)?.Dispose();

        _shutdown.Dispose();
    }
}
