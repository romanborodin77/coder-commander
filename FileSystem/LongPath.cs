namespace CoderCommander.FileSystem;

/// <summary>
/// Audit finding S7 (DEBUG.md §0.1): no <c>\\?\</c> long-path prefix existed anywhere in
/// <c>FileSystem/</c>, so any local path past Windows' historical MAX_PATH (260 characters) fails
/// with <see cref="PathTooLongException"/>/<see cref="DirectoryNotFoundException"/> on a machine
/// that hasn't opted into the modern <c>LongPathsEnabled</c> registry policy - which is most
/// machines; it defaults off. <c>\\?\</c> is the portable fix that works regardless of that
/// setting, predating it by well over a decade.
///
/// <para><b>Now also covers tree traversal (audit finding G057).</b> <see cref="LocalFileSystem.EnumerateAsync"/>/
/// <see cref="LocalFileSystem.EnumerateDeepAsync"/> resolve their root through <see cref="EnsureAccessible"/>
/// the same as every other method here, but a <c>\\?\</c>-prefixed root makes every yielded
/// <see cref="System.IO.FileSystemInfo.FullName"/> underneath it carry the prefix too - that would
/// leak into every <c>FileEntry.FullPath</c> shown in the UI and passed to every other method
/// downstream if left as-is, so each result is run back through <see cref="StripPrefix"/> before
/// becoming a <see cref="FileEntry"/>. This is what makes Flat View, the recursive folder-size
/// calculation, and content search work past the limit, not just Ctrl+G to a long path or a single
/// copy/move/pack/unpack destination.</para>
/// </summary>
public static class LongPath
{
    /// <summary>Below this length, a path is left completely untouched - <c>\\?\</c> is not a
    /// strict no-op for every Win32 API (e.g. it disables 8.3 short-name resolution and relative
    /// segment handling), so it is only worth the behavioral difference for paths that actually
    /// need it. Set comfortably under the real 260-character MAX_PATH to leave room for whatever
    /// filename component a caller appends to a path already at the edge.</summary>
    public const int SafeLength = 240;

    /// <summary>
    /// Returns <paramref name="path"/> unchanged if it's short enough or already <c>\\?\</c>-prefixed
    /// (idempotent - safe to call more than once on the same value); otherwise resolves it to a
    /// fully-qualified, backslash-normalized absolute path (<c>\\?\</c> tolerates neither forward
    /// slashes nor <c>.</c>/<c>..</c> segments) and prefixes it - <c>\\?\UNC\server\share\...</c>
    /// for a UNC path, <c>\\?\C:\...</c> for a drive path.
    /// </summary>
    public static string EnsureAccessible(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path.Replace('/', '\\'); // Win32 \\?\ paths require backslashes
        if (path.Length < SafeLength) return path;

        var full = Path.GetFullPath(path);
        return full.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + full[2..]
            : @"\\?\" + full;
    }

    /// <summary>
    /// Inverse of <see cref="EnsureAccessible"/> - removes a <c>\\?\</c> or <c>\\?\UNC\</c> prefix
    /// if present, restoring the ordinary form every caller outside this file expects to see and
    /// pass around. A no-op for a path that was never prefixed (the common case, since
    /// <see cref="EnsureAccessible"/> itself is a no-op below <see cref="SafeLength"/>) - safe to
    /// call unconditionally on every result of a <c>\\?\</c>-rooted enumeration.
    /// </summary>
    public static string StripPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }
}
