namespace CoderCommander.Operations;

/// <summary>
//// </summary>
public enum OperationState
{
    NotStarted,
    Running,
    Paused,
    Completed,
    Canceled,
    Failed
}

/// <summary>
//// </summary>
public enum OperationType
{
    Copy,
    Move,
    Delete,
    Wipe,
    CreateDirectory,
    CalculateStatistics,
    Pack,
    Unpack
}

/// <summary>
/// Progress report for file operations.
/// </summary>
public sealed class OperationProgress
{
    /// <summary>Overall percentage (0-100).</summary>
    public int Percent { get; init; }

    /// <summary>Currently processing file name.</summary>
    public string CurrentFile { get; init; } = "";

    /// <summary>Bytes processed so far.</summary>
    public long BytesProcessed { get; init; }

    /// <summary>Total bytes to process.</summary>
    public long BytesTotal { get; init; }

    /// <summary>Files processed so far.</summary>
    public int FilesProcessed { get; init; }

    /// <summary>Total files to process.</summary>
    public int FilesTotal { get; init; }

    /// <summary>Transfer speed in bytes/sec (0 if unknown).</summary>
    public long Speed { get; init; }

    /// <summary>Estimated remaining time (TimeSpan.Zero if unknown).</summary>
    public TimeSpan Remaining { get; init; }
}
