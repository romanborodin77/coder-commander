using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.FileSystem.Materialization;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Writes files into a ZIP archive in a single pass.
/// <para>
/// The source side is an arbitrary <see cref="IFileSystem"/>, so this is what backs both the
/// explicit "pack" command and a plain copy whose destination panel is browsing an archive.
/// Directories are expanded recursively and the archive is opened exactly once, which keeps the
/// cost linear instead of rewriting the container for every file.
/// </para>
/// <para>
/// The archive CONTAINER also lives on an arbitrary <see cref="IFileSystem"/> (<see cref="_archiveFs"/>) -
/// <see cref="IArchiveFormat.OpenRead"/>/<see cref="IArchiveFormat.OpenWrite"/> only accept a real
/// local path, so a non-local container is materialized via <see cref="MaterializedFile"/> at the
/// start of <see cref="ExecuteCoreAsync"/> and written back once the writer has closed. Everywhere
/// below that touches the file SYSTEM uses the materialized local path; everywhere that builds a
/// user-facing or archive-inner path (conflict resolution, entry addressing) keeps using
/// <see cref="_archivePath"/>, the container's real identity - the two must never be swapped, since
/// <see cref="_archivePath"/> may not even be a path <c>System.IO</c> can resolve.
/// </para>
/// </summary>
public sealed class PackOperation : FileOperation
{
    public override OperationType Type => OperationType.Pack;
    public override string Title => "Pack";

    private readonly IFileSystem _sourceFs;
    private readonly IReadOnlyList<FileEntry> _files;
    private readonly string _sourceBasePath;
    private readonly IFileSystem _archiveFs;
    private readonly string _archivePath;
    private readonly string _innerDestPath;
    private readonly TransferOptions _options;
    private readonly bool _removeSource;

    private int _filesProcessed;
    private int _filesTotal;
    private long _bytesProcessed;
    private long _bytesTotal;

    /// <summary>Creates a pack operation that writes files into an archive.</summary>
    /// <param name="archiveFs">The filesystem the archive FILE itself lives on - never the
    /// archive's own internal VFS. <see cref="FileSystem.LocalFileSystem"/> for the common case.</param>
    /// <param name="innerDestPath">Folder inside the archive that receives the files; empty for the root.</param>
    /// <param name="removeSource">Delete the originals once everything is written (move semantics).</param>
    public PackOperation(
        IFileSystem sourceFs,
        IReadOnlyList<FileEntry> files,
        string sourceBasePath,
        IFileSystem archiveFs,
        string archivePath,
        string innerDestPath = "",
        TransferOptions? options = null,
        bool removeSource = false)
    {
        _sourceFs = sourceFs;
        _files = files;
        _sourceBasePath = sourceBasePath;
        _archiveFs = archiveFs;
        _archivePath = archivePath;
        _innerDestPath = VfsPath.NormalizeInner(innerDestPath);
        _options = options ?? new TransferOptions();
        _removeSource = removeSource;
    }

    private sealed class PackItem
    {
        /// <summary>Source file entry, or null for directory-only items.</summary>
        public FileEntry? Source { get; init; }

        /// <summary>Entry name inside the archive.</summary>
        public string EntryName { get; init; } = "";

        /// <summary>True when this item represents a directory entry.</summary>
        public bool IsDirectory { get; init; }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var plan = await BuildPlanAsync(ct).ConfigureAwait(false);
        if (plan.Count == 0)
            return;

        _filesTotal = plan.Count;
        _bytesTotal = plan.Where(i => !i.IsDirectory && i.Source != null).Sum(i => i.Source!.Size);

        // Materialized AFTER confirming there's work to do (above), not before - a no-op pack
        // (empty plan) never touches the network for a non-local container. Passthrough (the
        // common, local case) copies nothing at all - see MaterializedFile's own doc comment.
        using var session = new TempSessionRoot("materialize");
        using var container = await MaterializedFile.AcquireAsync(
            _archiveFs, _archivePath, session, MaterializeOptions.ForArchiveWrite, ct).ConfigureAwait(false);
        var localArchivePath = container.LocalPath;

        var format = ArchiveFormatRegistry.Detect(localArchivePath)
            ?? throw new NotSupportedException($"Unsupported archive format: {_archivePath}");

