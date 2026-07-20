namespace CoderCommander.Archives;

/// <summary>Options passed to <see cref="IArchiveFormat.OpenWrite"/>.</summary>
public sealed class ArchiveWriteOptions
{
    /// <summary>Create a new, empty archive if <c>archivePath</c> doesn't exist yet.</summary>
    public bool CreateIfMissing { get; init; }

    /// <summary>
    /// Entry names the caller intends to write in this session, when known up front (e.g. a
    /// Pack operation over a fixed file list). Formats that can only add entries via a full
    /// rewrite (<see cref="ArchiveWriteMode.RewriteThrough"/>) use this to size/plan the rewrite;
    /// formats that support in-place updates can ignore it.
    /// </summary>
    public IReadOnlyList<string>? PlannedEntryNames { get; init; }
}
