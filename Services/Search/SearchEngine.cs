using System.Text.RegularExpressions;
using CoderCommander.FileSystem;

namespace CoderCommander.Services.Search;

/// <summary>What to look for.</summary>
/// <param name="NameMask">Wildcard masks, <c>;</c>-separated, or a single regular expression when
/// <paramref name="UseRegex"/> is true. Empty means every name.</param>
/// <param name="ContentText">Text that must occur inside the file - a literal substring, or a
/// regular expression when <paramref name="UseRegex"/> is true (matched per line - see
/// <see cref="ContentSearcher.FindRegexAsync"/>'s own doc comment for why regex content search
/// can't use the same whole-file substring window <see cref="ContentSearcher.FindAsync"/> does).
/// Empty means "names only".</param>
/// <param name="UseRegex">Interpret both <paramref name="NameMask"/> and
/// <paramref name="ContentText"/> as regular expressions instead of a wildcard mask / literal
/// substring. <paramref name="WholeWord"/> has no effect in this mode - a regex already expresses
/// word boundaries itself, via <c>\b</c>, when the user wants them.</param>
public sealed record SearchQuery(
    string NameMask,
    string ContentText = "",
    bool MatchCase = false,
    bool WholeWord = false,
    bool SearchSubdirectories = true,
    bool UseRegex = false);

/// <summary>One file the search matched.</summary>
/// <param name="LineNumber">Line of the content match, or 0 for a name-only match.</param>
public sealed record SearchHit(FileEntry Entry, int LineNumber, string Line);

/// <summary>
/// Walks any <see cref="IFileSystem"/> looking for files that match a query.
///
/// <para><b>Any filesystem</b> - local, inside an archive, on a connection - because it goes through
/// <see cref="IFileSystem"/> and nothing else. That is the payoff for the abstraction: searching a
/// WebDAV share needed no code here.</para>
///
/// <para><b>Nothing here touches the UI.</b> Hits are handed to a callback that fires on whatever
/// thread found them, and the caller marshals - the same contract <see cref="DriveCatalog"/> and
/// <see cref="ConnectionManager"/> state. Reporting each hit as it is found, rather than returning a
/// list at the end, is what lets results appear during a long search instead of after it.</para>
///
/// <para><b>Failures are skipped, never fatal.</b> A search over a whole drive will meet directories
/// it cannot list and files it cannot open - that is normal, not exceptional, and abandoning the
/// search at the first one would make the feature useless exactly where it is most wanted.</para>
/// </summary>
public sealed class SearchEngine
{
    /// <summary>Hits collected before the search stops itself. A grid with a million rows helps
    /// nobody, and a query that matches that many needs narrowing rather than displaying.</summary>
    public const int MaxResults = 10_000;

    /// <summary>Progress, for a status line. Directories walked, files examined, hits so far.</summary>
    public readonly record struct SearchProgress(int Directories, int FilesExamined, int Hits, string CurrentPath);

    /// <summary>Same bounded timeout as <see cref="FileMask"/>'s own wildcard/regex compile - the
    /// content pattern is user-typed text too, and a pathological regex must time out per line
    /// rather than hang the search.</summary>
    private static readonly TimeSpan ContentMatchTimeout = TimeSpan.FromSeconds(1);

    private readonly IFileSystem _fs;
    private readonly SearchQuery _query;
    private readonly FileMask _mask;
    private readonly Regex? _contentRegex;

    private int _directories;
    private int _filesExamined;
    private int _hits;

