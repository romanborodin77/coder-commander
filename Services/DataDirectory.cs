namespace CoderCommander.Services;

/// <summary>
/// Where <c>settings.json</c>, <c>app.log</c> and <c>credentials.dat</c> live.
///
/// <para><b>Why this exists.</b> All three used to build their own path directly from
/// <see cref="Environment.SpecialFolder.ApplicationData"/>, which is exactly what makes them
/// unreachable by the usual "redirect via environment variable" trick: unlike
/// <see cref="Path.GetTempPath"/>, <c>SHGetKnownFolderPath</c> (what
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> calls on Windows) does not
/// consult <c>%APPDATA%</c> or any other environment variable - it asks the shell directly. Setting
/// <c>APPDATA</c> on a child process's environment block, the obvious way to sandbox a test run,
/// therefore does nothing: the child still resolves to the real user profile.</para>
///
/// <para><b>The fix is a variable this app itself chooses to honour</b> - the same shape as
/// <c>CODERCOMMANDER_UI_DEBUG</c> already uses for the layout-dump diagnostic channel.
/// <c>CODERCOMMANDER_DATA_DIR</c>, when set, is used verbatim as the folder that would otherwise be
/// <c>%APPDATA%\CoderCommander</c>. Unset (every normal launch), behaviour is unchanged.</para>
///
/// <para>Read once into a field rather than on every access - matches how the three callers already
/// treated their own path as fixed for the process's lifetime, and means a test fixture only has to
/// set the variable before the first thing touches settings/log/credentials, not keep it consistent
/// for the whole run.</para>
/// </summary>
public static class DataDirectory
{
    public const string OverrideEnvironmentVariable = "CODERCOMMANDER_DATA_DIR";

    public static readonly string Root = ResolveRoot();

    private static string ResolveRoot()
    {
        // Used as-is, with no "CoderCommander" subfolder appended on top - unlike the default
        // branch below, where that segment separates this app's files from every other app
        // sharing the same real %APPDATA%. A caller setting this variable is already handing over
        // a folder meant for nothing else (a per-test sandbox directory), so there is nothing to
        // separate it from.
        var overridden = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
            return overridden;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CoderCommander");
    }
}
