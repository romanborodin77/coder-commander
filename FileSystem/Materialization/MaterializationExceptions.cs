namespace CoderCommander.FileSystem.Materialization;

/// <summary>Thrown by <see cref="MaterializedFile.AcquireAsync"/> when the origin's size (reported
/// up front, or measured while streaming for a provider that can't report it reliably) exceeds the
/// requested <see cref="MaterializeOptions.MaxBytes"/>.</summary>
public sealed class MaterializationTooLargeException : IOException
{
    public string OriginPath { get; }

    public MaterializationTooLargeException(string originPath, long observedBytes, long maxBytes)
        : base($"\"{originPath}\" is too large to materialize ({observedBytes:N0} bytes, limit {maxBytes:N0}).")
    {
        OriginPath = originPath;
    }
}

/// <summary>
/// Thrown by <see cref="MaterializedFile.WriteBackAsync"/> when the origin's size or last-write
/// timestamp no longer match what was recorded at <see cref="MaterializedFile.AcquireAsync"/> time -
/// something else changed the file on the server while it was being edited locally. Best-effort by
/// construction (some providers report timestamps at minute granularity), but it turns the common
/// two-clients-editing-the-same-file case into a clear, recoverable error instead of a silent
/// overwrite of whatever changed.
/// </summary>
public sealed class MaterializationConflictException : IOException
{
    public string OriginPath { get; }

    public MaterializationConflictException(string originPath)
        : base($"\"{originPath}\" changed on the server while it was being edited. Nothing was overwritten.")
    {
        OriginPath = originPath;
    }
}
