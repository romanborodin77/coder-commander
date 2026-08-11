using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;

namespace CoderCommander.Operations;

/// <summary>
/// Extracts entries from an archive in a single pass.
/// <para>
/// Backs both the explicit "unpack" command and a plain copy whose source panel is browsing an
/// archive. Selected folders expand to everything below them; an empty selection means the whole
/// archive. Entries are addressed by their <see cref="ArchiveEntryRecord.Index"/>, so names that
/// were stored in a legacy code page resolve to the very same bytes the panel displays.
/// </para>
/// </summary>
public sealed class UnpackOperation : FileOperation
{
    public override OperationType Type => OperationType.Unpack;
    public override string Title => "Unpack";

    private readonly string _archivePath;
    private readonly IReadOnlyList<FileEntry> _items;
    private readonly string _innerBasePath;
    private readonly IFileSystem _destFs;
    private readonly string _destPath;
    private readonly TransferOptions _options;
    private readonly bool _removeSource;

    private int _filesProcessed;
    private int _filesTotal;
    private long _bytesProcessed;
    private long _bytesTotal;

    // Populated when ExtractAsync catches a real I/O failure for an entry (locked/inaccessible
    // destination file, disk error, ...) - deliberately separate from encrypted/link entries,
    // which are an intentional, already-logged skip, not a failure.
    private readonly List<string> _extractFailures = new();

    /// <summary>Creates an unpack operation that extracts entries from an archive.</summary>
    /// <param name="items">Entries to extract; empty means the whole archive.</param>
    /// <param name="innerBasePath">Folder inside the archive the paths are relative to.</param>
    /// <param name="removeSource">Drop the extracted entries from the archive afterwards (move semantics).</param>
    public UnpackOperation(
        string archivePath,
        IReadOnlyList<FileEntry> items,
        string innerBasePath,
        IFileSystem destFs,
        string destPath,
        TransferOptions? options = null,
        bool removeSource = false)
    {
        _archivePath = archivePath;
        _items = items;
        _innerBasePath = VfsPath.NormalizeInner(innerBasePath);
        _destFs = destFs;
        _destPath = destPath;
        _options = options ?? new TransferOptions();
        _removeSource = removeSource;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var format = ArchiveFormatRegistry.Detect(_archivePath)
            ?? throw new NotSupportedException($"Unsupported archive format: {_archivePath}");

        var extracted = new List<ArchiveEntryRecord>();

        using (var reader = format.OpenRead(_archivePath))
        {
            var directory = await reader.ReadDirectoryAsync(ct).ConfigureAwait(false);
            // Audit Phase 5/6 (AUDIT-FINDINGS.md §7.1, archive_fuzz): IsValid is false only when
            // the container itself couldn't be read (corrupt/truncated/locked) - ArchiveDirectory's
            // own doc comment says as much. Before this check, that case fell through to the
            // selected.Count == 0 branch below exactly like a genuinely empty archive, silently
            // completing with nothing extracted and no error at all - confirmed with a truncated
            // ZIP: State ended up Completed, LastError null. Distinguishing them here is what makes
            // "archive never actually opened" surface as a real, reported failure.
            if (!directory.IsValid)
                throw new IOException($"Archive could not be read (corrupt, truncated, or locked): {_archivePath}");

            var selected = SelectRecords(directory.Entries);
            if (selected.Count == 0)
                return;

            _filesTotal = selected.Count;
            _bytesTotal = selected.Where(r => !r.IsDirectory).Sum(r => r.Size);

            RejectIfBombLike(selected);
            await RejectIfWouldExhaustDisk(ct).ConfigureAwait(false);

            await _destFs.CreateDirectoryAsync(_destPath, ct).ConfigureAwait(false);

            if (reader.SupportsRandomAccess)
            {
                foreach (var record in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    var content = record.IsDirectory ? null : reader.OpenEntry(record);
                    await ProcessRecordAsync(record, content, extracted, ct).ConfigureAwait(false);
                }
            }
            else
            {
                // No central directory to seek into (TAR/TAR.GZ) - a single forward pass is the
                // only option, so pick out the wanted entries as they're encountered instead of
                // opening each one individually afterwards.
                var wanted = new HashSet<int>(selected.Select(r => r.Index));
                await foreach (var item in reader.ScanAsync(ct).ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!wanted.Contains(item.Entry.Index))
                    {
                        item.Content.Dispose();
                        continue;
                    }

                    await ProcessRecordAsync(item.Entry, item.Entry.IsDirectory ? null : item.Content, extracted, ct).ConfigureAwait(false);
                    if (item.Entry.IsDirectory)
                        item.Content.Dispose();
                }
            }
        }

