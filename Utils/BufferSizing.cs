namespace CoderCommander.Utils;

/// <summary>
/// Picks a copy buffer size that scales with the amount of data being moved - shared so
/// <c>LocalFileSystem</c>'s plain file copies and <c>ZipArchiveWriter</c>'s entry writes don't
/// each pick a different buffer for the same size of file.
/// </summary>
public static class BufferSizing
{
    /// <summary>Returns a buffer size in bytes: 80KB by default, scaling up to 4MB for large transfers.</summary>
    public static int ForSize(long size) => size switch
    {
        > 104_857_600 => 4_194_304, // >100MB -> 4MB
        > 10_485_760 => 1_048_576,  // >10MB -> 1MB
        _ => 81920                  // default -> 80KB
    };
}
