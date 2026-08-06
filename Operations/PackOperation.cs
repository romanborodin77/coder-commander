using CoderCommander.Archives;
using CoderCommander.FileSystem;
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
/// </summary>
public sealed class PackOperation : FileOperation
{
    public override OperationType Type => OperationType.Pack;
    public override string Title => "Pack";

    private readonly IFileSystem _sourceFs;
    private readonly IReadOnlyList<FileEntry> _files;
    private readonly string _sourceBasePath;
    private readonly string _archivePath;
    private readonly string _innerDestPath;
    private readonly TransferOptions _options;
    private readonly bool _removeSource;

    private int _filesProcessed;
    private int _filesTotal;
    private long _bytesProcessed;
    private long _bytesTotal;

    /// <summary>Creates a pack operation that writes files into an archive.</summary>
    /// <param name="innerDestPath">Folder inside the archive that receives the files; empty for the root.</param>
    /// <param name="removeSource">Delete the originals once everything is written (move semantics).</param>
    public PackOperation(
        IFileSystem sourceFs,
        IReadOnlyList<FileEntry> files,
        string sourceBasePath,
        string archivePath,
        string innerDestPath = "",
        TransferOptions? options = null,
        bool removeSource = false)
    {
        _sourceFs = sourceFs;
        _files = files;
        _sourceBasePath = sourceBasePath;
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

        /// <summary>Index into <see cref="_files"/> this item was expanded from.</summary>
        public int TopLevelIndex { get; init; }
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var plan = await BuildPlanAsync(ct).ConfigureAwait(false);
        if (plan.Count == 0)
            return;

        _filesTotal = plan.Count;
        _bytesTotal = plan.Where(i => !i.IsDirectory && i.Source != null).Sum(i => i.Source!.Size);

        var format = ArchiveFormatRegistry.Detect(_archivePath)
            ?? throw new NotSupportedException($"Unsupported archive format: {_archivePath}");

        IReadOnlyDictionary<string, ArchiveEntryRecord> existing;
        using (var reader = format.OpenRead(_archivePath))
        {
            var directory = await reader.ReadDirectoryAsync(ct).ConfigureAwait(false);
            existing = directory.Entries
                .GroupBy(e => e.FullName.Trim('/'), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        var written = 0;
        var writtenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions { PlannedEntryNames = plan.Select(i => i.EntryName).ToList() }))
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
                    plan.Add(new PackItem { Source = file, EntryName = target, TopLevelIndex = idx });
                continue;
            }

            if (seen.Add(target + "/"))
                plan.Add(new PackItem { EntryName = target + "/", IsDirectory = true, TopLevelIndex = idx });

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
                        plan.Add(new PackItem { EntryName = childName + "/", IsDirectory = true, TopLevelIndex = idx });
                }
                else if (seen.Add(childName))
                {
                    plan.Add(new PackItem { Source = child, EntryName = childName, TopLevelIndex = idx });
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
    /// Used whenever <see cref="TransferOptions.AlreadyCompressedExtensions"/> isn't set.</summary>
    private static readonly IReadOnlyList<string> DefaultAlreadyCompressedExtensions = new[]
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

                var descendantFiles = plan.Where(p => p.TopLevelIndex == idx && !p.IsDirectory && p.Source != null).ToList();
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
