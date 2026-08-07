using CoderCommander.FileSystem;

namespace CoderCommander.Operations;

/// <summary>Result of resolving a destination-already-exists conflict.</summary>
/// <param name="Proceed">False means skip this item entirely (do nothing).</param>
/// <param name="TargetPath">The path to actually write to - same as the requested destination
/// unless the conflict was resolved by renaming.</param>
/// <param name="Overwrite">Whether the caller should pass "overwrite" through to a filesystem
/// call that needs it explicitly (e.g. <see cref="IFileSystem.MoveAsync"/>); irrelevant for calls
/// that just always write to <see cref="TargetPath"/> regardless (e.g. CopyFromStreamAsync).</param>
internal readonly record struct ConflictResolution(bool Proceed, string TargetPath, bool Overwrite);

/// <summary>
/// Shared "does the destination already exist, and if so what should happen" check, previously
/// reimplemented near-identically in <c>CopyOperation</c>, <c>MoveOperation</c>, and
/// <c>UnpackOperation</c>. <c>PackOperation</c> resolves clashes against an in-memory archive
/// directory snapshot instead of a filesystem, so it keeps its own <c>ResolveClash</c>.
/// </summary>
internal static class ConflictResolver
{
    public static async Task<ConflictResolution> ResolveAsync(
        IFileSystem destFs,
        string sourcePath,
        string destPath,
        FileEntry sourceInfo,
        TransferOptions options,
        CancellationToken ct)
    {
        if (!await destFs.ExistsAsync(destPath, ct).ConfigureAwait(false))
            return new ConflictResolution(true, destPath, false);

        var action = OverwriteAction.Skip;
        string? newName = null;

        if (options.OverwriteResolver != null)
        {
            var destInfo = await destFs.GetFileInfoAsync(destPath, ct).ConfigureAwait(false);
            action = options.OverwriteResolver(sourcePath, destPath, sourceInfo, destInfo, out newName);
        }
        else if (options.Overwrite)
        {
            action = OverwriteAction.Overwrite;
        }

        if (action is OverwriteAction.Skip or OverwriteAction.SkipAll)
            return new ConflictResolution(false, destPath, false);

        if (action == OverwriteAction.Rename)
        {
            var target = await ResolveRenameTargetAsync(destFs, destPath, newName, ct).ConfigureAwait(false);
            return new ConflictResolution(true, target, false);
        }

        return new ConflictResolution(true, destPath, true);
    }

    /// <summary>
    /// Verifies (and if needed, generates) a rename target that's actually free in
    /// <paramref name="destFs"/>. The one shipped <c>OverwriteResolveHandler</c>
    /// (<c>MainForm.GenerateUniqueName</c>) computes its suggested name by checking the real
    /// Windows disk via <see cref="File.Exists(string)"/>/<see cref="Directory.Exists(string)"/> -
    /// meaningless for an archive-backed <paramref name="destFs"/>, whose VFS paths use <c>|</c>
    /// as the archive/inner-path separator, which <see cref="Path.GetDirectoryName(string)"/>
    /// doesn't understand. Trusting that suggestion blindly (the old behavior) meant "Rename" on
    /// an archive destination almost always produced a name that already existed inside the
    /// archive, silently deleting and replacing an unrelated entry instead of keeping both. This
    /// also covers a caller supplying no suggestion at all (previously fell through to Overwrite).
    /// </summary>
    private static async Task<string> ResolveRenameTargetAsync(
        IFileSystem destFs, string destPath, string? suggestedName, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(suggestedName))
        {
            var suggestedPath = VfsPath.ChangeName(destPath, suggestedName);
            if (!await destFs.ExistsAsync(suggestedPath, ct).ConfigureAwait(false))
                return suggestedPath;
        }

        var originalName = VfsPath.GetName(destPath);
        // Not FileEntry.GetExtension: it lowercases on purpose (it's meant for extension
        // comparisons), which would silently rewrite e.g. "Report.PDF" to "Report (1).pdf" here -
        // a different filename on any case-sensitive destination (TAR, a case-sensitive local
        // FS). Same dotfile rule (a leading dot with no other dot, e.g. ".gitignore", isn't an
        // extension separator) via the same lastDot > 0 check, just without the case-folding.
        var lastDot = originalName.LastIndexOf('.');
        var baseName = lastDot > 0 ? originalName[..lastDot] : originalName;
        var ext = lastDot > 0 ? originalName[lastDot..] : "";

        for (var counter = 1; ; counter++)
        {
            var candidatePath = VfsPath.ChangeName(destPath, $"{baseName} ({counter}){ext}");
            if (!await destFs.ExistsAsync(candidatePath, ct).ConfigureAwait(false))
                return candidatePath;
        }
    }
}
