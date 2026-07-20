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
    public Dictionary<string, string> ArchiveCompression { get; set; } = new();

    /// <summary>When true, files whose extension is already compressed (see
    /// <see cref="AlreadyCompressedExtensions"/>) are stored without further compression
    /// regardless of the format's chosen preset.</summary>
    public bool SkipCompressionForCompressedFiles { get; set; } = true;

    /// <summary>Extensions considered already-compressed for <see cref="SkipCompressionForCompressedFiles"/>.
    /// Empty means "use the built-in default list" (see <c>PackOperation</c>).</summary>
    public List<string> AlreadyCompressedExtensions { get; set; } = new();

    /// <summary>Format id (<see cref="CoderCommander.Archives.IArchiveFormat.Id"/>) the Pack
    /// dialog preselects for new archives - "zip", "tar", or "tar.gz".</summary>
    public string DefaultArchiveFormat { get; set; } = "zip";

    // Terminal settings
    public bool TerminalVisible { get; set; } = false;
    public int TerminalHeight { get; set; } = 250;
    public string DefaultShellType { get; set; } = "Cmd";
    public List<string> OpenTerminalTabs { get; set; } = new();
    public string? LastTerminalPath { get; set; }
}

/// <summary>
/// Loads and saves application settings.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CoderCommander", "settings.json");

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
            catch
            {
                _cached = new AppSettings();
            }

            Validate(_cached);
            return _cached;
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
    }

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