        if (_removeSource && extracted.Count > 0)
            await RemoveExtractedAsync(format, extracted, ct).ConfigureAwait(false);

        // Extract everything that could be extracted (above) before reporting the failure,
        // rather than aborting the whole operation the instant one entry can't be written - but
        // still fail loudly at the end instead of silently completing with fewer files than the
        // archive actually contained (the same WipeOperation/PackOperation.RemoveSourcesAsync
        // precedent: collect failures, still do the achievable work, then throw a clear summary).
        if (_extractFailures.Count > 0)
            throw new IOException(
                $"Unpacked successfully, but {_extractFailures.Count} entry(ies) could not be extracted: " +
                $"{string.Join(", ", _extractFailures.Take(5))}" +
                (_extractFailures.Count > 5 ? $" and {_extractFailures.Count - 5} more" : ""));
    }

    private async Task ProcessRecordAsync(ArchiveEntryRecord record, Stream? content, List<ArchiveEntryRecord> extracted, CancellationToken ct)
    {
        using var _ = content;

        var relative = Relativize(record.FullName);
        if (relative.Length == 0)
        {
            _filesProcessed++;
            return;
        }

        if (ArchiveSafety.EscapesTarget(relative) || ArchiveSafety.EscapesRoot(_destPath, relative))
        {
            LogService.Warning($"Unpack: refusing traversal entry {record.FullName}");
            _filesProcessed++;
            return;
        }

        if (record.IsEncrypted)
        {
            // No password-prompt UI exists (by design - see the plan's "Прочие решения"); rather
            // than let the reader throw a raw crypto exception the moment its stream is touched,
            // skip the entry cleanly and let the rest of the archive extract normally.
            LogService.Warning($"Unpack: skipping encrypted entry {record.FullName}");
            _filesProcessed++;
            return;
        }

        if (record.IsLink)
        {
            // No reader materializes a link's target as real content (see IArchiveReader.ScanAsync
            // implementations) - writing a 0-byte file in its place would silently look like real,
            // if empty, data. Skip it visibly instead.
            LogService.Warning($"Unpack: skipping symlink/hardlink entry {record.FullName}");
            _filesProcessed++;
            return;
        }

        var target = VfsPath.Combine(_destPath, relative);

        if (record.IsDirectory)
        {
            await _destFs.CreateDirectoryAsync(target, ct).ConfigureAwait(false);
            extracted.Add(record);
            _filesProcessed++;
            ReportProgress(VfsPath.GetName(relative));
            return;
        }

        if (content != null && await ExtractAsync(record, content, target, relative, ct).ConfigureAwait(false))
            extracted.Add(record);

        _filesProcessed++;
        ReportProgress(VfsPath.GetName(relative));
    }

    /// <summary>Resolves the selection into the concrete set of records to extract.</summary>
    private List<ArchiveEntryRecord> SelectRecords(IReadOnlyList<ArchiveEntryRecord> all)
    {
        if (_items.Count == 0)
        {
            var basePrefix = _innerBasePath.Length == 0 ? "" : _innerBasePath + "/";
            return all.Where(r => r.FullName.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase)
                                  && r.FullName.Trim('/').Length > basePrefix.Trim('/').Length)
                      .ToList();
        }

        var picked = new List<ArchiveEntryRecord>();
        var seen = new HashSet<int>();

        foreach (var item in _items)
        {
            var inner = VfsPath.GetInner(item.FullPath);
            if (inner.Length == 0)
                continue;

            var prefix = inner + "/";
            foreach (var record in all)
            {
                var name = record.FullName.Trim('/');
                var isSelf = string.Equals(name, inner, StringComparison.OrdinalIgnoreCase);
                var isBelow = record.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                if ((isSelf || isBelow) && seen.Add(record.Index))
                    picked.Add(record);
            }
        }

        return picked;
    }

    private string Relativize(string entryName)
    {
        var name = entryName.Trim('/');
        if (_innerBasePath.Length == 0)
            return name;

        if (name.Length > _innerBasePath.Length &&
            name.StartsWith(_innerBasePath, StringComparison.OrdinalIgnoreCase) &&
            name[_innerBasePath.Length] == '/')
            return name[(_innerBasePath.Length + 1)..];

        var cut = name.LastIndexOf('/');
        return cut < 0 ? name : name[(cut + 1)..];
    }

    /// <summary>
    /// Refuses to start extraction when the selection is pathological in a way the free-space
    /// check below cannot see: too many entries, too extreme a compression ratio, or path
    /// nesting deep enough that no legitimate archive would produce it. See
    /// <see cref="UnpackLimits"/> for the thresholds and why each exists.
    ///
    /// <para>Entirely from metadata already read into <paramref name="selected"/> - nothing here
    /// opens or inflates a single entry, so a hostile archive is rejected at the same "before one
    /// byte is written" point the free-space check already achieves.</para>
    ///
    /// <para>Internal, not private: the entry-count threshold is 200,000, and building a real
    /// archive with that many entries just to exercise one comparison would make the test suite
    /// slower for no benefit - this method reads nothing but the list it's given, so a synthetic
    /// one is a faithful test of the real decision.</para>
    /// </summary>
    internal static void RejectIfBombLike(IReadOnlyList<ArchiveEntryRecord> selected)
    {
        if (selected.Count > UnpackLimits.MaxEntries)
            throw new IOException(
                $"This archive has {selected.Count:N0} entries, more than the {UnpackLimits.MaxEntries:N0} this app will extract in one operation.");

        long uncompressed = 0, compressed = 0;
        foreach (var record in selected)
        {
            if (record.IsDirectory || record.PackedSize <= 0) continue;
            uncompressed += record.Size;
            compressed += record.PackedSize;
        }
        // compressed == 0 means no selected entry reports a packed size at all (TAR and TAR.GZ
        // entries don't - the format compresses the whole stream, not per entry) - nothing to
        // compute a ratio from, so this check is skipped rather than guessed at, the same
        // "can't determine" philosophy the free-space check below already follows.
        if (compressed > 0 && uncompressed / compressed > UnpackLimits.MaxRatio)
            throw new IOException(
                $"This archive would expand {uncompressed / compressed:N0}x, more than the {UnpackLimits.MaxRatio}x this app will extract - it has the shape of a decompression bomb.");

        foreach (var record in selected)
        {
            var depth = record.FullName.Count(c => c is '/' or '\\');
            if (depth > UnpackLimits.MaxPathDepth)
                throw new IOException(
                    $"\"{record.FullName}\" is nested {depth} levels deep, more than the {UnpackLimits.MaxPathDepth} this app will extract.");
        }
    }

    /// <summary>
    /// Refuses to start extraction when the archive's own declared uncompressed size already
    /// exceeds the free space at the destination - the defining trait of a decompression bomb
    /// (a small compressed file whose metadata honestly advertises a huge uncompressed payload,
    /// e.g. the classic "42.zip"). <see cref="_bytesTotal"/> comes straight from the entries'
    /// declared <see cref="ArchiveEntryRecord.Size"/>, computed before a single byte is written,
    /// so this catches the bomb without needing to actually inflate anything first.
    /// </summary>
    private async Task RejectIfWouldExhaustDisk(CancellationToken ct)
    {
        if (_bytesTotal <= 0)
            return;

        var (freeBytes, _) = await _destFs.GetDriveSpaceAsync(_destPath, ct).ConfigureAwait(false);
        if (freeBytes <= 0)
            return; // destination doesn't report free space (e.g. writing into another archive) - can't check

        if (_bytesTotal > freeBytes)
            throw new IOException(
                $"Unpacking would need {FormatUtils.FormatSize(_bytesTotal)} but only {FormatUtils.FormatSize(freeBytes)} is free at the destination.");
    }

    private async Task<bool> ExtractAsync(
        ArchiveEntryRecord record,
        Stream content,
        string target,
        string relative,
        CancellationToken ct)
    {
        // Deliberately not VfsPath.GetParent(target): target is _destPath with the archive
        // entry's relative name folded in, and a legal archive entry name may itself contain
        // '|' (ZIP/TAR impose no such restriction). VfsPath.IsArchive uses a bare '|'-substring
        // check to spot VFS-flavored paths, so once that character reaches a real disk path it
        // gets misread as an archive path and mis-split on the wrong character entirely. relative
        // is always '/'-normalized archive-inner text (see Relativize), never itself VFS-flavored,
        // so split it directly instead of routing it back through IsArchive's ambiguous sniff.
        var cut = relative.LastIndexOf('/');
        var relDir = cut < 0 ? "" : relative[..cut];
        var parent = relDir.Length == 0 ? _destPath : VfsPath.Combine(_destPath, relDir);
        if (!string.IsNullOrEmpty(parent))
            await _destFs.CreateDirectoryAsync(parent, ct).ConfigureAwait(false);

        var sourceInfo = new FileEntry(
            ArchivePath.MakePath(_archivePath, record.FullName),
            false, true, record.Size, lastWriteTimeUtc: record.LastWriteTimeUtc);

        var resolution = await ConflictResolver.ResolveAsync(_destFs, sourceInfo.FullPath, target, sourceInfo, _options, ct).ConfigureAwait(false);
        if (!resolution.Proceed)
            return false;
        var actualTarget = resolution.TargetPath;

        try
        {
            using (var counting = new ProgressStream(content, chunk =>
            {
                _bytesProcessed += chunk;
                ReportThrottled(() => ReportProgress(record.FullName));
            }))
            {
                await _destFs.CopyFromStreamAsync(actualTarget, counting, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Warning($"Unpack failed for {record.FullName}: {ex.Message}");
            _extractFailures.Add(record.FullName);
            return false;
        }

        if (_options.CopyTimestamps &&
            _destFs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths) &&
            record.LastWriteTimeUtc != default)
        {
            try { File.SetLastWriteTimeUtc(actualTarget, record.LastWriteTimeUtc); }
            catch (Exception ex) { LogService.Warning($"Unpack: cannot stamp {actualTarget}: {ex.Message}"); }
        }

        return true;
    }

    private async Task RemoveExtractedAsync(IArchiveFormat format, IReadOnlyList<ArchiveEntryRecord> extracted, CancellationToken ct)
    {
        try
        {
            await using (var writer = format.OpenWrite(_archivePath, new ArchiveWriteOptions()))
            {
                foreach (var record in extracted.OrderByDescending(r => r.Index))
                    writer.TryDeleteEntry(record);
                await writer.CommitAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Error($"Unpack: cannot remove entries from {_archivePath}: {ex.Message}", ex);
            // Rethrow rather than swallow: silently reporting this operation as Completed when
            // "move" left the entries behind in the archive (as well as extracted to disk) would
            // be worse than a clearly worded failure.
            throw new IOException(
                $"Unpacked successfully, but the extracted entries could not be removed from the archive (move left copies in both places): {ex.Message}", ex);
        }
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
