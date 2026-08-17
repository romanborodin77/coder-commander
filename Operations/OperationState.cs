namespace CoderCommander.Operations;

/// <summary>Lifecycle state of a file operation.</summary>
public enum OperationState
{
    /// <summary>Operation has been created but not yet started.</summary>
    NotStarted,

    /// <summary>Operation is actively executing.</summary>
    Running,

    /// <summary>Operation is paused (reserved for future use).</summary>
    Paused,

    /// <summary>Operation finished successfully.</summary>
    Completed,

    /// <summary>Operation was cancelled before finishing.</summary>
    Canceled,

    /// <summary>Operation terminated due to an error.</summary>
    Failed
}

/// <summary>Kind of file operation being performed.</summary>
public enum OperationType
{
    /// <summary>Copying files from one location to another.</summary>
    Copy,

    /// <summary>Moving files between locations.</summary>
    Move,

    /// <summary>Deleting files or directories.</summary>
    Delete,

    /// <summary>Securely wiping files before deletion.</summary>
    Wipe,

    /// <summary>Creating a new directory.</summary>
    CreateDirectory,

    /// <summary>Calculating folder sizes or statistics.</summary>
    CalculateStatistics,

    /// <summary>Packing files into an archive.</summary>
    Pack,

    /// <summary>Unpacking files from an archive.</summary>
    Unpack,

    /// <summary>Splitting a file into numbered parts.</summary>
    Split,

    /// <summary>Combining numbered parts back into a single file.</summary>
    Combine
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
