using System.ComponentModel;

namespace CoderCommander.Services;

/// <summary>
/// Whether this code is running inside a visual designer rather than the real application.
///
/// <para><b>Why this is needed.</b> The Windows Forms Designer instantiates the base class of the
/// form being designed - and every custom control dropped on it - inside the IDE's own process. For
/// this app that means <see cref="WinForms.ThemedForm"/>'s constructor runs there, which reaches
/// <see cref="ThemeService.Current"/> → <c>ThemeFontSet.FromSettings()</c> →
/// <see cref="SettingsService.Load"/>: real disk I/O against the developer's own
/// <c>%APPDATA%\CoderCommander</c>, and on a parse failure <see cref="LogService"/> would create and
/// append <c>app.log</c> from inside the IDE. Design time gets built-in defaults instead.</para>
///
/// <para><b>Why two checks.</b> <see cref="LicenseManager.UsageMode"/> is the documented signal but
/// only reports <c>Designtime</c> while a component is actually being constructed by the designer -
/// it reads <c>Runtime</c> everywhere else, including from a property getter the designer calls
/// later. The host-process check covers the rest and is stable for the whole process lifetime, so it
/// is computed once. Since .NET 5 the WinForms designer runs out-of-process in
/// <c>DesignToolsServer.exe</c> rather than in <c>devenv.exe</c> itself, so both are matched.</para>
/// </summary>
internal static class DesignTime
{
    private static readonly bool HostIsDesigner = DetectHost();

    /// <summary>True when running under a visual designer. Cheap enough to call from a hot path -
    /// the process check is cached and only the <see cref="LicenseManager"/> probe is live.</summary>
    public static bool IsActive => HostIsDesigner || LicenseManager.UsageMode == LicenseUsageMode.Designtime;

    private static bool DetectHost()
    {
        // Environment.ProcessPath rather than Process.GetCurrentProcess().ProcessName: it needs no
        // handle to the process and returns null instead of throwing when unavailable.
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        var name = Path.GetFileNameWithoutExtension(exe);
        return name.Equals("DesignToolsServer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("devenv", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Blend", StringComparison.OrdinalIgnoreCase);
    }
}