    public SearchEngine(IFileSystem fs, SearchQuery query)
    {
        _fs = fs;
        _query = query;
        _mask = new FileMask(query.NameMask, query.UseRegex, query.MatchCase);

        if (query.UseRegex && query.ContentText.Length > 0)
        {
            var opts = RegexOptions.Compiled | (query.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            try
            {
                _contentRegex = new Regex(query.ContentText, opts, ContentMatchTimeout);
            }
            catch (ArgumentException)
            {
                // Invalid regex - IsValid below reports it so the caller can show the compile
                // error instead of silently running a search that will find nothing.
                _contentRegex = null;
                ContentRegexInvalid = true;
            }
        }
    }

    /// <summary>False when <see cref="SearchQuery.NameMask"/> failed to compile as a regex - see
    /// <see cref="FileMask.IsValid"/>. Check before calling <see cref="RunAsync"/>.</summary>
    public bool IsNameMaskValid => _mask.IsValid;

    /// <summary>True when <see cref="SearchQuery.ContentText"/> failed to compile as a regex (only
    /// meaningful when <see cref="SearchQuery.UseRegex"/> is true and content text is non-empty).
    /// Check before calling <see cref="RunAsync"/>.</summary>
    public bool ContentRegexInvalid { get; }

    /// <summary>Whether the search stopped because it reached <see cref="MaxResults"/>.</summary>
    public bool WasTruncated { get; private set; }

    /// <summary>
    /// Live counters, for the summary shown when the search ends.
    ///
    /// <para>Read from here rather than from the last <see cref="SearchProgress"/>: progress is
    /// reported when a directory is <i>opened</i>, before its files have been scanned, so the last
    /// report is always short by the contents of the last directory. It read "examined 8" for a
    /// folder of nine files - a number small enough to look plausible and be wrong.</para>
    /// </summary>
    public int FilesExamined => Volatile.Read(ref _filesExamined);

    /// <inheritdoc cref="FilesExamined"/>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>
    /// Runs the search.
    /// </summary>
    /// <param name="rootPath">Where to start. Included itself only as a directory to walk.</param>
    /// <param name="onHit">Called for every match, <b>on a background thread</b>.</param>
    /// <param name="onProgress">Called periodically, <b>on a background thread</b>. May be null.</param>
    public async Task RunAsync(
        string rootPath,
        Action<SearchHit> onHit,
        Action<SearchProgress>? onProgress,
        CancellationToken ct = default)
    {
        // A remote filesystem gets one worker: its connections are a scarce, protocol-limited
        // resource, and issuing sixteen concurrent reads against a pool of four just makes twelve of
        // them queue while the server sees a burst it may treat as abuse. A local disk gets several,
        // because content search there is dominated by decoding rather than by the drive.
        var workers = _fs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths)
            ? Math.Clamp(Environment.ProcessorCount / 2, 2, 8)
            : 1;

        var queue = new Queue<string>();
        queue.Enqueue(rootPath);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (_hits >= MaxResults)
            {
                WasTruncated = true;
                return;
            }

            var directory = queue.Dequeue();
            IReadOnlyList<FileEntry> entries;
            try
            {
                entries = await _fs.EnumerateAsync(directory, includeHidden: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Debug($"Search: cannot list {directory}: {ex.GetType().Name}");
                continue;
            }

            Interlocked.Increment(ref _directories);
            onProgress?.Invoke(new SearchProgress(_directories, _filesExamined, _hits, directory));

            var candidates = new List<FileEntry>();
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    // Skip junctions/symlinks to prevent infinite loops on circular reparse points.
                    if (_query.SearchSubdirectories && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                        queue.Enqueue(entry.FullPath);
                    continue;
                }

                if (_mask.Matches(entry.Name)) candidates.Add(entry);
            }

            if (candidates.Count == 0) continue;

            if (_query.ContentText.Length == 0)
            {
                // Name-only search: nothing to open, so there is nothing to parallelise.
                foreach (var entry in candidates)
                {
                    Interlocked.Increment(ref _filesExamined);
                    // Check BEFORE increment to avoid inflating _hits past MaxResults.
                    if (Volatile.Read(ref _hits) >= MaxResults)
                    {
                        WasTruncated = true;
                        return;
                    }
                    Interlocked.Increment(ref _hits);
                    onHit(new SearchHit(entry, 0, ""));
                }
                continue;
            }

            await ScanContentsAsync(candidates, workers, onHit, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens and searches each candidate, up to <paramref name="workers"/> at a time.
    ///
    /// <para>Bounded rather than unbounded: <c>Task.WhenAll</c> over a directory of ten thousand
    /// files would open ten thousand handles at once, which on a local disk thrashes and on a
    /// connection exhausts the pool instantly.</para>
    /// </summary>
    private async Task ScanContentsAsync(
        List<FileEntry> candidates, int workers, Action<SearchHit> onHit, CancellationToken ct)
    {
        using var slots = new SemaphoreSlim(workers, workers);
        var running = new List<Task>(candidates.Count);

        try
        {
            foreach (var entry in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (_hits >= MaxResults)
                {
                    WasTruncated = true;
                    break;
                }

                await slots.WaitAsync(ct).ConfigureAwait(false);
                running.Add(ScanOneAsync(entry, slots, onHit, ct));
            }
        }
        finally
        {
            // Ensure all in-flight scans are awaited even if the foreach threw (cancellation,
            // exception) — otherwise they become unobserved tasks that may fault after the
            // semaphore is disposed.
            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    private async Task ScanOneAsync(FileEntry entry, SemaphoreSlim slots, Action<SearchHit> onHit, CancellationToken ct)
    {
        try
        {
            Interlocked.Increment(ref _filesExamined);

            await using var stream = await _fs.OpenReadAsync(entry.FullPath, ct).ConfigureAwait(false);
            var hit = _contentRegex != null
                ? await ContentSearcher.FindRegexAsync(stream, _contentRegex, ct).ConfigureAwait(false)
                : await ContentSearcher
                    .FindAsync(stream, _query.ContentText, _query.MatchCase, _query.WholeWord, ct)
                    .ConfigureAwait(false);

            if (!hit.Found) return;

            if (Interlocked.Increment(ref _hits) > MaxResults)
            {
                WasTruncated = true;
                return;
            }
            onHit(new SearchHit(entry, hit.LineNumber, hit.Line));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A locked file, a file the account cannot read, a connection that dropped for this one
            // request. All ordinary during a wide search.
            LogService.Debug($"Search: cannot read {entry.FullPath}: {ex.GetType().Name}");
        }
        finally
        {
            try { slots.Release(); } catch (ObjectDisposedException) { /* search cancelled */ }
        }
    }
}
