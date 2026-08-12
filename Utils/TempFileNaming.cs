namespace CoderCommander.Utils;

/// <summary>
/// Single naming convention for a temp file, replacing the mix that grew organically across the
/// codebase: some sites called <see cref="Path.GetTempFileName"/> (shared <c>%TEMP%</c>, a
/// documented ~65,535-unique-name-per-directory ceiling, and an extra round-trip to disk just to
/// create the placeholder file), others built <c>path + ".tmp-"/".stage-"/".rewrite-"/".update-" +
/// Guid</c> by hand next to their target. Not a defect either way - every site already deletes its
/// temp file on every exit path - but one convention is easier to recognize on disk and to audit.
/// </summary>
public static class TempFileNaming
{
    /// <summary>
    /// Temp path next to <paramref name="targetPath"/>, tagged with <paramref name="tag"/> (e.g.
    /// <c>"stage"</c>, <c>"rewrite"</c>) so an orphaned file left behind by a crash says what it
    /// was for at a glance. Same directory as the target keeps a later <see cref="File.Move(string, string, bool)"/>
    /// on one volume - an atomic rename instead of a cross-volume copy+delete. Nothing is created
    /// on disk; the caller creates the file when it actually writes.
    /// </summary>
    public static string NextTo(string targetPath, string tag = "tmp") =>
        $"{targetPath}.{tag}-{Guid.NewGuid():N}";

    /// <summary>
    /// Temp path in the system temp directory, for the rare site with no target path to sit next
    /// to. Still Guid-named rather than going through <see cref="Path.GetTempFileName"/>, so it
    /// isn't subject to that API's per-directory unique-name ceiling. Nothing is created on disk;
    /// the caller creates the file when it actually writes.
    /// </summary>
    public static string InSystemTemp(string tag = "tmp") =>
        Path.Combine(Path.GetTempPath(), $"ccmd-{tag}-{Guid.NewGuid():N}.tmp");
}
