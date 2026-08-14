using CoderCommander.Services;

namespace CoderCommander.Viewers;

/// <summary>
/// One per <see cref="WinForms.ViewerForm"/> window - the temp folder a WebView-backed format
/// materializes a non-local file into so WebView2 has something to navigate a URL to (see
/// <see cref="ViewerSource.MaterializeAsync"/> and its own doc comment for why a virtual-host
/// mapping over a real file, not <c>WebResourceRequested</c> streaming, is the chosen shape).
///
/// <para>Root is <see cref="DataDirectory.Root"/>, not <c>%TEMP%</c> - <see cref="DataDirectory"/>
/// honors <c>CODERCOMMANDER_DATA_DIR</c>, which is the same hook <c>UiTests/</c> already relies on
/// to sandbox a launch away from the operator's real profile. A session that ignored it would leak
/// a test run's materialized files into the real user's temp folder instead.</para>
///
/// <para>Folder shape: <c>{DataDirectory.Root}/viewer/sessions/{pid}-{guid}/{fileGuid}/</c> - one
/// window per top-level session folder, one subfolder per materialized file, so a virtual-host
/// mapping onto a subfolder only ever exposes that single materialized file (see
/// <see cref="AllocateFileFolder"/>).</para>
/// </summary>
public sealed class ViewerTempSession : IDisposable
{
    private static readonly string SessionsRoot = Path.Combine(DataDirectory.Root, "viewer", "sessions");

    public string RootPath { get; }

    private bool _disposed;

    public ViewerTempSession()
    {
        RootPath = Path.Combine(SessionsRoot, $"{Environment.ProcessId}-{Guid.NewGuid():N}");
    }

    /// <summary>Allocates a fresh, empty subfolder for one materialized file. The caller writes
    /// exactly one file into it (name/extension of its own choosing, from an allow-list - never
    /// taken from user-controlled text) and maps a virtual host onto this folder, not
    /// <see cref="RootPath"/>, so the mapping can never expose a sibling materialized file.</summary>
    public string AllocateFileFolder()
    {
        var folder = Path.Combine(RootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Best-effort cleanup for this window's session folder. A WebView2 process that
    /// outlived the killed test host (or a file still memory-mapped by the OS) can make the delete
    /// fail - that must not throw back into <c>ViewerForm.Dispose</c>, the same reasoning
    /// <see cref="SweepOrphans"/> below already documents for the startup sweep.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Best-effort - see class doc comment / SweepOrphans for why a failed delete here is
            // not treated as an error.
        }
    }

    /// <summary>Called once at startup (<c>Program.Main</c>) to remove session folders left behind
    /// by a previous run that crashed or was killed before its own <see cref="Dispose"/> ran -
    /// otherwise every dev-loop restart (or every killed <c>UiTests</c> host) leaks one more
    /// materialized-file folder forever. Deletes every subfolder under <c>viewer/sessions</c>
    /// whose pid segment does not belong to a currently-running process; a folder for a still-live
    /// pid is left alone (it might be a second running instance). Errors deleting an individual
    /// orphan are swallowed per-folder so one locked folder doesn't stop the rest of the sweep.</summary>
    public static void SweepOrphans()
    {
        if (!Directory.Exists(SessionsRoot)) return;

        foreach (var dir in Directory.EnumerateDirectories(SessionsRoot))
        {
            var name = Path.GetFileName(dir);
            var dash = name.IndexOf('-', StringComparison.Ordinal);
            if (dash <= 0 || !int.TryParse(name.AsSpan(0, dash), out var pid)) continue;

            try
            {
                if (IsProcessAlive(pid)) continue;
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // Best-effort sweep - a locked orphan is left for the next startup to retry.
            }
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            return !proc.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no such pid - definitely gone
        }
        catch (InvalidOperationException)
        {
            return false; // already exited
        }
    }
}
