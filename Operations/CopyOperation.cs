using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;

namespace CoderCommander.Operations;

/// <summary>
/// Overwrite resolution policy for file operations.
/// </summary>
public enum OverwriteAction
{
    /// <summary>Prompt the user for a decision.</summary>
    Ask,

    /// <summary>Overwrite the existing file.</summary>
    Overwrite,

    /// <summary>Skip the conflicting file.</summary>
    Skip,

    /// <summary>Overwrite only if the source is newer.</summary>
    OverwriteOlder,

    /// <summary>Overwrite all conflicts without further prompts.</summary>
    OverwriteAll,

    /// <summary>Skip all conflicts without further prompts.</summary>
    SkipAll,

    /// <summary>Rename the incoming file to avoid collision.</summary>
    Rename
}

/// <summary>
/// Callback for resolving file-exists conflicts during copy/move.
/// Returns the chosen action and optionally a new name.
/// </summary>
public delegate OverwriteAction OverwriteResolveHandler(string source, string destination, FileEntry sourceInfo, FileEntry? destInfo, out string? newName);

/// <summary>
/// Options for copy/move operations.
/// </summary>
public sealed class TransferOptions
{
    /// <summary>When true, overwrite existing files without prompting.</summary>
    public bool Overwrite { get; set; }

    /// <summary>When true, copy file-system attributes (ReadOnly, Hidden, etc.).</summary>
    public bool CopyAttributes { get; set; } = true;

    /// <summary>When true, preserve creation and last-write timestamps.</summary>
    public bool CopyTimestamps { get; set; } = true;

    /// <summary>Optional callback for resolving overwrite conflicts interactively.</summary>
    public OverwriteResolveHandler? OverwriteResolver { get; set; }

    /// <summary>Compression to use for archive operations; null lets <see cref="PackOperation"/>
    /// fall back to <see cref="ArchiveCompressionSpec.Balanced"/>.</summary>
    public ArchiveCompressionSpec? Compression { get; set; }

    /// <summary>When true (the default), files whose extension is in <see cref="AlreadyCompressedExtensions"/>
    /// are stored without compression regardless of <see cref="Compression"/>.</summary>
    public bool SkipCompressionForCompressedFiles { get; set; } = true;

    /// <summary>Extensions <see cref="PackOperation"/> treats as already-compressed; null uses its
    /// own built-in default list.</summary>
    public IReadOnlyList<string>? AlreadyCompressedExtensions { get; set; }
}

/// <summary>
/// Copy operation: copies files and directories from source to destination.
/// </summary>
public sealed class CopyOperation : FileOperation
{
    public override OperationType Type => OperationType.Copy;
    public override string Title => "Copy";

    /// <summary>Wired into the per-file (non-batch) copy loop below - see <see cref="ExecuteCoreAsync"/>.
    /// Not honored on the batch-archive-source path (<see cref="CopyFilesBatchAsync"/>), which reads
    /// through a format-specific <see cref="IBatchReadableFileSystem.CopyManyToAsync"/> single pass
    /// that has no natural per-file interruption point without threading pause/skip into every
    /// implementer of that interface - deferred.</summary>
    public override bool SupportsPauseAndSkip => true;

    private readonly IFileSystem _sourceFs;
    private readonly IFileSystem _destFs;
    private readonly IReadOnlyList<FileEntry> _files;
    private readonly string _sourceBasePath;
    private readonly string _destPath;
    private readonly TransferOptions _options;

    private int _filesProcessed;
    private int _filesTotal;
    private long _bytesProcessed;
    private long _bytesTotal;
    private readonly HashSet<string> _writtenPaths;

    /// <summary>Source paths that were actually written to the destination - i.e. NOT skipped via
    /// a conflict resolution. <see cref="OperationState.Completed"/> alone doesn't mean every
    /// planned file was copied: a "Skip" conflict resolution still lets the operation finish
    /// normally. Callers that need to know exactly what landed on disk (e.g. <see cref="MoveOperation"/>
    /// deciding what's now safe to delete from the source) must consult this, not just
    /// <see cref="FileOperation.State"/> or the destination's existence.</summary>
    public IReadOnlyCollection<string> WrittenPaths => _writtenPaths;

