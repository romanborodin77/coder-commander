using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoderCommander.Services;

/// <summary>
/// Application settings persisted as JSON.
/// </summary>
public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "en";
    public bool ShowHidden { get; set; } = true;
    public bool ShowSystem { get; set; } = false;
    public string LeftPath { get; set; } = "";
    public string RightPath { get; set; } = "";

    /// <summary>Every open tab on the left side, in display order, as <c>"{flags}|{path}"</c>
    /// entries - flags first (not the path) because a path can itself contain <c>|</c> (an archive
    /// path, <c>archive.zip|inner</c>), so parsing must always be <c>Split('|', 2)</c>, never a
    /// bare <c>Split('|')</c>. <c>flags</c> is <c>"0"</c> for every tab today; reserved so a future
    /// locked-tab flag doesn't need a settings-format version bump. Empty on a settings file from
    /// before tabs existed (or if the user never opened a second tab) - <see cref="LeftPath"/>
    /// remains the fallback single-tab restore path for that case.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<string> LeftPanelTabs { get; init; } = new();

    /// <summary>Right-side counterpart of <see cref="LeftPanelTabs"/>.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<string> RightPanelTabs { get; init; } = new();

    /// <summary>Index into <see cref="LeftPanelTabs"/> that was active when the window closed.</summary>
    public int LeftActiveTabIndex { get; set; }

    /// <summary>Index into <see cref="RightPanelTabs"/> that was active when the window closed.</summary>
    public int RightActiveTabIndex { get; set; }
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    /// <summary>Last size of the Settings dialog itself (resizable since the Ф1 nav rewrite).
    /// 640x580, not the original fixed 560x520 (F140) - the nav column (176px) permanently
    /// narrowed every section's content width, and the densest section (Archives, with its
    /// extensions list + add/remove/restore row) needed more room than the old fixed size gave it
    /// to avoid its last row sitting right at the AutoScroll viewport's edge by default.</summary>
    public int SettingsWindowWidth { get; set; } = 640;
    public int SettingsWindowHeight { get; set; } = 580;
    public bool ConfirmDelete { get; set; } = true;
    public bool ConfirmOverwrite { get; set; } = true;
    public bool ViewerWordWrap { get; set; }
    public bool ViewerImageFitToWindow { get; set; } = true;
    /// <summary>Last-used universal viewer format id ("text"/"ascii"/"binary"/"hex"), restored
    /// the next time a file with no matched format (or with the viewer's own format group
    /// switched away from a matched format) is opened. Never a matched format's id (e.g.
    /// "image") - a matched format always wins for a file it recognizes regardless of this
    /// value, so persisting one here would make the next unrelated file default to a forced (and
    /// likely failing) decode in that format.</summary>
    public string ViewerLastMode { get; set; } = "text";

    /// <summary>Manual encoding override for Text mode (<see cref="EncodingCatalog"/> id, e.g.
    /// "windows-1251") - empty means autodetect via <see cref="TextEncodingDetector"/>. Never
    /// applies to ASCII/Binary/Hex, which don't decode through an <c>Encoding</c> at all.</summary>
    public string ViewerEncodingOverride { get; set; } = "";

    /// <summary>CSV delimiter: <c>"auto"</c> (detect via <c>CsvParser.DetectDelimiter</c>) or a
    /// single literal character (<c>","</c>, <c>";"</c>, <c>"\t"</c>, <c>"|"</c>).</summary>
    public string ViewerCsvDelimiter { get; set; } = "auto";

    /// <summary>Whether the CSV viewer treats the first row as column headers rather than data.</summary>
    public bool ViewerCsvHasHeader { get; set; } = true;

    /// <summary>Whether the F3 HTML viewer's "browser mode" executes script in the page it's
    /// showing. Off by default - <c>WinForms.Viewers.WebViewHost</c>'s security baseline resets
    /// this to false before every non-HTML format navigates regardless of this setting; only HTML
    /// format's own explicit toolbar toggle reads and writes it.</summary>
    public bool ViewerHtmlAllowScripts { get; set; }

    /// <summary>Whether Quick View (Ctrl+Q, Ф4) previews a file on a remote connection
    /// (FTP/SFTP/WebDAV). Off by default - unlike a deliberate F3 open, Quick View triggers on
    /// every arrow-key tick while browsing, and each one would materialize a fresh remote file
    /// over the network just to throw it away a moment later. Never consulted for a local path or
    /// an archive entry - those always preview.</summary>
    public bool QuickViewRemoteEnabled { get; set; }

    /// <summary>Main-toolbar button layout as an ordered list of <c>CommandIds</c> values (plus
    /// <c>Views.ToolbarButtonCatalog.Separator</c> for a divider) - F5.2. Empty means "use
    /// <c>ToolbarButtonCatalog.DefaultToolbarLayout</c>", so a settings file from before this
    /// feature existed shows the exact same toolbar it always did.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<string> ToolbarButtons { get; init; } = new();

    /// <summary>Function (F-key) bar layout - see <see cref="ToolbarButtons"/>. No separator entries
    /// (the function bar has never had one).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<string> FunctionBarButtons { get; init; } = new();

    public bool ShowStatusBar { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public bool ShowFunctionButtons { get; set; } = true;

    /// <summary>UI font family for panels, dialogs, and every <see cref="ThemePalette"/> font role
    /// that isn't <see cref="MonoFontFamily"/> (see <see cref="ThemePalette.CreateDark"/>'s
    /// <c>BuildFonts</c>). Empty means "use the built-in default" ("Segoe UI") - same empty-means-
    /// default convention as <see cref="ViewerEncodingOverride"/>.</summary>
    public string UiFontFamily { get; set; } = "";

    /// <summary>Base UI font size in points (what <see cref="ThemePalette.GridFont"/> uses
    /// directly; every other UI-family role keeps its own offset from the built-in 9pt base, so
    /// the dialog-chrome size hierarchy - Title 15pt/Subtitle 13pt/Section 10pt/body 9pt/hint
    /// 8.5pt - is preserved rather than collapsed to one flat size). 0 means "use the built-in
    /// default" (9pt).</summary>
    public double UiFontSize { get; set; }

    /// <summary>Monospace font family for the code editor, F3 text/hex viewer, and terminal.
    /// Empty means "use the built-in default" ("Consolas").</summary>
    public string MonoFontFamily { get; set; } = "";

    /// <summary>Monospace font size in points. 0 means "use the built-in default" (9.5pt).</summary>
    public double MonoFontSize { get; set; }

    /// <summary>When true, F3 launches <see cref="ExternalViewerPath"/> instead of the built-in
    /// <c>ViewerForm</c> - only for a file on a native-path filesystem (see
    /// <see cref="FileSystem.FileSystemCapabilities.NativePaths"/>); a file inside an archive or on
    /// a remote connection always uses the built-in viewer regardless of this setting, since an
    /// external process has no way to read through those.</summary>
    public bool ExternalViewerEnabled { get; set; }

    /// <summary>Full path to the external viewer executable. A missing/nonexistent path falls back
    /// to the built-in viewer silently (see <see cref="ExternalToolLauncher"/>) rather than failing
    /// F3 outright.</summary>
    public string ExternalViewerPath { get; set; } = "";

    /// <summary>Command-line arguments for <see cref="ExternalViewerPath"/>; <c>%1</c> is replaced
    /// with the quoted file path (appended if the template has no <c>%1</c> at all).</summary>
    public string ExternalViewerArgs { get; set; } = "%1";

    /// <summary>Same as <see cref="ExternalViewerEnabled"/>, for F4/<c>EditorForm</c>.</summary>
    public bool ExternalEditorEnabled { get; set; }

    /// <summary>Same as <see cref="ExternalViewerPath"/>, for F4/<c>EditorForm</c>.</summary>
    public string ExternalEditorPath { get; set; } = "";

    /// <summary>Same as <see cref="ExternalViewerArgs"/>, for F4/<c>EditorForm</c>.</summary>
    public string ExternalEditorArgs { get; set; } = "%1";

    /// <summary>App-hotkey rebinds - keyed by <c>Commands.HotkeyManager.HotkeyDef.Id</c> (stable
    /// per default binding, independent of its shortcut), valued by a chord string in
    /// <c>Terminal.Input.TerminalKeyBindings.FormatChord</c>'s format, or <c>""</c> for
    /// "explicitly unbound". Partial-override model, unlike <see cref="TerminalCustomKeyBindings"/>'s
    /// full-replacement "Custom" preset - an id missing here simply keeps its built-in default (see
    /// <c>HotkeyManager.ApplyOverrides</c>).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> CustomHotkeys { get; } = new(StringComparer.Ordinal);
    public bool FlatView { get; set; } = false;
    public string SortColumn { get; set; } = "Name";
    public bool SortDescending { get; set; } = false;
    public bool DirectoriesFirst { get; set; } = true;
    public bool CopyAttributes { get; set; } = true;
    public bool CopyTimestamps { get; set; } = true;
    public bool ShowExtensionInName { get; set; } = true;

    /// <summary>
    /// Legacy compression level (0=none, 1=fastest, 2=optimal) from before per-format compression
    /// existed. Kept only as a migration source - <see cref="SettingsService.Validate"/> folds it
    /// into <see cref="ArchiveCompression"/> and clears it, so the UI never reads or writes this
    /// directly. Nullable and unset by default so a fresh install has nothing to migrate.
    /// </summary>
    [JsonPropertyName("CompressionLevel")]
    public int? LegacyCompressionLevel { get; set; }

    /// <summary>
    /// Preferred compression per archive format, keyed by format id (e.g. "zip", "tar.gz") with
    /// the value being a <c>CompressionPreset</c> name (e.g. "Balanced"). A format with no entry
    /// here falls back to "Balanced" wherever it's resolved. Kept as plain strings (not the
    /// <c>CompressionPreset</c> enum) so this class has no dependency on the Archives namespace.
    /// </summary>
    /// <remarks>Get-only + <see cref="JsonObjectCreationHandlingAttribute"/>, not a settable
    /// property: <c>System.Text.Json</c> populates this existing instance via its indexer during
    /// deserialization instead of assigning a new one - confirmed empirically, since a get-only
    /// collection property is *silently* left at its initializer value (not an error) without this
    /// attribute. The initializer's <see cref="StringComparer.OrdinalIgnoreCase"/> is what
    /// <see cref="SettingsForm"/>'s working copy already used before this was a plain settable
    /// property with a whole-dictionary replace - populate-mode preserves it because it deserializes
    /// into this exact instance rather than constructing a fresh one.</remarks>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> ArchiveCompression { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When true, files whose extension is already compressed (see
    /// <see cref="AlreadyCompressedExtensions"/>) are stored without further compression
    /// regardless of the format's chosen preset.</summary>
    public bool SkipCompressionForCompressedFiles { get; set; } = true;

    /// <summary>Extensions considered already-compressed for <see cref="SkipCompressionForCompressedFiles"/>.
    /// Empty means "use the built-in default list" (see <c>PackOperation</c>).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<string> AlreadyCompressedExtensions { get; } = new();

    /// <summary>Format id (<see cref="CoderCommander.Archives.IArchiveFormat.Id"/>) the Pack
    /// dialog preselects for new archives - "zip", "tar", or "tar.gz".</summary>
    public string DefaultArchiveFormat { get; set; } = "zip";

    /// <summary>Initial checked state of <c>PackDialogForm</c>'s "delete originals after packing"
    /// checkbox (move semantics). The dialog itself always started unchecked before this existed -
    /// off by default here too, so an upgraded install behaves identically until the user opts in.</summary>
    public bool DeleteOriginalsAfterPack { get; set; } = false;

    /// <summary>Part size (bytes) <c>SplitDialogForm</c> preselects. 0 means "use the built-in
    /// default preset" (100 MB, see <c>SplitDialogForm</c>'s own preset list) rather than a
    /// literal zero-byte part - same sentinel convention as <see cref="UiFontSize"/>.</summary>
    public long DefaultSplitPartSizeBytes { get; set; } = 0;

    /// <summary>Initial checked state of <c>SplitDialogForm</c>'s "create .crc" checkbox.</summary>
    public bool SplitWriteCrcDefault { get; set; } = true;

    /// <summary>Initial checked state of <c>SplitDialogForm</c>'s "delete source after splitting"
    /// checkbox. Off by default - splitting is destination-additive by nature (TC's own default),
    /// deleting the source is an explicit opt-in.</summary>
    public bool DeleteOriginalsAfterSplit { get; set; } = false;

    /// <summary>Initial checked state of <c>CombineDialogForm</c>'s "delete parts after combining"
    /// checkbox.</summary>
    public bool DeleteOriginalsAfterCombine { get; set; } = false;

    /// <summary>Initial checked state of <c>CombineDialogForm</c>'s "verify against .crc" checkbox.
    /// On by default - verification is free once a sidecar exists (already computed while writing
    /// the combined file) and only warns on mismatch, never blocks.</summary>
    public bool VerifyCrcAfterCombine { get; set; } = true;

    // Terminal settings
    public bool TerminalVisible { get; set; } = false;
    public int TerminalHeight { get; set; } = 250;

    /// <summary>A <c>Terminal.Shells.ShellDescriptor.Id</c> ("cmd", "powershell", "pwsh", "gitbash",
    /// "wsl:&lt;distro&gt;"). <see cref="SettingsService.Validate"/> migrates the pre-rewrite
    /// "Cmd"/"PowerShell" tokens the first time a settings file written by an older version loads.</summary>
    public string DefaultShellType { get; set; } = "cmd";

    /// <summary>Restored tabs as <c>"{ShellId}|{Path}"</c> entries.</summary>
    /// <remarks><c>init</c>, not a plain get-only property like its four siblings above/below: an
    /// object-initializer construction site (<c>new AppSettings { OpenTerminalTabs = [...] }</c>)
    /// needs an accessor it can target. Confirmed empirically that this does not reopen CA2227 (init
    /// still isn't a public mutator reachable after construction) and does not fight
    /// <see cref="JsonObjectCreationHandlingAttribute"/> - <c>System.Text.Json</c> still populates
    /// this exact instance via <c>Add</c> during deserialization rather than calling <c>init</c>.</remarks>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<string> OpenTerminalTabs { get; init; } = new();
    public string? LastTerminalPath { get; set; }

    /// <summary>Whether navigating the active file panel pushes a <c>cd</c>-equivalent into the
    /// visible terminal tab: <c>"Never"</c> (tabs still seed from the panel's path when first
    /// created, but never afterward), <c>"OnOpen"</c> (push once when the terminal panel becomes
    /// visible, not on every subsequent panel navigation), <c>"Always"</c> (push on every
    /// navigation while the terminal is visible).</summary>
    public string TerminalFollowPanelCwd { get; set; } = "OnOpen";

    /// <summary>Key binding preset: <c>"WindowsTerminal"</c> (default), <c>"Classic"</c> (mirrors
    /// this app's pre-rewrite Ctrl+T/Ctrl+W/Ctrl+C/Ctrl+V layout), or <c>"Custom"</c> (user-edited,
    /// stored in <see cref="TerminalCustomKeyBindings"/>).</summary>
    public string TerminalKeyBindingPreset { get; set; } = "WindowsTerminal";

    /// <summary>Custom chord overrides when <see cref="TerminalKeyBindingPreset"/> is
    /// <c>"Custom"</c> - keyed by <c>Terminal.Input.TerminalAction</c> name, valued by a chord
    /// string in <c>Terminal.Input.TerminalKeyBindings.FormatChord</c>'s format (e.g.
    /// <c>"Ctrl+Shift+T"</c>). Starts as a copy of the WindowsTerminal preset when the user first
    /// switches to Custom (see <c>TerminalKeyBindingsForm</c>); an action missing from this
    /// dictionary is simply unbound, not defaulted.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, string> TerminalCustomKeyBindings { get; } = new();

    /// <summary>Whether a new PowerShell tab loads the user's profile (oh-my-posh, Starship,
    /// PSReadLine config, ...). Off trades a faster tab-open for a bare, un-customized prompt -
    /// on by default since a plain PowerShell prompt with no tab-completion history is what the
    /// pre-rewrite pipe-based terminal shipped, and this rewrite exists specifically to fix that.</summary>
    public bool TerminalLoadShellProfile { get; set; } = true;

    /// <summary>User-defined shells (Settings ▸ Terminal ▸ Custom Shells), merged by
    /// <see cref="Terminal.Shells.ShellCatalog"/> into the discovered list alongside the built-in
    /// ones. See <see cref="Models.CustomShellDefinition"/> for the resolution/security model.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Models.CustomShellDefinition> TerminalCustomShells { get; } = new();

    /// <summary>Saved remote connections. Contains no secrets by construction - see
    /// <see cref="Models.ConnectionProfile"/>; passwords live in <see cref="CredentialStore"/>,
    /// keyed by profile id, because this file is plain text.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public List<Models.ConnectionProfile> Connections { get; } = new();
}

