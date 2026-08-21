using System.Runtime.InteropServices;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// The app's view of which drives exist, kept off the UI thread.
///
/// The drive bar used to call <c>DriveInfo.GetDrives().Where(d =&gt; d.IsReady)</c> directly while
/// building itself. Both halves of that are traps: <c>IsReady</c> (and <c>VolumeLabel</c>, and
/// <c>TotalSize</c>) issue a device query that blocks for seconds on an optical drive spinning up
/// or a network share whose server is gone, and filtering on it means such a drive vanishes from
/// the bar entirely.
///
/// This class splits the work by cost. <c>GetLogicalDrives</c> and <c>GetDriveType</c> are register
/// reads and run inline, so every drive gets a button immediately. Everything that touches the
/// medium runs on a thread-pool thread with a per-drive timeout, and a drive that misses it is
/// reported <see cref="DriveProbeState.Unavailable"/> rather than dropped.
/// </summary>
public sealed class DriveCatalog
{
    /// <summary>How long one drive gets to answer before it's called unavailable. Deliberately
    /// short: this budget is paid per drive in parallel, and the only cost of being wrong is a
    /// button that says "unavailable" until the next refresh.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(1);

    private static readonly Lazy<DriveCatalog> Shared = new(() => new DriveCatalog());

    /// <summary>Process-wide instance. Both panels read the same snapshot, so a device change
    /// costs one probe pass rather than one per panel.</summary>
    public static DriveCatalog Instance => Shared.Value;

    private readonly object _lock = new();
    private IReadOnlyList<DriveEntry> _current = [];
    private Task? _inFlight;
    private bool _refreshQueued;

