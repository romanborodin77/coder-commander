namespace CoderCommander.Utils;

/// <summary>
/// The one-generation "move to <c>.old</c>" rotation scheme this codebase uses for its log files.
///
/// Extracted here once a second log file (<c>Program.cs</c>'s crash log) needed the identical
/// logic that <c>LogService</c>'s app.log rotation already had - two copies of the same five lines
/// would have meant a bound fixed in one place silently staying unfixed in the other. Deliberately
/// has no try/catch of its own: how tolerant to be of a failed rotation differs by caller (the
/// crash log's own logging must never throw back into a crash handler; app.log's can be a plain
/// best-effort swallow), so each caller wraps this in whatever it already does.
/// </summary>
public static class LogRotation
{
    /// <summary>Moves <paramref name="path"/> to <c>{path}.old</c> if it is at least
    /// <paramref name="maxSizeBytes"/>, replacing any previous <c>.old</c>. Does nothing if the
    /// file doesn't exist or is still under the threshold.</summary>
    public static void RotateIfTooLarge(string path, long maxSizeBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < maxSizeBytes)
            return;

        var oldPath = path + ".old";
        if (File.Exists(oldPath))
            File.Delete(oldPath);
        File.Move(path, oldPath);
    }
}
