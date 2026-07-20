namespace CoderCommander.Archives;

/// <summary>
/// Path-traversal ("zip slip") guards shared by every format's extraction path. Originally
/// duplicated between a relative-segment check and an absolute resolved-path check in two
/// separate places - this combines both so every reader benefits from the stronger of the two
/// regardless of how its entry names are shaped.
/// </summary>
public static class ArchiveSafety
{
    /// <summary>True if a "/"-separated entry-relative path contains a rooted segment or a ".."
    /// component that would escape its own subtree.</summary>
    public static bool EscapesTarget(string relativeEntryPath) =>
        Path.IsPathRooted(relativeEntryPath) ||
        relativeEntryPath.Split('/').Any(part => part == "..");

    /// <summary>True if resolving <paramref name="entryName"/> against <paramref name="targetRoot"/>
    /// would land outside <paramref name="targetRoot"/>. Real path resolution, as a second layer
    /// behind <see cref="EscapesTarget"/>'s cheaper segment check - every current reader (Zip/Tar/
    /// SharpCompress) already normalizes entry names to '/' before this runs, so today the two
    /// checks agree on every reachable input; this exists so a future reader that doesn't normalize
    /// isn't silently unprotected. An entry name so malformed that path resolution itself throws
    /// (illegal characters, etc.) is treated as escaping too - fail closed rather than let a raw IO
    /// exception abort the whole extraction.</summary>
    public static bool EscapesRoot(string targetRoot, string entryName)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(targetRoot + Path.DirectorySeparatorChar);
            var resolved = Path.GetFullPath(Path.Combine(targetRoot, entryName));
            return !resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return true;
        }
    }
}