    /// <summary>
    /// Raised after <see cref="Current"/> changes - once when the cheap fields are in, and again
    /// when the probes finish.
    ///
    /// **Fires on a thread-pool thread, never guaranteed to be the UI thread.** Subscribers that
    /// touch WinForms controls must marshal with <c>Control.BeginInvoke</c> - the same contract
    /// <see cref="Terminal.Native.PtySession"/> documents for its own background-thread events, and
    /// for the same reason: a synchronous <c>Invoke</c> from here can deadlock against a UI thread
    /// that is itself waiting on this class.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Last known snapshot. Never blocks; empty until the first refresh completes.</summary>
    public IReadOnlyList<DriveEntry> Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>
    /// Re-reads the drive list. Safe to call from the UI thread - returns as soon as the cheap
    /// fields are published, while the slow probes continue in the background.
    ///
    /// Concurrent calls collapse: a refresh already running is not duplicated, and at most one
    /// further pass is queued behind it. A burst of device notifications therefore costs two
    /// passes at worst, not one per message.
    /// </summary>
    public Task RefreshAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_inFlight is { IsCompleted: false })
            {
                _refreshQueued = true;
                return _inFlight;
            }
            _inFlight = RunRefreshAsync(ct);
            return _inFlight;
        }
    }

    private async Task RunRefreshAsync(CancellationToken ct)
    {
        try
        {
            await RefreshOnceAsync(ct).ConfigureAwait(false);

            // Drain a refresh that arrived while this one was running - the device state it
            // reported may be newer than what we just probed.
            while (true)
            {
                lock (_lock)
                {
                    if (!_refreshQueued) return;
                    _refreshQueued = false;
                }
                await RefreshOnceAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown or an explicit cancel - the previous snapshot stays valid.
        }
        catch (Exception ex)
        {
            LogService.Error("Drive refresh failed", ex);
        }
    }

    private async Task RefreshOnceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Cheap pass: letters + type only.
        var pending = EnumerateCheap();

        // Publish it immediately only when the set of drives actually changed - that is what makes
        // a newly inserted stick get a button without waiting for any medium. When the set is the
        // same (a manual refresh, a repeated device notification, a network-mapping check),
        // publishing would replace fully probed entries with Pending ones and blank every label and
        // size on the bar for as long as the probes take, for no new information.
        //
        // Deliberately not carried the other way either: probed fields are never copied onto the
        // new snapshot by letter. A letter can be reused by a different volume, and a label that
        // belongs to a disk that is no longer there is worse than no label.
        if (!SameShape(pending))
            Publish(pending);

        if (pending.Count == 0)
            return;

        // Slow pass: all drives probed in parallel, each with its own timeout, so one dead share
        // costs one second rather than one second per drive in sequence.
        var probes = pending.Select(d => ProbeAsync(d, ct)).ToArray();
        var probed = await Task.WhenAll(probes).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        Publish(probed);
    }

    /// <summary>Letters and drive type only - no call here touches the medium.</summary>
    private static List<DriveEntry> EnumerateCheap()
    {
        var result = new List<DriveEntry>();
        string[] roots;
        try
        {
            roots = Directory.GetLogicalDrives();
        }
        catch (Exception ex)
        {
            LogService.Error("GetLogicalDrives failed", ex);
            return result;
        }

        foreach (var root in roots)
        {
            // GetDriveTypeW is a lookup in the mount table, not a device query - safe inline.
            var type = (DriveType)GetDriveType(root);
            result.Add(DriveEntry.Pending(root, type));
        }
        return result;
    }

    /// <summary>
    /// Fills in label and free space for one drive, giving up after <see cref="ProbeTimeout"/>.
    ///
    /// The timed-out probe's thread is abandoned, not cancelled: it is blocked inside a
    /// synchronous Win32 device call that no <see cref="CancellationToken"/> can interrupt. That
    /// is acceptable here because the thread does finish once the device answers or the driver
    /// gives up, it holds nothing but its own locals, and the alternative - waiting for it - is
    /// exactly the frozen UI this class exists to prevent.
    /// </summary>
    private static async Task<DriveEntry> ProbeAsync(DriveEntry drive, CancellationToken ct)
    {
        var probe = Task.Run(() => Probe(drive), ct);

        // Linked so the timeout delay is cancelled the moment probe wins - without this, every
        // successful (the common case) probe still left its 1-second Task.Delay timer armed for
        // its full duration, a small but permanent churn of timer-queue entries every time
        // DeviceChangeWatcher nudges a refresh.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var finished = await Task.WhenAny(probe, Task.Delay(ProbeTimeout, timeoutCts.Token)).ConfigureAwait(false);

        if (finished != probe)
            return drive with { ProbeState = DriveProbeState.Unavailable };

        timeoutCts.Cancel();

        try
        {
            return await probe.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return drive with { ProbeState = DriveProbeState.Unavailable };
        }
    }

    private static DriveEntry Probe(DriveEntry drive)
    {
        try
        {
            var info = new DriveInfo(drive.RootPath);
            if (!info.IsReady)
                return drive with { ProbeState = DriveProbeState.Unavailable };

            var label = info.VolumeLabel ?? string.Empty;

            // Free space via GetDiskFreeSpaceExW rather than DriveInfo.TotalSize, matching
            // LocalFileSystem.GetDriveSpaceAsync: DriveInfo only understands lettered local
            // drives and silently reports zeros elsewhere.
            long free = 0, total = 0;
            if (GetDiskFreeSpaceEx(drive.RootPath, out var freeAvailable, out var totalBytes, out _))
            {
                free = (long)freeAvailable;
                total = (long)totalBytes;
            }

            return drive with
            {
                Label = label,
                FreeBytes = free,
                TotalBytes = total,
                ProbeState = DriveProbeState.Ready,
            };
        }
        catch (Exception ex)
        {
            // An unreadable drive is an ordinary state (no disc, no card, share offline), not an
            // error worth an Error-level entry.
            LogService.Debug($"Drive probe failed for {drive.RootPath}: {ex.Message}");
            return drive with { ProbeState = DriveProbeState.Unavailable };
        }
    }

    /// <summary>Whether a freshly enumerated list names the same drives, of the same types, as the
    /// snapshot in hand - regardless of what the probes later found on them.</summary>
    private bool SameShape(IReadOnlyList<DriveEntry> candidate)
    {
        lock (_lock)
        {
            if (_current.Count != candidate.Count) return false;
            for (var i = 0; i < candidate.Count; i++)
            {
                if (!string.Equals(_current[i].RootPath, candidate[i].RootPath, StringComparison.OrdinalIgnoreCase) ||
                    _current[i].DriveType != candidate[i].DriveType)
                    return false;
            }
            return true;
        }
    }

    private void Publish(IReadOnlyList<DriveEntry> snapshot)
    {
        lock (_lock)
        {
            if (_current.SequenceEqual(snapshot))
                return;   // nothing changed - don't make every panel rebuild its bar for nothing
            _current = snapshot;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetDriveType(string lpRootPathName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}
