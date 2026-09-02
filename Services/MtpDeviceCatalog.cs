using MediaDevices;
using CoderCommander.FileSystem;

namespace CoderCommander.Services;

/// <summary>
/// Discovers MTP/PTP devices (Android phones, cameras, media players) connected via USB, using
/// <see cref="MediaDevice.GetDevices()"/> from the <c>MediaDevices</c> NuGet package.
///
/// <para>Shares the <see cref="DeviceChangeWatcher"/> debounce with <see cref="DriveCatalog"/>:
/// plugging in an Android phone generates a device-interface change (not a volume change), so the
/// watcher's <c>DBT_DEVICETYPE_VOLUME</c> filter ignores it. <see cref="MtpDeviceCatalog"/> polls
/// <c>GetDevices()</c> on a timer (every 3 seconds) as a pragmatic substitute — the WPD COM API
/// doesn't expose a notification sink without significantly more interop.</para>
/// </summary>
public sealed class MtpDeviceCatalog : IDisposable
{
    private static readonly Lazy<MtpDeviceCatalog> Shared = new(() => new MtpDeviceCatalog());
    public static MtpDeviceCatalog Instance => Shared.Value;

    private readonly object _lock = new();
    private IReadOnlyList<MtpDeviceInfo> _current = [];
    private readonly System.Threading.Timer _timer;
    private bool _disposed;

    /// <summary>Base poll interval (also the fast interval a device-set change resets back to, so
    /// a quick unplug/replug during an already-backed-off idle period is still caught promptly).</summary>
    private static readonly TimeSpan BaseInterval = TimeSpan.FromSeconds(3);

    /// <summary>Ceiling the interval backs off to on a machine with no MTP device ever attached -
    /// most of them. A machine that never has a device to enumerate used to pay a full WPD/COM
    /// <c>GetDevices()</c> round trip every 3 seconds for the app's entire lifetime.</summary>
    private static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(20);

    private TimeSpan _currentInterval = BaseInterval;

    /// <summary>Raised after <see cref="Current"/> changes. **Fires on a thread-pool thread.**</summary>
    public event EventHandler? Changed;

    /// <summary>Last known snapshot of connected MTP devices.</summary>
    public IReadOnlyList<MtpDeviceInfo> Current
    {
        get { lock (_lock) return _current; }
    }