        IReadOnlyDictionary<string, ArchiveEntryRecord> existing;
        using (var reader = format.OpenRead(localArchivePath))
        {
            var directory = await reader.ReadDirectoryAsync(ct).ConfigureAwait(false);
            existing = directory.Entries
                .GroupBy(e => e.FullName.Trim('/'), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        var written = 0;
        var writtenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var writer = format.OpenWrite(localArchivePath, new ArchiveWriteOptions { PlannedEntryNames = plan.Select(i => i.EntryName).ToList() }))
        {
            foreach (var item in plan)
            {
                ct.ThrowIfCancellationRequested();

                if (item.IsDirectory)
                {
                    if (!existing.ContainsKey(item.EntryName.Trim('/')))
                        writer.CreateDirectoryEntry(item.EntryName, DateTime.UtcNow);
                    _filesProcessed++;
                    ReportProgress(item.EntryName);
                    continue;
                }

                if (await WriteFileAsync(writer, item, existing, ct).ConfigureAwait(false))
                {
                    written++;
                    writtenPaths.Add(item.Source!.FullPath);
                }

                _filesProcessed++;
                ReportProgress(item.Source!.Name);
            }

            await writer.CommitAsync(ct).ConfigureAwait(false);
        }

        // AFTER the writer above has closed - uploading while it's still open would ship stale,
        // pre-commit bytes. No-op for a passthrough (local) container.
        container.MarkDirty();
        await container.WriteBackAsync(ct).ConfigureAwait(false);

        if (_removeSource && written > 0)
            await RemoveSourcesAsync(plan, writtenPaths, ct).ConfigureAwait(false);
    }

    /// <summary>Expands the selection into a flat, ordered list of archive entries to create.</summary>
    private async Task<List<PackItem>> BuildPlanAsync(CancellationToken ct)
    {
        var plan = new List<PackItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var idx = 0; idx < _files.Count; idx++)
        {
            var file = _files[idx];
            ct.ThrowIfCancellationRequested();

            var target = Join(_innerDestPath, VfsPath.GetRelative(_sourceBasePath, file.FullPath));
            if (target.Length == 0)
                continue;

            if (!file.IsDirectory)
            {
                if (seen.Add(target))
                    plan.Add(new PackItem { Source = file, EntryName = target });
                continue;
            }

            if (seen.Add(target + "/"))
                plan.Add(new PackItem { EntryName = target + "/", IsDirectory = true });

            IReadOnlyList<FileEntry> children;
            try
            {
                children = await _sourceFs.EnumerateDeepAsync(file.FullPath, includeHidden: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Pack: cannot enumerate {file.FullPath}: {ex.Message}");
                continue;
            }

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                var childName = Join(target, VfsPath.GetRelative(file.FullPath, child.FullPath));
                if (childName.Length == 0)
                    continue;

                if (child.IsDirectory)
                {
                    if (seen.Add(childName + "/"))
                        plan.Add(new PackItem { EntryName = childName + "/", IsDirectory = true });
                }
                else if (seen.Add(childName))
                {
                    plan.Add(new PackItem { Source = child, EntryName = childName });
                }
            }
        }

        return plan;
    }

    private async Task<bool> WriteFileAsync(
        IArchiveWriter writer,
        PackItem item,
        IReadOnlyDictionary<string, ArchiveEntryRecord> existing,
        CancellationToken ct)
    {
        var source = item.Source!;
        var entryName = item.EntryName;

        if (existing.TryGetValue(entryName.Trim('/'), out var clash))
        {
            var action = ResolveClash(source, entryName, clash, out var newName);
            if (action is OverwriteAction.Skip or OverwriteAction.SkipAll)
                return false;

            if (action == OverwriteAction.Rename && !string.IsNullOrEmpty(newName))
            {
                var slash = entryName.LastIndexOf('/');
                entryName = slash < 0 ? newName : entryName[..(slash + 1)] + newName;
            }
            else
            {
                writer.TryDeleteEntry(clash);
            }
        }

        // Skip compression for already-compressed formats
        var compression = ShouldSkipCompression(source.Extension)
            ? ArchiveCompressionSpec.Store
            : _options.Compression ?? ArchiveCompressionSpec.Balanced;

        using (var src = await _sourceFs.OpenReadAsync(source.FullPath, ct).ConfigureAwait(false))
        using (var counting = new ProgressStream(src, chunk =>
        {
            _bytesProcessed += chunk;
            ReportThrottled(() => ReportProgress(source.Name));
        }))
        {
            await writer.WriteFileAsync(entryName, counting, source.Size, source.LastWriteTimeUtc, compression, ct).ConfigureAwait(false);
        }

        return true;
    }

