namespace CoderCommander.Archives;

/// <summary>What an <see cref="IArchiveFormat"/> can actually do - callers (the Pack dialog's format
/// list, drag-into-archive rejection, the compression settings list) check these instead of
/// special-casing format IDs.</summary>
[Flags]
public enum ArchiveCapabilities
{
    None = 0,
    Read = 1 << 0,
    /// <summary>Entries can be opened by index/name without decoding everything before them.</summary>
    RandomAccessRead = 1 << 1,
    Create = 1 << 2,
    /// <summary>Add entries to an already-existing archive of this format (natively or via a
    /// rewrite-through-a-temp-file strategy - callers don't need to know which).</summary>
    AddEntries = 1 << 3,
    /// <summary>Remove entries (needed for move-into/move-out-of-archive and in-panel Delete).</summary>
    DeleteEntries = 1 << 4,
    /// <summary>Has an <see cref="IFileSystem"/> provider, i.e. can be browsed as a panel.</summary>
    Browse = 1 << 5
}
