namespace CoderCommander.Archives;

/// <summary>
/// Path-traversal ("zip slip") guards shared by every format's extraction path. Originally
/// duplicated between a relative-segment check and an absolute resolved-path check in two
/// separate places - this combines both so every reader benefits from the stronger of the two
/// regardless of how its entry names are shaped.
/// </summary>
public static class ArchiveSafety
{
    /// <summary>True if a "/"-separated entry-relative path contains a rooted segment, a ".."
    /// component that would escape its own subtree, or a ':' in any segment. The latter blocks
    /// NTFS Alternate Data Stream syntax (e.g. "readme.txt:payload.exe"): Path.IsPathRooted and
    /// Path.GetFullPath both treat that as a plain, non-rooted filename (":" is only special to
    /// them in the drive-letter position), so it slips past every other guard here and writes a
    /// hidden stream onto the extracted file - invisible in Explorer/dir but directly executable.
    /// No current archive reader legitimately needs ':' in an entry name, so this is a flat reject
    /// rather than a narrower Windows-only check.
    /// <para>Walks the path as a span, one '/'-delimited segment at a time, instead of
    /// <c>string.Split</c> + LINQ <c>Any</c> - this runs once per entry during extraction, and the
    /// old version allocated a fresh <c>string[]</c> (plus a closure for the lambda) for every
    /// single one, entirely for a two-branch character check.</para></summary>
    public static bool EscapesTarget(string relativeEntryPath)
    {
        if (Path.IsPathRooted(relativeEntryPath))
            return true;

        ReadOnlySpan<char> remaining = relativeEntryPath;
        while (true)
        {
            var slash = remaining.IndexOf('/');
            var part = slash < 0 ? remaining : remaining[..slash];
            if (part.SequenceEqual("..") || part.Contains(':'))
                return true;
            if (slash < 0)
                return false;
            remaining = remaining[(slash + 1)..];
        }
    }

    /// <summary>Normalizes <paramref name="targetRoot"/> once into the form
    /// <see cref="EscapesRoot"/> compares resolved entry paths against - hoist this out of a
    /// per-entry extraction loop and pass the result in, rather than letting <see cref="EscapesRoot"/>
    /// recompute the same <see cref="Path.GetFullPath(string)"/> call for the same root on every
    /// single entry.</summary>
    public static string NormalizeRoot(string targetRoot) =>
        Path.GetFullPath(targetRoot + Path.DirectorySeparatorChar);

    /// <summary>True if resolving <paramref name="entryName"/> against <paramref name="normalizedRoot"/>
    /// (as produced by <see cref="NormalizeRoot"/>) would land outside it. Real path resolution, as
    /// a second layer behind <see cref="EscapesTarget"/>'s cheaper segment check - every current
    /// reader (Zip/Tar/SharpCompress) already normalizes entry names to '/' before this runs, so
    /// today the two checks agree on every reachable input; this exists so a future reader that
    /// doesn't normalize isn't silently unprotected. An entry name so malformed that path
    /// resolution itself throws (illegal characters, etc.) is treated as escaping too - fail closed
    /// rather than let a raw IO exception abort the whole extraction.</summary>
    public static bool EscapesRoot(string normalizedRoot, string entryName)
    {
        try
        {
            var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, entryName));
            return !resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return true;
        }
    }
}