    private bool ShouldSkipCompression(string extension) =>
        _options.SkipCompressionForCompressedFiles &&
        (_options.AlreadyCompressedExtensions ?? DefaultAlreadyCompressedExtensions).Contains(extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>Built-in default: formats that are already compressed - no benefit from Deflate.
    /// Used whenever <see cref="TransferOptions.AlreadyCompressedExtensions"/> isn't set. Internal
    /// (not private) so <c>WinForms.SettingsForm</c>'s "restore built-in defaults" button in the
    /// Archives section can offer the same list back to the user instead of duplicating the
    /// literal extensions a second time.</summary>
    internal static readonly IReadOnlyList<string> DefaultAlreadyCompressedExtensions = new[]
    {
        ".zip", ".rar", ".7z", ".gz", ".bz2", ".xz",
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv",
        ".mp3", ".aac", ".ogg", ".flac", ".wma",
        ".pdf", ".docx", ".xlsx", ".pptx"
    };

    private OverwriteAction ResolveClash(
        FileEntry source,
        string entryName,
        ArchiveEntryRecord clash,
        out string? newName)
    {
        newName = null;

        if (_options.OverwriteResolver == null)
            return _options.Overwrite ? OverwriteAction.Overwrite : OverwriteAction.Skip;

        var destPath = ArchivePath.MakePath(_archivePath, entryName);
        var destInfo = new FileEntry(destPath, clash.IsDirectory, true, clash.Size,
            lastWriteTimeUtc: clash.LastWriteTimeUtc);

        return _options.OverwriteResolver(source.FullPath, destPath, source, destInfo, out newName);
    }

    /// <summary>
    /// Deletes only the sources that were actually written into the archive. A top-level directory is
    /// removed as a whole only if every file beneath it made it into the archive; otherwise its
    /// individually-written files are removed one by one, leaving anything skipped (e.g. via a
    /// "Skip" conflict resolution) untouched on disk.
    /// </summary>
    private async Task RemoveSourcesAsync(List<PackItem> plan, HashSet<string> writtenPaths, CancellationToken ct)
    {
        // Best-effort per file (a lock on one source shouldn't stop the rest from being cleaned
        // up), but failures are collected rather than only logged: silently reporting this
        // operation as Completed when "move" left copies behind in both places would be worse
        // than a clearly worded failure - see the throw below.
        var failures = new List<string>();

        for (var idx = 0; idx < _files.Count; idx++)
        {
            var file = _files[idx];
            try
            {
                if (!file.IsDirectory)
                {
                    if (writtenPaths.Contains(file.FullPath))
                        await _sourceFs.DeleteAsync(file.FullPath, recursive: false, ct).ConfigureAwait(false);
                    continue;
                }

                // Path-prefix containment, not TopLevelIndex: BuildPlanAsync's `seen` dedup can
                // attribute a file to whichever selection index reached it FIRST, not necessarily
                // the folder that actually contains it on disk (e.g. both a file and its
                // containing folder are selected together, in Flat View). Filtering by index
                // used to silently exclude such a file from this folder's own descendant check,
                // so "every descendant was written" could come back true while that file - never
                // written to the archive - still got swept up by the recursive delete below.
                //
                // VfsPath.IsDescendantOf, not a bare Path.DirectorySeparatorChar prefix test: a
                // remote or archive source path never contains '\', so the old prefix test always
                // found zero descendants for a non-local folder, and .All() on an empty sequence is
                // vacuously true - the recursive delete below then fired unconditionally, deleting
                // the whole source folder from the server regardless of what actually made it into
                // the archive. descendantFiles.Count == 0 no longer means "nothing to check", it
                // means "this folder has no files in the plan at all" - see the emptiness guard below.
                var descendantFiles = plan.Where(p => !p.IsDirectory && p.Source != null &&
                    VfsPath.IsDescendantOf(file.FullPath, p.Source!.FullPath)).ToList();
                // .All() on an empty descendantFiles is true - and correctly so now that the filter
                // above is VFS-aware: an empty list here genuinely means "this folder has no file
                // descendants in the plan" (an empty directory, or one containing only further empty
                // subdirectories), not "the filter couldn't find any" the way the old
                // Path.DirectorySeparatorChar prefix test silently mismatched for every remote/
                // archive path regardless of how many descendants actually existed.
                var allWritten = descendantFiles.All(d => writtenPaths.Contains(d.Source!.FullPath));

                if (allWritten)
                {
                    await _sourceFs.DeleteAsync(file.FullPath, recursive: true, ct).ConfigureAwait(false);
                    continue;
                }

                foreach (var d in descendantFiles)
                {
                    if (!writtenPaths.Contains(d.Source!.FullPath))
                        continue;

                    try
                    {
                        await _sourceFs.DeleteAsync(d.Source!.FullPath, recursive: false, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogService.Warning($"Pack: cannot remove source {d.Source!.FullPath}: {ex.Message}");
                        failures.Add(d.Source!.FullPath);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Pack: cannot remove source {file.FullPath}: {ex.Message}");
                failures.Add(file.FullPath);
            }
        }

        if (failures.Count > 0)
            throw new IOException(
                $"Packed successfully, but {failures.Count} source item(s) could not be removed " +
                $"(move left copies in both places): {string.Join(", ", failures.Take(5))}" +
                (failures.Count > 5 ? $" and {failures.Count - 5} more" : ""));
    }

    private static string Join(string head, string tail)
    {
        var normalizedTail = VfsPath.NormalizeInner(tail);
        if (normalizedTail.Length == 0) return head;
        return head.Length == 0 ? normalizedTail : head + "/" + normalizedTail;
    }

    private void ReportProgress(string currentFile)
    {
        Report(new OperationProgress
        {
            Percent = _bytesTotal > 0
                ? (int)Math.Min(100, _bytesProcessed * 100 / _bytesTotal)
                : (_filesTotal > 0 ? _filesProcessed * 100 / _filesTotal : 0),
            CurrentFile = currentFile,
            BytesProcessed = _bytesProcessed,
            BytesTotal = _bytesTotal,
            FilesProcessed = _filesProcessed,
            FilesTotal = _filesTotal
        });
    }
}
