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

        if (action == OverwriteAction.Rename && !string.IsNullOrEmpty(newName))
            return new ConflictResolution(true, VfsPath.ChangeName(destPath, newName), false);

        return new ConflictResolution(true, destPath, true);
    }
}
