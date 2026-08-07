using CoderCommander.Services;

namespace CoderCommander.Archives;

/// <summary>
/// Shared retry policy for opening archive files that may be transiently locked by another
/// process (AV/indexer scan of a just-written file, a second panel reading the same archive) -
/// the same backoff schedule <c>ZipArchiveFileSystem</c> already uses internally, generalized so
/// new formats don't have to reimplement it. <c>ZipArchiveFileSystem</c> itself is left untouched
/// (its own copy keeps working exactly as before); this is for formats added from here on.
/// </summary>
public static class ArchiveFileRetry
{
    /// <summary>Backoff delays (in milliseconds) applied between retries when the archive file is locked.
    /// Widened from {150, 300, 600} (~1.05s total) after UiTests showed real, reproducible failures under
    /// this budget - Commit_AddingToExistingArchive_PreservesPriorEntries and its siblings would
    /// occasionally exhaust all 3 retries against a lock that cleared shortly after (almost certainly
    /// Windows Defender's real-time scan of a just-written temp archive; the same failure mode also hit
    /// PackOperation silently, since FileOperation.ExecuteAsync catches and reports it as State=Failed
    /// rather than throwing - see PackOperation callers for why checking State matters). ~4.7s total
    /// budget across 5 attempts comfortably covers scans that used to occasionally outlast ~1s.</summary>
    private static readonly int[] RetryDelaysMs = { 150, 300, 600, 1200, 2400 };

    /// <summary>
    /// Opens the archive at <paramref name="path"/> for shared reading. If the file is locked by another
    /// process, retries up to <see cref="RetryDelaysMs"/> times before throwing <see cref="IOException"/>.
    /// </summary>
    public static FileStream OpenReadWithRetry(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (IOException ex) when (ex.Message.Contains("being used by another process"))
        {
            return Retry(path, ex, () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite),
                "Cannot open archive");
        }
    }

    /// <summary>
    /// Opens the archive at <paramref name="path"/> for exclusive read-write access. If the file is locked
    /// by another process, retries up to <see cref="RetryDelaysMs"/> times before throwing <see cref="IOException"/>.
    /// </summary>
    public static FileStream OpenExclusiveWithRetry(string path)
    {
        try
        {
            return File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (ex.Message.Contains("being used by another process"))
        {
            return Retry(path, ex, () => File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),
                "Cannot open archive for update");
        }
    }

    /// <summary>
    /// Retries the given <paramref name="attempt"/> action with exponential backoff, throwing
    /// <see cref="IOException"/> with <paramref name="failureVerb"/> in the message if all retries fail.
    /// </summary>
    private static FileStream Retry(string path, Exception firstError, Func<FileStream> attempt, string failureVerb)
    {
        LogService.Warning($"Archive locked by another process, retrying: {path}");
        var lastError = firstError;
        foreach (var delayMs in RetryDelaysMs)
        {
            Thread.Sleep(delayMs);
            try
            {
                return attempt();
            }
            catch (Exception ex2)
            {
                lastError = ex2;
            }
        }

        throw new IOException($"{failureVerb} after {RetryDelaysMs.Length} retries: {path}", lastError);
    }
}