    /// <summary>Creates a copy operation from <paramref name="sourceFs"/> to <paramref name="destFs"/>.</summary>
    public CopyOperation(
        IFileSystem sourceFs,
        IFileSystem destFs,
        IReadOnlyList<FileEntry> files,
        string sourceBasePath,
        string destPath,
        TransferOptions? options = null)
    {
        _sourceFs = sourceFs;
        _destFs = destFs;
        _files = files;
        _sourceBasePath = sourceBasePath;
        _destPath = destPath;
        _options = options ?? new TransferOptions();

        // Case sensitivity follows the source: Windows local FS is case-insensitive, but ZIP
        // archives and remote providers distinguish Foo.txt from foo.txt — using OrdinalIgnoreCase
        // for those silently drops the second file from _writtenPaths, causing MoveOperation to
        // not delete it from the source (data remains in both places after a move).
        var sourceCmp = sourceFs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        _writtenPaths = new HashSet<string>(sourceCmp);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var enumerationFailures = new List<string>();
        var plan = await FlattenAsync(_sourceFs, _destFs, _files, _sourceBasePath, _destPath, ct, enumerationFailures).ConfigureAwait(false);

        _filesTotal = plan.Count(p => !p.Entry.IsDirectory);
        _bytesTotal = plan.Where(p => !p.Entry.IsDirectory).Sum(p => p.Entry.Size);

        // Pre-flight: check that the destination has enough free space before writing anything.
        // Without this, a copy to a near-full disk fails mid-way, leaving a partial file and
        // wasted I/O — UnpackOperation already does the same check.
        try
        {
            var (freeBytes, _) = await _destFs.GetDriveSpaceAsync(_destPath, ct).ConfigureAwait(false);
            if (freeBytes > 0 && _bytesTotal > freeBytes)
                throw new IOException($"Not enough free space: need {FormatUtils.FormatSize(_bytesTotal)}, {FormatUtils.FormatSize(freeBytes)} available.");
        }
        catch (IOException) { throw; }
        catch (Exception) { /* GetDriveSpaceAsync not supported — skip the check */ }

        await _destFs.CreateDirectoryAsync(_destPath, ct).ConfigureAwait(false);

        // A single locked/inaccessible file used to abort the ENTIRE copy - nothing caught an
        // exception from CopyFileWithProgress, so it propagated straight out of ExecuteCoreAsync
        // and every remaining planned file (potentially thousands) was never even attempted.
        // Unlike Pack/Unpack (which already tolerate this), Copy had zero partial-failure
        // tolerance. Collected here and reported together with any enumeration failures below.
        var copyFailures = new List<string>();

        // Directories first (plan is already ordered top-down) - both the batch and per-file
        // paths below assume every directory in `plan` already exists on the destination.
        var fileEntries = new List<(FileEntry Entry, string DestPath)>(plan.Count);
        foreach (var (entry, destFullPath) in plan)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
            {
                await _destFs.CreateDirectoryAsync(destFullPath, ct).ConfigureAwait(false);
                if (_options.CopyAttributes && entry.Attributes != default)
                    await TryApplyAttributes(destFullPath, entry.Attributes, ct).ConfigureAwait(false);
                if (_options.CopyTimestamps)
                    ApplyTimestamps(_destFs, destFullPath, entry);
                continue;
            }

            fileEntries.Add((entry, destFullPath));
        }

