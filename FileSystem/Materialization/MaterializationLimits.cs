namespace CoderCommander.FileSystem.Materialization;

/// <summary>Every bound a materialization runs under, in one file - the same "no single check on a
/// random line, every ceiling visible at a glance" convention as <c>Viewers.ViewerLimits</c>/
/// <c>Archives.OfficeLimits</c>/<c>FileSystem.Remote.RemoteLimits</c>.</summary>
public static class MaterializationLimits
{
    /// <summary>Default ceiling for an arbitrary non-archive file (matches
    /// <c>Viewers.ViewerLimits.MaterializeMaxBytes</c> - same shape of decision, same number).</summary>
    public const long DefaultMaxBytes = 256L * 1024 * 1024;

    /// <summary>Ceiling for an archive container - deliberately much larger than
    /// <see cref="DefaultMaxBytes"/>, since the whole point of materializing one is to run a real
    /// pack/unpack/browse against it, not to preview a few hundred KB of it.</summary>
    public const long ArchiveMaxBytes = 4L * 1024 * 1024 * 1024;

    /// <summary>Above this, browsing an archive whose container isn't on this machine asks for
    /// confirmation first (download size shown) rather than silently starting a multi-hundred-MB
    /// transfer on a single Enter keystroke. Below it, no prompt - a small archive isn't worth
    /// interrupting for.</summary>
    public const long ArchiveBrowseWarnBytes = 64L * 1024 * 1024;
}
