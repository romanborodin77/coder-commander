namespace CoderCommander.FileSystem;

/// <summary>
/// Audit finding S7 (AUDIT-FINDINGS.md): no <c>\\?\</c> long-path prefix existed anywhere in
/// <c>FileSystem/</c>, so any local path past Windows' historical MAX_PATH (260 characters) fails
/// with <see cref="PathTooLongException"/>/<see cref="DirectoryNotFoundException"/> on a machine
/// that hasn't opted into the modern <c>LongPathsEnabled</c> registry policy - which is most
/// machines; it defaults off. <c>\\?\</c> is the portable fix that works regardless of that
/// setting, predating it by well over a decade.
///
/// <para><b>Honestly scoped, not a complete fix.</b> Applied at <see cref="LocalFileSystem"/>'s
/// single-path, non-enumerating methods (exists/create/copy/move/delete/read a specific path) -
/// where <c>\\?\</c>-prefixing a path and using it for one Win32 call has no other consequence.
/// Deliberately NOT applied yet to <c>EnumerateAsync</c>/<c>EnumerateDeepAsync</c>: those build
/// each returned <c>FileEntry.FullPath</c> from <see cref="System.IO.DirectoryInfo.FullName"/>
/// starting at the enumerated root, so a prefixed root would leak <c>\\?\</c> into every entry's
/// path shown in the UI and passed to every other method downstream - fixing that correctly means
/// stripping the prefix back off each result, which needs its own dedicated pass and tests, not a
/// same-session bolt-on. A user hitting the limit via Flat View or a deep recursive walk is not yet
/// covered; one hitting it via Ctrl+G to a long path, or a copy/move/pack/unpack whose destination
/// ends up past the limit, now is.</para>
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
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.Length < SafeLength) return path;

        var full = Path.GetFullPath(path);
        return full.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\UNC\" + full[2..]
            : @"\\?\" + full;
    }
}