        // A source without random-access entry opening (TAR/TAR.GZ/7z/RAR) pays a full archive
        // scan for every independent OpenReadAsync call - O(files x archive size) instead of one
        // O(archive size) pass. IBatchReadableFileSystem.CopyManyToAsync is the single-pass
        // alternative; only worth the extra machinery for more than one file.
        if (_sourceFs is IBatchReadableFileSystem batchSrc && fileEntries.Count >= 2)
        {
            await CopyFilesBatchAsync(batchSrc, fileEntries, copyFailures, ct).ConfigureAwait(false);
        }
        else
        {
            foreach (var (entry, destFullPath) in fileEntries)
            {
                ct.ThrowIfCancellationRequested();
                await WaitIfPausedAsync(ct).ConfigureAwait(false);

                var fileCts = BeginFile(ct);
                try
                {
                    await CopyFileWithProgress(entry, destFullPath, fileCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // fileCts fired but the operation's own ct didn't - a Skip request for this
                    // file specifically, not a cancellation of the whole copy.
                    LogService.Info($"Copy: skipped {entry.FullPath} by user request");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogService.Warning($"Copy: failed for {entry.FullPath}: {ex.Message}");
                    copyFailures.Add(entry.FullPath);
                }
                finally
                {
                    EndFile(fileCts);
                }

                _filesProcessed++;
                ReportProgress(entry.Name);
            }
        }

        // Copy everything that could be read (above) before reporting the failure, rather than
        // aborting the whole operation the instant one file/subtree can't be copied/enumerated -
        // but still fail loudly at the end instead of silently completing with less content than
        // the user selected (the WipeOperation/PackOperation.RemoveSourcesAsync precedent: collect
        // failures, still do the achievable work, then throw a clear summary).
        var allFailures = enumerationFailures.Concat(copyFailures).ToList();
        if (allFailures.Count > 0)
            throw new IOException(
                $"Copied successfully, but {allFailures.Count} item(s) could not be copied: " +
                $"{string.Join(", ", allFailures.Take(5))}" +
                (allFailures.Count > 5 ? $" and {allFailures.Count - 5} more" : ""));
    }

