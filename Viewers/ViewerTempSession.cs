using CoderCommander.Services;

namespace CoderCommander.Viewers;

/// <summary>
/// One per <see cref="WinForms.ViewerForm"/> window - the temp folder a WebView-backed format
/// materializes a non-local file into so WebView2 has something to navigate a URL to (see
/// <see cref="Formats.MaterializingViewerLoader"/> for the "local file - just its own real
/// directory, non-local - read into memory and write here" split, and each WebView-backed content's
/// own doc comment for why a virtual-host mapping over a real file, not
/// <c>WebResourceRequested</c> streaming, is the chosen shape).
///
/// <para>Thin wrapper over <see cref="TempSessionRoot"/> (category <c>"viewer"</c>) - the folder
/// mechanics and the orphan-sweep's pid-liveness rule live there, shared with the VFS materialize
/// layer (<c>FileSystem.Materialization</c>) rather than duplicated. Public surface here is
/// unchanged from before the extraction, so no call site elsewhere needed to change.</para>
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
    private const string Category = "viewer";

    private readonly TempSessionRoot _root = new(Category);

    public string RootPath => _root.RootPath;

    /// <summary>Allocates a fresh, empty subfolder for one materialized file. The caller writes
    /// exactly one file into it (name/extension of its own choosing, from an allow-list - never
    /// taken from user-controlled text) and maps a virtual host onto this folder, not
    /// <see cref="RootPath"/>, so the mapping can never expose a sibling materialized file.</summary>
    public string AllocateFileFolder() => _root.AllocateFileFolder();

    /// <summary>Best-effort cleanup for this window's session folder. A WebView2 process that
    /// outlived the killed test host (or a file still memory-mapped by the OS) can make the delete
    /// fail - that must not throw back into <c>ViewerForm.Dispose</c>, the same reasoning
    /// <see cref="SweepOrphans"/> below already documents for the startup sweep.</summary>
    public void Dispose() => _root.Dispose();

    /// <summary>Called once at startup (<c>Program.Main</c>) to remove viewer session folders left
    /// behind by a previous run that crashed or was killed before its own <see cref="Dispose"/> ran
    /// - see <see cref="TempSessionRoot.SweepOrphans"/> for the full explanation.</summary>
    public static void SweepOrphans() => TempSessionRoot.SweepOrphans(Category);
}
