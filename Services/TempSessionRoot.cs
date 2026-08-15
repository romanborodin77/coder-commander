namespace CoderCommander.Services;

/// <summary>
/// Generalized form of what <see cref="Viewers.ViewerTempSession"/> pioneered: one process-lifetime
/// temp folder per "session" that owns a set of materialized files, plus a startup sweep that
/// removes any prior run's session folder whose owning process is no longer alive. Extracted so a
/// second consumer (the VFS materialize layer - see <c>FileSystem.Materialization</c>) does not
/// hand-roll a second copy of the orphan-sweep logic.
///
/// <para>The thing actually worth sharing here is not the folder-naming code, it's the
/// <see cref="IsProcessAlive"/> rule: a wrong answer there means deleting a still-live temp file
/// out from under a second running instance (or, for the materialize layer, an archive a user's
/// unsaved edit depends on) - the one class of bug in this app where "just copy the ten lines"
/// would be a mistake, not a shortcut.</para>
///
/// <para><paramref name="category"/> (constructor) segments the tree by consumer -
/// <c>viewer/sessions/...</c> vs <c>materialize/sessions/...</c> - so a panel's materialized
/// archive (which must outlive every viewer window) and a viewer's materialized PDF (window-scoped)
/// are swept independently and never collide on a session id.</para>
/// </summary>
public sealed class TempSessionRoot : IDisposable
{
    public string RootPath { get; }

    private bool _disposed;

    public TempSessionRoot(string category)
    {
        RootPath = Path.Combine(SessionsRootFor(category), $"{Environment.ProcessId}-{Guid.NewGuid():N}");
    }

    private static string SessionsRootFor(string category) =>
        Path.Combine(DataDirectory.Root, category, "sessions");

    /// <summary>Allocates a fresh, empty subfolder for one materialized file. The caller writes
    /// exactly one file into it (name/extension of its own choosing, from an allow-list - never
    /// taken from user-controlled text) so nothing sharing a folder-level mapping (a WebView2
    /// virtual host) can ever expose a sibling materialized file.</summary>
    public string AllocateFileFolder()
    {
        var folder = Path.Combine(RootPath, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Best-effort cleanup for this session's folder. A process that outlived this one
    /// (WebView2, an external editor still holding the temp file open) can make the delete fail -
    /// that must not throw back into the owner's own Dispose; see <see cref="SweepOrphans"/> for
    /// why a failed delete here is never treated as an error.</summary>
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

    /// <summary>Called once at startup (<c>Program.Main</c>) per category to remove session folders
    /// left behind by a previous run that crashed or was killed before its own <see cref="Dispose"/>
    /// ran - otherwise every dev-loop restart (or every killed <c>UiTests</c> host) leaks one more
    /// materialized-file folder forever. Deletes every subfolder under <c>{category}/sessions</c>
    /// whose pid segment does not belong to a currently-running process; a folder for a still-live
    /// pid is left alone (it might be a second running instance). Errors deleting an individual
    /// orphan are swallowed per-folder so one locked folder doesn't stop the rest of the sweep.</summary>
    public static void SweepOrphans(string category)
    {
        var sessionsRoot = SessionsRootFor(category);
        if (!Directory.Exists(sessionsRoot)) return;

        foreach (var dir in Directory.EnumerateDirectories(sessionsRoot))
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