    /// <summary>
    /// Expands the selection into every entry that has to be created, keeping folders ahead of
    /// their content so that the destination tree is always built top-down. A root whose subtree
    /// can't be enumerated is reported via <paramref name="enumerationFailures"/> (when supplied)
    /// and excluded from the plan entirely, rather than silently producing an empty destination
    /// folder that looks like a successful (if content-less) copy.
    /// </summary>
    internal static async Task<List<(FileEntry Entry, string Destination)>> FlattenAsync(
        IFileSystem sourceFs,
        IFileSystem? destFs,
        IReadOnlyList<FileEntry> roots,
        string sourceBasePath,
        string destPath,
        CancellationToken ct,
        List<string>? enumerationFailures = null)
    {
        var plan = new List<(FileEntry, string)>();
        // Guards against double-copying: Flat View lets the user select both a folder and a
        // file already inside it (e.g. Ctrl-click a folder, then Ctrl-click a file nested under
        // it). Without this, that file's destination path is planned once as part of the
        // folder's own recursive walk and again as its own selection root - the second write
        // then collides with the one the first write just made, tripping a spurious "already
        // exists" conflict and inflating the reported file/byte totals. Mirrors
        // PackOperation.BuildPlanAsync's identical `seen` dedup.
        // Case sensitivity follows the destination: Windows local FS is case-insensitive, but
        // ZIP archives and remote providers distinguish Foo.txt from foo.txt — using
        // OrdinalIgnoreCase for those silently drops the second file from the plan.
        var cmp = destFs is not null && destFs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(cmp);

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();

            var rootDest = VfsPath.Combine(destPath, VfsPath.GetRelative(sourceBasePath, root.FullPath));
            var rootAdded = seen.Add(rootDest);
            if (rootAdded)
                plan.Add((root, rootDest));

            if (!root.IsDirectory)
                continue;

            IReadOnlyList<FileEntry> children;
            try
            {
                children = await sourceFs.EnumerateDeepAsync(root.FullPath, includeHidden: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Copy: cannot enumerate {root.FullPath}: {ex.Message}");
                // Don't leave a "planned but silently empty" destination folder for a subtree
                // that couldn't actually be read.
                if (rootAdded)
                {
                    plan.RemoveAt(plan.Count - 1);
                    seen.Remove(rootDest);
                }
                enumerationFailures?.Add(root.FullPath);
                continue;
            }

            foreach (var child in children.OrderBy(c => c.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                // A junction/symlink inside the tree is listed by EnumerateDeepAsync (with
                // ReparsePointGuard.SkipRecursion preventing descent) but must not be turned
                // into an empty placeholder directory at the destination — that looks like a
                // successful copy of a folder whose contents are silently absent. Report it
                // alongside enumeration failures so the user is told what was skipped.
                if (child.IsDirectory && (child.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    enumerationFailures?.Add($"{child.FullPath} (junction/symlink — not followed)");
                    continue;
                }
                var childDest = VfsPath.Combine(rootDest, VfsPath.GetRelative(root.FullPath, child.FullPath));
                if (seen.Add(childDest))
                    plan.Add((child, childDest));
            }
        }

        return plan;
    }

    /// <summary>
    /// Same-path guard, destination-parent-directory creation, and overwrite-conflict resolution
    /// for one file - the metadata/existence-check work that has to run per file regardless of
    /// whether the actual byte copy goes through the per-file path below or
    /// <see cref="CopyFilesBatchAsync"/>'s single-pass batch read. Returns <c>null</c> if the file
    /// should be skipped entirely (already accounted in <see cref="_bytesProcessed"/> when the
    /// skip came from conflict resolution - not when it came from the self-copy guard, matching
    /// the original single-method version's behavior).
    /// </summary>
    private async Task<string?> PrepareFileDestinationAsync(FileEntry file, string destPath, CancellationToken ct)
    {
        // Source == destination: copying a file onto itself. Without this guard,
        // CopyFromStreamAsync opens the dest for writing (truncating it to 0), then
        // OpenReadAsync reads from the now-empty source — silent data loss.
        if (string.Equals(file.FullPath, destPath, StringComparison.OrdinalIgnoreCase))
            return null;

        var destDir = VfsPath.GetParent(destPath);
        if (!string.IsNullOrEmpty(destDir))
            await _destFs.CreateDirectoryAsync(destDir, ct).ConfigureAwait(false);

        var resolution = await ConflictResolver.ResolveAsync(_destFs, file.FullPath, destPath, file, _options, ct).ConfigureAwait(false);
        if (!resolution.Proceed)
        {
            Interlocked.Add(ref _bytesProcessed, file.Size);
            return null;
        }
        return resolution.TargetPath;
    }

    private async Task CopyFileWithProgress(FileEntry file, string destPath, CancellationToken ct)
    {
        var actualDestPath = await PrepareFileDestinationAsync(file, destPath, ct).ConfigureAwait(false);
        if (actualDestPath == null)
            return;

        using (var src = await _sourceFs.OpenReadAsync(file.FullPath, ct).ConfigureAwait(false))
        {
            // Reports progress as bytes are actually read, not only once the whole file finishes -
            // without this (audit finding G050), copying a single very large file left the
            // indicator frozen until the entire file had already landed. Also the pause checkpoint
            // for a file large enough that "between files" would otherwise mean a very long delay
            // before Pause actually takes effect.
            using var progressSrc = new ProgressStream(src, n =>
            {
                Interlocked.Add(ref _bytesProcessed, n);
                WaitIfPausedSync(ct);
                ReportThrottled(() => ReportProgress(file.Name));
            });
            await _destFs.CopyFromStreamAsync(actualDestPath, progressSrc, ct).ConfigureAwait(false);
        }

        _writtenPaths.Add(file.FullPath);

        if (_options.CopyAttributes && file.Attributes != default)
            await TryApplyAttributes(actualDestPath, file.Attributes, ct).ConfigureAwait(false);
        if (_options.CopyTimestamps)
            ApplyTimestamps(_destFs, actualDestPath, file);
    }

    /// <summary>
    /// Runs <see cref="PrepareFileDestinationAsync"/> for every file (may prompt, may rename -
    /// cheap metadata work, not the expensive archive read), then hands the survivors to
    /// <paramref name="batchSrc"/> for a single sequential pass instead of one
    /// <see cref="IFileSystem.OpenReadAsync"/> call per file.
    /// </summary>
    private async Task CopyFilesBatchAsync(
        IBatchReadableFileSystem batchSrc,
        List<(FileEntry Entry, string DestPath)> fileEntries,
        List<string> copyFailures,
        CancellationToken ct)
    {
        var cmp = _sourceFs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var byPath = new Dictionary<string, (FileEntry Entry, string DestPath)>(fileEntries.Count, cmp);
        var items = new List<(string SourcePath, string DestPath)>(fileEntries.Count);

        foreach (var (entry, destFullPath) in fileEntries)
        {
            ct.ThrowIfCancellationRequested();

            string? actualDestPath;
            try
            {
                actualDestPath = await PrepareFileDestinationAsync(entry, destFullPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Copy: failed for {entry.FullPath}: {ex.Message}");
                copyFailures.Add(entry.FullPath);
                _filesProcessed++;
                ReportProgress(entry.Name);
                continue;
            }
            if (actualDestPath == null)
            {
                _filesProcessed++;
                ReportProgress(entry.Name);
                continue;
            }

            byPath[entry.FullPath] = (entry, actualDestPath);
            items.Add((entry.FullPath, actualDestPath));
        }

        if (items.Count == 0)
            return;

        var copiedPaths = new HashSet<string>(cmp);
        try
        {
            await batchSrc.CopyManyToAsync(items, _destFs, async (sourcePath, bytesWritten, innerCt) =>
            {
                if (!byPath.TryGetValue(sourcePath, out var target))
                    return; // defensive only - every sourcePath here came from `items` above

                copiedPaths.Add(sourcePath);
                _writtenPaths.Add(sourcePath);
                Interlocked.Add(ref _bytesProcessed, bytesWritten);

                if (_options.CopyAttributes && target.Entry.Attributes != default)
                    await TryApplyAttributes(target.DestPath, target.Entry.Attributes, innerCt).ConfigureAwait(false);
                if (_options.CopyTimestamps)
                    ApplyTimestamps(_destFs, target.DestPath, target.Entry);

                _filesProcessed++;
                ReportProgress(target.Entry.Name);
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Warning($"Copy: batch read failed: {ex.Message}");
            // Whatever the batch call never got to (including everything, if it failed before
            // copying anything) is reported the same way a per-file failure would be.
            foreach (var (sourcePath, _) in items)
            {
                if (copiedPaths.Contains(sourcePath)) continue;
                copyFailures.Add(sourcePath);
                _filesProcessed++;
                ReportProgress(sourcePath);
            }
            return;
        }

        // An item the batch call itself silently skipped (its entry vanished from the archive
        // between listing and this read, logged by CopyManyToAsync) never invoked the callback -
        // still needs to be reported, matching a per-file OpenReadAsync throwing FileNotFoundException.
        foreach (var (sourcePath, _) in items)
        {
            if (copiedPaths.Contains(sourcePath)) continue;
            copyFailures.Add(sourcePath);
            _filesProcessed++;
            ReportProgress(sourcePath);
        }
    }

    private async Task TryApplyAttributes(string path, FileAttributes attributes, CancellationToken ct)
    {
        try
        {
            await _destFs.SetAttributesAsync(path, attributes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Warning($"Copy: cannot set attributes on {path}: {ex.Message}");
        }
    }

    /// <summary>Timestamps are only meaningful on a real filesystem; archives carry their own.</summary>
    internal static void ApplyTimestamps(IFileSystem destFs, string path, FileEntry source)
    {
        if (!destFs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths))
            return;

        try
        {
            var accessible = FileSystem.LongPath.EnsureAccessible(path);
            if (source.CreatedTimeUtc != default) File.SetCreationTimeUtc(accessible, source.CreatedTimeUtc);
            if (source.LastWriteTimeUtc != default) File.SetLastWriteTimeUtc(accessible, source.LastWriteTimeUtc);
            if (source.LastAccessTimeUtc != default) File.SetLastAccessTimeUtc(accessible, source.LastAccessTimeUtc);
        }
        catch (Exception ex)
        {
            LogService.Warning($"Copy: cannot set timestamps on {path}: {ex.Message}");
        }
    }

    private void ReportProgress(string currentFile)
    {
        Report(new OperationProgress
        {
            Percent = _bytesTotal > 0 ? (int)Math.Min(100, _bytesProcessed * 100 / _bytesTotal)
                : _filesTotal > 0 ? _filesProcessed * 100 / _filesTotal : 0,
            CurrentFile = currentFile,
            BytesProcessed = _bytesProcessed,
            BytesTotal = _bytesTotal,
            FilesProcessed = _filesProcessed,
            FilesTotal = _filesTotal
        });
    }
}
