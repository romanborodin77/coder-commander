using System.Diagnostics;
using CoderCommander.Utils;

namespace CoderCommander.Services;

/// <summary>
/// Launches a user-configured external viewer/editor executable in place of the built-in F3/F4
/// windows (<see cref="AppSettings.ExternalViewerEnabled"/>/<see cref="AppSettings.ExternalEditorEnabled"/>).
/// <para>
/// <b>Security:</b> only ever starts the exact executable path the user typed into Settings -
/// never <c>UseShellExecute</c> against a path that came from the file panel (that's the pattern
/// that turns a cleverly-named file into an unintended shell association launch). The file being
/// viewed/edited only ever appears as a quoted command-line argument, substituted into the user's
/// own argument template - it is never itself executed or shell-interpreted.
/// </para>
/// </summary>
public static class ExternalToolLauncher
{
    /// <summary>
    /// Attempts to launch <paramref name="exePath"/> with <paramref name="argsTemplate"/> (its
    /// literal <c>%1</c> token replaced with a quoted <paramref name="filePath"/>; a template with
    /// no <c>%1</c> gets the quoted path appended instead, so a blank/typo'd template still opens
    /// the file rather than silently opening the tool on nothing).
    /// </summary>
    /// <returns><see langword="true"/> if the process was started; <see langword="false"/> if
    /// <paramref name="exePath"/> is blank, doesn't exist, or launching it failed - every failure
    /// path is logged and swallowed, never thrown, so a stale or mistyped external-tool setting
    /// falls back to the built-in viewer/editor instead of blocking F3/F4 entirely.</returns>
    public static bool TryLaunch(string exePath, string argsTemplate, string filePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;

        try
        {
            if (!File.Exists(exePath))
            {
                LogService.Warning($"External tool not found, falling back to built-in: {exePath}");
                return false;
            }

            // Win32ArgumentQuoting, not the CSV/cmd doubling convention this used to use: that
            // convention mishandles a path with a trailing backslash (the final \" escapes the
            // closing quote instead of ending the argument, so the argument swallows the rest of
            // the command line) - a real argument-injection vector, since filePath can come from a
            // materialized archive/remote entry whose name isn't restricted from containing one.
            var quotedPath = Win32ArgumentQuoting.Quote(filePath);
            var args = string.IsNullOrEmpty(argsTemplate)
                ? quotedPath
                : argsTemplate.Contains("%1", StringComparison.Ordinal)
                    ? argsTemplate.Replace("%1", quotedPath, StringComparison.Ordinal)
                    : argsTemplate + " " + quotedPath;

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false
                }
            };
            process.Start();
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Failed to launch external tool '{exePath}' for '{filePath}'", ex);
            return false;
        }
    }
}
