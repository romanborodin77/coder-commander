namespace CoderCommander.FileSystem.Materialization;

/// <summary>Parameters for one <see cref="MaterializedFile.AcquireAsync"/> call.</summary>
public sealed record MaterializeOptions
{
    /// <summary>Hard ceiling in bytes. Exceeded (by the origin's reported size, or - since some
    /// providers report an unreliable or absent size - by the actual byte count while copying)
    /// throws before the caller ever sees a usable <see cref="MaterializedFile"/>.</summary>
    public long MaxBytes { get; init; } = MaterializationLimits.DefaultMaxBytes;

    /// <summary>True when the origin is allowed not to exist yet - e.g. <c>PackOperation</c>
    /// creating a brand-new archive on a remote destination. When set and the origin is missing,
    /// <see cref="MaterializedFile.LocalPath"/> names a fresh, empty, not-yet-existing file inside
    /// its own temp folder rather than throwing <see cref="FileNotFoundException"/>.</summary>
    public bool AllowMissing { get; init; }

    public static readonly MaterializeOptions ForArchiveWrite =
        new() { MaxBytes = MaterializationLimits.ArchiveMaxBytes, AllowMissing = true };

    public static readonly MaterializeOptions ForArchiveRead =
        new() { MaxBytes = MaterializationLimits.ArchiveMaxBytes };
}
