namespace CoderCommander.Models;

/// <summary>How far <see cref="DriveEntry"/>'s slow fields have got.</summary>
public enum DriveProbeState
{
    /// <summary>Letter and type are known; label and free space are still being read.</summary>
    Pending,
    /// <summary>Everything was read successfully.</summary>
    Ready,
    /// <summary>The drive exists but didn't answer in time, or answered with an error - an empty
    /// optical drive, a disconnected network share, a card reader with no card. Deliberately kept
    /// in the list rather than dropped: a button that disappears is worse than one that is present
    /// but marked unavailable.</summary>
    Unavailable,
}

/// <summary>
/// One drive as the UI needs it. Split into cheap fields (known immediately) and slow fields
/// (filled in by a background probe) because reading them costs wildly different amounts:
/// <c>GetLogicalDrives</c>/<c>GetDriveType</c> are register reads, whereas
/// <see cref="System.IO.DriveInfo.IsReady"/>, <c>VolumeLabel</c> and <c>TotalSize</c> go to the
/// device and can block for seconds on an optical drive spinning up or a dead network share.
/// </summary>
/// <param name="RootPath">Root with trailing separator, e.g. <c>"C:\"</c>.</param>
/// <param name="Letter">Display form without the separator, e.g. <c>"C:"</c>.</param>
public sealed record DriveEntry(
    string RootPath,
    string Letter,
    DriveType DriveType,
    string Label,
    long FreeBytes,
    long TotalBytes,
    DriveProbeState ProbeState)
{
    /// <summary>The cheap-fields-only form, used the moment a drive is discovered so its button can
    /// be drawn before anything is known about the medium in it.</summary>
    public static DriveEntry Pending(string rootPath, DriveType type) => new(
        rootPath,
        rootPath.TrimEnd(Path.DirectorySeparatorChar),
        type,
        Label: string.Empty,
        FreeBytes: 0,
        TotalBytes: 0,
        DriveProbeState.Pending);

    /// <summary>Label when known, letter otherwise - what a button/tooltip should show.</summary>
    public string DisplayName => Label.Length > 0 ? $"{Letter} ({Label})" : Letter;

    /// <summary><c>true</c> once the medium answered, i.e. navigating to it should work.</summary>
    public bool IsAccessible => ProbeState == DriveProbeState.Ready;
}
