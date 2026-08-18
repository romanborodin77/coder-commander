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

    /// <summary>Raised after <see cref="Current"/> changes. **Fires on a thread-pool thread.**</summary>
    public event EventHandler? Changed;

    /// <summary>Last known snapshot of connected MTP devices.</summary>
    public IReadOnlyList<MtpDeviceInfo> Current
    {
        get { lock (_lock) return _current; }
    }

    private MtpDeviceCatalog()
    {
        _timer = new System.Threading.Timer(_ => Refresh(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    /// <summary>Re-reads the device list immediately.</summary>
    public void Refresh()
    {
        IReadOnlyList<MtpDeviceInfo> snapshot;
        try
        {
            snapshot = MediaDevice.GetDevices()
                .Select(d => new MtpDeviceInfo(d.DeviceId, d.FriendlyName))
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Warning($"MTP device enumeration failed: {ex.Message}");
            return;
        }

        bool changed;
        lock (_lock)
        {
            if (_disposed) return;
            changed = snapshot.Count != _current.Count ||
                !snapshot.SequenceEqual(_current);
            _current = snapshot;
        }
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_lock) _disposed = true;
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
}