/// <summary>
/// Loads and saves application settings.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(DataDirectory.Root, "settings.json");

    internal static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly object Lock = new();
    private static AppSettings? _cached;

    private const int MinWindowSize = 200;
    // Matches SettingsForm.MinimumSize - kept in sync there rather than referenced across the
    // Services/WinForms boundary, same as MinWindowSize's relationship to MainForm.
    private const int MinSettingsWindowWidth = 620;
    private const int MinSettingsWindowHeight = 480;

    // Matches SettingsForm's own FontDialog.MinSize/MaxSize bounds - kept in sync there rather
    // than referenced across the Services/WinForms boundary, same reasoning as the two constants
    // above. 0 (the "use built-in default" sentinel) is below MinFontSize by construction, so the
    // Validate() check below resets it to 0 too - a harmless no-op, not a special case.
    private const double MinFontSize = 6.0;
    private const double MaxFontSize = 36.0;

    public static AppSettings Load()
    {
        lock (Lock)
        {
            if (_cached != null) return _cached;

            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _cached = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
                }
                else
                {
                    _cached = new AppSettings();
                }
            }
            catch (Exception ex)
            {
                // Falling back to defaults here is the right recovery - an app that refuses to
                // start over a corrupt settings file is worse than one that starts with defaults -
                // but silently doing so was its own defect: the user loses every saved setting with
                // no record of what happened, and the very next Save() (on any change, or on close)
                // overwrites the only copy of whatever was actually in the file, so there would be
                // nothing left to look at even if someone thought to ask.
                LogService.Error("settings.json could not be read; falling back to defaults", ex);
                BackUpCorruptFile(SettingsPath);
                _cached = new AppSettings();
            }

            Validate(_cached);
            return _cached;
        }
    }

    /// <summary>
    /// Thread-safe read of <see cref="AppSettings.Connections"/>: a shallow copy taken under
    /// <see cref="Lock"/>, not the live list <see cref="Load"/> would otherwise hand out.
    ///
    /// <see cref="Services.ConnectionManager"/> is documented as never running on the UI thread and
    /// enumerates connections on every status refresh and every <c>ConnectAsync</c>/
    /// <c>AutoConnectAllAsync</c> call, while <see cref="WinForms.ConnectionsForm"/> mutates the
    /// same list on the UI thread. Handing out the raw <c>List&lt;ConnectionProfile&gt;</c> reference
    /// (what plain <c>Load().Connections</c> does) let those two race - an
    /// <see cref="InvalidOperationException"/> ("Collection was modified") thrown out of a
    /// background enumeration, or out of <see cref="Save"/>'s own <c>JsonSerializer.Serialize</c>
    /// call, aborting the write mid-flight.
    /// </summary>
    public static IReadOnlyList<Models.ConnectionProfile> SnapshotConnections()
    {
        lock (Lock)
        {
            return new List<Models.ConnectionProfile>(Load().Connections);
        }
    }

    /// <summary>
    /// Thread-safe mutation of <see cref="AppSettings.Connections"/>: <paramref name="mutate"/>
    /// runs against the live list under <see cref="Lock"/>, and the result is persisted before the
    /// lock is released - closing the same race <see cref="SnapshotConnections"/> exists to close,
    /// from the writer's side. <see cref="Lock"/> is acquired via the ordinary <c>lock</c>
    /// statement (<c>Monitor</c>-based, reentrant for the owning thread), so the nested
    /// <see cref="Load"/>/<see cref="Save"/> calls inside are safe.
    /// </summary>
    public static void MutateConnections(Action<List<Models.ConnectionProfile>> mutate)
    {
        lock (Lock)
        {
            var settings = Load();
            mutate(settings.Connections);
            Save(settings);
        }
    }

    /// <summary>Thread-safe mutation of <see cref="AppSettings.TerminalCustomShells"/> - same
    /// shape as <see cref="MutateConnections"/>. Does not itself invalidate
    /// <see cref="Terminal.Shells.ShellCatalog"/>'s cache - callers editing shells interactively
    /// (<c>CustomShellsForm</c>) do that themselves once the dialog closes.</summary>
    public static void MutateCustomShells(Action<List<Models.CustomShellDefinition>> mutate)
    {
        lock (Lock)
        {
            var settings = Load();
            mutate(settings.TerminalCustomShells);
            Save(settings);
        }
    }

    /// <summary>Replaces <see cref="AppSettings.ToolbarButtons"/> or
    /// <see cref="AppSettings.FunctionBarButtons"/> wholesale (F5.2's editor rebuilds the whole
    /// ordered layout at once, unlike <see cref="MutateCustomShells"/>'s incremental add/edit/remove) -
    /// same <see cref="Lock"/> discipline as every other settings mutation here.</summary>
    public static void SaveToolbarLayout(bool isFunctionBar, IReadOnlyList<string> layout)
    {
        lock (Lock)
        {
            var settings = Load();
            var target = isFunctionBar ? settings.FunctionBarButtons : settings.ToolbarButtons;
            target.Clear();
            target.AddRange(layout);
            Save(settings);
        }
    }

    /// <summary>
    /// Copies the unreadable file aside before it gets overwritten by the in-memory defaults on the
    /// next <see cref="Save"/>. Best-effort: a failure here (disk full, permission denied) is
    /// logged but must not replace or hide the original read failure that's already been logged by
    /// the caller.
    ///
    /// Internal and parameterized on the path, rather than reading <see cref="SettingsPath"/>
    /// itself, so it can be tested directly against a throwaway file - no test here may write to
    /// the operator's real settings file, and <see cref="Load"/> itself always reads that real
    /// path, leaving no other way to exercise this logic honestly.
    /// </summary>
    internal static void BackUpCorruptFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var backupPath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss.fff}";
            File.Copy(path, backupPath, overwrite: true);
            LogService.Warning($"Corrupt settings file preserved at {backupPath}");
        }
        catch (Exception ex)
        {
            LogService.Warning($"Could not back up corrupt settings file: {ex.Message}");
        }
    }

    /// <summary>Clamps fields that a hand-edited or otherwise corrupted (but JSON-valid) settings file
    /// could set to a value the UI can't recover from, e.g. a zero/negative window size. Internal
    /// (rather than private) so tests can exercise migration logic directly on an in-memory
    /// <see cref="AppSettings"/> instance without going through the real settings file on disk.</summary>
    internal static void Validate(AppSettings s)
    {
        var defaults = new AppSettings();
        if (s.WindowWidth < MinWindowSize) s.WindowWidth = defaults.WindowWidth;
        if (s.WindowHeight < MinWindowSize) s.WindowHeight = defaults.WindowHeight;
        if (s.SettingsWindowWidth < MinSettingsWindowWidth) s.SettingsWindowWidth = defaults.SettingsWindowWidth;
        if (s.SettingsWindowHeight < MinSettingsWindowHeight) s.SettingsWindowHeight = defaults.SettingsWindowHeight;
        if (s.TerminalHeight < 0) s.TerminalHeight = defaults.TerminalHeight;
        if (s.UiFontSize is < MinFontSize or > MaxFontSize) s.UiFontSize = 0;
        if (s.MonoFontSize is < MinFontSize or > MaxFontSize) s.MonoFontSize = 0;

        MigrateLegacyCompressionLevel(s);
        CleanArchiveCompression(s);
        CleanAlreadyCompressedExtensions(s);
        MigrateLegacyShellTokens(s);
        MigrateViewerLastMode(s);
        CleanConnections(s);
    }

    private static readonly string[] KnownUniversalViewerModes = { "text", "ascii", "binary", "hex" };

    /// <summary>Maps the pre-rewrite capitalized values ("Text"/"Hex") onto the lowercase format
    /// ids the universal viewer formats now use, and falls back an unrecognized value (a format
    /// id that no longer exists, or hand-edited garbage) to "text" rather than leaving the viewer
    /// unable to resolve its own last-mode preference. Idempotent - runs on every load AND save
    /// (see <see cref="Validate"/>'s callers), so an already-migrated value must round-trip
    /// unchanged.</summary>
    private static void MigrateViewerLastMode(AppSettings s)
    {
        s.ViewerLastMode = s.ViewerLastMode switch
        {
            "Text" => "text",
            "Hex" => "hex",
            var v when Array.IndexOf(KnownUniversalViewerModes, v) >= 0 => v,
            _ => "text"
        };
    }

    /// <summary>
    /// Drops connection profiles that could never be used, and repairs ones that are merely
    /// incomplete.
    ///
    /// A hand-edited or partially-written <c>settings.json</c> can produce a profile with no scheme
    /// or no URL; keeping it means a dead button in the places bar that fails on every click. An
    /// all-zero <see cref="Models.ConnectionProfile.Id"/> is worse than useless - every such profile
    /// would share one key in the credential store, so saving a password for one would silently
    /// hand it to the others. Both are repaired here rather than defended against at each use site.
    /// </summary>
    private static void CleanConnections(AppSettings s)
    {
        s.Connections.RemoveAll(c =>
            c is null || string.IsNullOrWhiteSpace(c.Scheme) || string.IsNullOrWhiteSpace(c.Url));

        foreach (var c in s.Connections.Where(c => c.Id == Guid.Empty))
            c.Id = Guid.NewGuid();

        // Defensive backstop: ConnectionEditForm.OnOk already refuses to save a URL with an
        // embedded "user:pass@" - this strips it anyway on every load, in case a profile reached
        // settings.json some other way (an older build, hand-editing the file, a future import
        // path). Url is serialised to disk in the clear; a credential living inside it defeats the
        // entire point of routing passwords through the DPAPI-encrypted CredentialStore instead.
        foreach (var c in s.Connections.Where(c => Uri.TryCreate(c.Url, UriKind.Absolute, out var u) && u.UserInfo.Length > 0))
        {
            var uri = new Uri(c.Url, UriKind.Absolute);
            var builder = new UriBuilder(uri) { UserName = "", Password = "" };
            c.Url = builder.Uri.ToString();
        }

        // AutoConnect with neither a saved password nor an anonymous login would pop a credential
        // prompt during startup, before the window is even usable. Clearing it is the conservative
        // repair: the connection stays configured, it just waits to be opened deliberately.
        foreach (var c in s.Connections.Where(c => c.AutoConnect && !c.SavePassword && c.UserName.Length > 0))
            c.AutoConnect = false;
    }

    /// <summary>Maps both legacy shell-token vocabularies onto the new stable
    /// <c>ShellDescriptor.Id</c> values ("cmd"/"powershell" - mirrored here as literals rather than
    /// referencing <c>Terminal.Shells.ShellIds</c>, matching <see cref="KnownCompressionPresets"/>'s
    /// own "no dependency on a higher-level namespace" reasoning, since Terminal already depends on
    /// Services). <see cref="AppSettings.DefaultShellType"/> used to store "Cmd"/"PowerShell";
    /// <see cref="AppSettings.OpenTerminalTabs"/> entries used "cmd.exe"/"PowerShell" - the two
    /// disagreed even with each other pre-rewrite. Idempotent: an already-new-style id (or an
    /// unrecognized custom/WSL id) passes through unchanged, so this is safe to run on every load.</summary>
    private static void MigrateLegacyShellTokens(AppSettings s)
    {
        s.DefaultShellType = MapLegacyShellToken(s.DefaultShellType);

        for (var i = 0; i < s.OpenTerminalTabs.Count; i++)
        {
            var parts = s.OpenTerminalTabs[i].Split('|', 2);
            if (parts.Length == 2)
                s.OpenTerminalTabs[i] = MapLegacyShellToken(parts[0]) + "|" + parts[1];
        }
    }

    private static string MapLegacyShellToken(string token) => token switch
    {
        "Cmd" or "cmd.exe" => "cmd",
        "PowerShell" => "powershell",
        _ => token
    };

    /// <summary>Mirrors <see cref="CoderCommander.Archives.CompressionPreset"/>'s member names as
    /// plain strings rather than referencing the enum itself, matching <see cref="AppSettings.ArchiveCompression"/>'s
    /// own "no dependency on the Archives namespace" design (see its doc comment) - Archives
    /// already depends on Services (LogService), so the reverse reference would be circular.</summary>
    private static readonly string[] KnownCompressionPresets = { "Store", "Fastest", "Balanced", "Maximum" };

    /// <summary>Drops entries whose value isn't a recognized preset name. The point of use
    /// (PackDialogForm/SettingsForm) already falls back gracefully via Enum.TryParse for an
    /// unrecognized value, but a hand-edited or stale settings.json would otherwise carry a dead
    /// entry around forever instead of it ever getting cleaned up.</summary>
    private static void CleanArchiveCompression(AppSettings s)
    {
        if (s.ArchiveCompression.Count == 0) return;

        var invalidKeys = s.ArchiveCompression
            .Where(kv => Array.IndexOf(KnownCompressionPresets, kv.Value) < 0)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in invalidKeys)
            s.ArchiveCompression.Remove(key);
    }

    /// <summary>Drops entries that can't be a real extension (blank, or missing the leading dot
    /// every other extension list in the app uses).</summary>
    private static void CleanAlreadyCompressedExtensions(AppSettings s) =>
        s.AlreadyCompressedExtensions.RemoveAll(ext => string.IsNullOrWhiteSpace(ext) || !ext.StartsWith('.'));

    /// <summary>
    /// Folds the old global 0/1/2 compression level into the new per-format
    /// <see cref="AppSettings.ArchiveCompression"/> the first time settings are loaded after an
    /// upgrade. Only ZIP existed when the legacy setting was meaningful, so it migrates there and
    /// nowhere else - other formats simply fall back to "Balanced" like a fresh install would.
    /// Runs only when <see cref="AppSettings.ArchiveCompression"/> is still empty, so a
    /// hand-edited settings file that already has explicit per-format entries is never overwritten.
    /// </summary>
    private static void MigrateLegacyCompressionLevel(AppSettings s)
    {
        if (s.ArchiveCompression.Count > 0 || s.LegacyCompressionLevel is not { } legacyLevel)
            return;

        s.ArchiveCompression["zip"] = legacyLevel switch
        {
            0 => "Store",
            1 => "Fastest",
            _ => "Balanced"
        };
        s.LegacyCompressionLevel = null;
    }

    public static void Save(AppSettings? settings = null)
    {
        lock (Lock)
        {
            _cached = settings ?? _cached ?? new AppSettings();
            Validate(_cached);
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                // Write-then-replace so a crash or a second instance saving concurrently can't leave
                // settings.json half-written.
                var tempPath = SettingsPath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(_cached, JsonOpts));
                File.Move(tempPath, SettingsPath, overwrite: true);
            }
            catch (Exception ex)
            {
                LogService.Error("Failed to save settings", ex);
            }
        }
    }

    public static string GetEffectiveTheme()
    {
        var s = Load();
        return s.Theme == "System"
            ? (IsSystemDark() ? "Dark" : "Light")
            : s.Theme;
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
