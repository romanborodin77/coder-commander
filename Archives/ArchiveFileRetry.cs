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
    private static readonly int[] RetryDelaysMs = { 150, 300, 600 };

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
