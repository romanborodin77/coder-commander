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
    public int WindowWidth { get; set; } = 1200;
    public int WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public bool ConfirmDelete { get; set; } = true;
    public bool ConfirmOverwrite { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    public bool ShowFunctionButtons { get; set; } = true;
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

    // Terminal settings
    public bool TerminalVisible { get; set; } = false;
    public int TerminalHeight { get; set; } = 250;

    /// <summary>A <c>Terminal.Shells.ShellDescriptor.Id</c> ("cmd", "powershell", "pwsh", "gitbash",
    /// "wsl:&lt;distro&gt;"). <see cref="SettingsService.Validate"/> migrates the pre-rewrite
    /// "Cmd"/"PowerShell" tokens the first time a settings file written by an older version loads.</summary>
    public string DefaultShellType { get; set; } = "cmd";

    /// <summary>Restored tabs as <c>"{ShellId}|{Path}"</c> entries.</summary>
    /// <remarks><c>init</c>, not a plain get-only property like its four siblings above/below:
    /// <c>UiTests/TerminalSettingsMigrationTests.cs</c> constructs test fixtures with
    /// <c>new AppSettings { OpenTerminalTabs = [...] }</c>, which needs an accessor object-
    /// initializer syntax can target. Confirmed empirically that this does not reopen CA2227 (init
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
            var backupPath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
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
        if (s.TerminalHeight < 0) s.TerminalHeight = defaults.TerminalHeight;

        MigrateLegacyCompressionLevel(s);
        CleanArchiveCompression(s);
        CleanAlreadyCompressedExtensions(s);
        MigrateLegacyShellTokens(s);
        CleanConnections(s);
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