    private MtpDeviceCatalog()
    {
        _timer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.Zero, BaseInterval);
    }

    /// <summary>Re-reads the device list immediately.</summary>
    public void Refresh()
    {
        IReadOnlyList<MtpDeviceInfo> snapshot;
        try
        {
            var devices = MediaDevice.GetDevices();
            try
            {
                snapshot = devices
                    .Select(d => new MtpDeviceInfo(d.DeviceId, d.FriendlyName))
                    .ToList();
            }
            finally
            {
                // MediaDevice implements IDisposable (WPD COM objects) and must be disposed after
                // its DeviceId/FriendlyName have been read, or every poll leaks one.
                //
                // Except for a device somebody is actually browsing. MediaDevices hands back an
                // instance representing the same underlying WPD device, and disposing it
                // disconnects that device - including the session a file panel is holding. So this
                // poll, running every three seconds, tore down the connection the user had just
                // opened: the first folder opened fine and everything attempted more than three
                // seconds later failed with "Not connected", which reads as "folders on the device
                // do not open". A device that is registered as in use is already known to be
                // present, which is the only thing this enumeration is trying to establish.
                foreach (var d in devices)
                {
                    if (MtpConnectionRegistry.Get(d.DeviceId) is not null) continue;
                    d.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warning($"MTP device enumeration failed: {ex.Message}");
            return;
        }

        bool changed;
        bool disposed;
        lock (_lock)
        {
            if (_disposed) return;
            changed = snapshot.Count != _current.Count ||
                !snapshot.SequenceEqual(_current);
            _current = snapshot;
            disposed = _disposed;
        }

        if (!disposed)
        {
            // Exponential-ish back-off (fixed 1s step, capped at MaxInterval) while the device set
            // stays unchanged - the common case for the entire session on a machine with no MTP
            // device attached. Any observed change resets straight back to BaseInterval so a
            // hot-plug is still caught promptly even right after backing off.
            _currentInterval = changed
                ? BaseInterval
                : TimeSpan.FromSeconds(Math.Min(_currentInterval.TotalSeconds + 1, MaxInterval.TotalSeconds));
            try { _timer.Change(_currentInterval, _currentInterval); }
            catch (ObjectDisposedException) { /* Dispose() raced this callback */ }
        }

        if (changed && !disposed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;

        // Dispose(WaitHandle) so a Refresh() already mid-flight (blocked in the WPD/COM
        // GetDevices() call) finishes before this returns, rather than potentially still running
        // after Dispose() completes. Bounded: shutdown must not hang indefinitely on a stuck
        // device enumeration. The plain Dispose() afterward is a safe no-op on an
        // already-disposed Timer - it's here only so CA2213's pattern match (which doesn't
        // recognise the WaitHandle overload as disposing the field) sees this field disposed.
        using var done = new ManualResetEvent(false);
        if (_timer.Dispose(done))
            done.WaitOne(TimeSpan.FromSeconds(2));
        _timer.Dispose();
    }
}

/// <summary>One discovered MTP device — its ID (for path construction) and friendly name (for UI).</summary>
public sealed record MtpDeviceInfo(string DeviceId, string FriendlyName)
{
    /// <summary>Display name: the friendly name if set, otherwise the device ID.</summary>
    public string DisplayName => string.IsNullOrEmpty(FriendlyName) ? DeviceId : FriendlyName;
}

/// <summary>
/// Process-wide registry of live MTP filesystems, keyed by device ID. Mirrors the role
/// <see cref="ConnectionManager"/> plays for WebDAV/FTP/SFTP/SMB: <c>PanelViewModel.AdoptFileSystemFor</c>
/// looks up a <c>mtp://</c> path here to find the filesystem that serves it, and
/// <c>MainForm.OnMtpDeviceActivated</c> registers a newly-connected device here.
/// </summary>
public static class MtpConnectionRegistry
{
    private static readonly Dictionary<string, IFileSystem> s_live = new();
    private static readonly object s_lock = new();

    /// <summary>Registers a live MTP filesystem for a device.</summary>
    public static void Register(string deviceId, IFileSystem fs)
    {
        lock (s_lock) s_live[deviceId] = fs;
    }

    /// <summary>Unregisters a device's filesystem.</summary>
    public static void Unregister(string deviceId)
    {
        lock (s_lock) s_live.Remove(deviceId);
    }

    /// <summary>The live filesystem for a device, or <c>null</c>.</summary>
    public static IFileSystem? Get(string deviceId)
    {
        lock (s_lock) return s_live.GetValueOrDefault(deviceId);
    }

    /// <summary>The live filesystem that serves <paramref name="path"/>, or <c>null</c>.</summary>
    public static IFileSystem? GetForPath(string? path)
    {
        if (!RemotePath.IsRemote(path)) return null;
        var scheme = RemotePath.SchemeOf(path);
        if (scheme != "mtp") return null;
        var host = RemotePath.HostOf(path!);
        return Get(host);
    }

    /// <summary>Whether <paramref name="fs"/> is one of the live MTP filesystems.</summary>
    public static bool IsMtpFileSystem(IFileSystem? fs)
    {
        if (fs is null) return false;
        lock (s_lock) return s_live.Values.Any(live => ReferenceEquals(live, fs));
    }

    /// <summary>Disposes all live MTP filesystems and clears the registry.
    /// Called on application shutdown to release WPD COM objects.</summary>
    public static void DisposeAll()
    {
        List<IFileSystem>? toDispose;
        lock (s_lock)
        {
            toDispose = s_live.Values.ToList();
            s_live.Clear();
        }
        foreach (var fs in toDispose)
            (fs as IDisposable)?.Dispose();
    }
}
