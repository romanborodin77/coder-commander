using System.Runtime.InteropServices;

namespace CoderCommander.Services;

/// <summary>
/// Decoding of the <c>WM_DEVICECHANGE</c> payload, kept free of any window or timer so it can be
/// unit-tested directly. <see cref="DeviceChangeWatcher"/> is the part that needs a message loop.
/// </summary>
public static class DeviceChangeMessages
{
    public const int WmDeviceChange = 0x0219;

    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevTypVolume = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastVolume
    {
        public uint dbcv_size;
        public uint dbcv_devicetype;
        public uint dbcv_reserved;
        public uint dbcv_unitmask;
        public ushort dbcv_flags;
    }

    /// <summary>
    /// <c>true</c> when this message means a volume appeared or disappeared and the drive list
    /// should be re-read.
    ///
    /// Only arrival and remove-complete are treated as relevant. <c>DBT_DEVICEREMOVEPENDING</c> is
    /// deliberately ignored: it is a request that can still be vetoed, so acting on it would remove
    /// a button for a drive that is often still there.
    /// </summary>
    public static bool IsVolumeChange(nint wParam, nint lParam)
    {
        var evt = (int)wParam;
        if (evt != DbtDeviceArrival && evt != DbtDeviceRemoveComplete)
            return false;
        if (lParam == 0)
            return false;

        try
        {
            // Read the device type only - the header layout is identical across DEV_BROADCAST_*
            // variants, so this is safe before knowing which one it is.
            var deviceType = (uint)Marshal.ReadInt32(lParam, sizeof(uint));
            return deviceType == DbtDevTypVolume;
        }
        catch
        {
            // A malformed or already-freed lParam must not take the message loop down.
            return false;
        }
    }

    /// <summary>Drive letters named by a volume message, for logging. The refresh itself re-reads
    /// every drive rather than patching the named ones - a volume can be announced before it is
    /// mounted, so the letter alone doesn't say what is now true.</summary>
    public static IReadOnlyList<string> DecodeVolumeLetters(nint lParam)
    {
        if (lParam == 0) return [];
        try
        {
            var volume = Marshal.PtrToStructure<DevBroadcastVolume>(lParam);
            return DecodeUnitMask(volume.dbcv_unitmask);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Bit 0 = A:, bit 1 = B:, … bit 25 = Z:. Bits above 25 are not drive letters and are
    /// ignored rather than producing nonsense names.</summary>
    public static IReadOnlyList<string> DecodeUnitMask(uint unitMask)
    {
        var letters = new List<string>();
        for (var i = 0; i < 26; i++)
        {
            if ((unitMask & (1u << i)) != 0)
                letters.Add($"{(char)('A' + i)}:");
        }
        return letters;
    }
}

/// <summary>
/// Turns the burst of <c>WM_DEVICECHANGE</c> messages Windows sends for one physical action into a
/// single <see cref="DevicesChanged"/> event.
///
/// The burst is the whole reason this class exists: plugging in one USB stick produces a volume
/// arrival, usually a media change, and sometimes a repeat - refreshing on each would re-probe
/// every drive several times over.
///
/// Uses <see cref="System.Threading.Timer"/> rather than the WinForms one on purpose: the event
/// already has to be marshalled by subscribers (see <see cref="DevicesChanged"/>), and a
/// thread-pool timer makes the debounce testable without a message loop.
/// </summary>
public sealed class DeviceChangeWatcher : IDisposable
{
    private readonly TimeSpan _debounce;
    private readonly System.Threading.Timer _timer;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Raised once per settled burst. **Fires on a thread-pool thread** - subscribers touching
    /// WinForms controls must marshal with <c>Control.BeginInvoke</c>.
    /// </summary>
    public event EventHandler? DevicesChanged;

    /// <param name="debounceMilliseconds">Quiet period before the event fires. 400 ms comfortably
    /// covers the arrival/media-change pair without being noticeable to a user who just plugged
    /// something in.</param>
    public DeviceChangeWatcher(int debounceMilliseconds = 400)
    {
        _debounce = TimeSpan.FromMilliseconds(debounceMilliseconds);
        _timer = new System.Threading.Timer(OnDebounceElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Feed every window message here; returns <c>true</c> if it was a volume change this
    /// watcher took an interest in (for logging - the message must still be passed on to the base
    /// window procedure either way).</summary>
    public bool HandleMessage(int msg, nint wParam, nint lParam)
    {
        if (msg != DeviceChangeMessages.WmDeviceChange) return false;
        if (!DeviceChangeMessages.IsVolumeChange(wParam, lParam)) return false;

        var letters = DeviceChangeMessages.DecodeVolumeLetters(lParam);
        LogService.Info($"Device change: volume {(letters.Count > 0 ? string.Join(", ", letters) : "(unnamed)")}");
        Nudge();
        return true;
    }

    /// <summary>Restarts the quiet period. Exposed so a caller can fold other refresh triggers
    /// into the same debounce.</summary>
    public void Nudge()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? _)
    {
        lock (_lock)
        {
            if (_disposed) return;
        }
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _timer.Dispose();
    }
}
