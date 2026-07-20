using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Services;

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
    private long _lastReportTicks;

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

    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var format = ArchiveFormatRegistry.Detect(_archivePath)
            ?? throw new NotSupportedException($"Unsupported archive format: {_archivePath}");

        var extracted = new List<ArchiveEntryRecord>();

        using (var reader = format.OpenRead(_archivePath))
        {
            var directory = await reader.ReadDirectoryAsync(ct).ConfigureAwait(false);
            var selected = SelectRecords(directory.Entries);
            if (selected.Count == 0)
                return;

            _filesTotal = selected.Count;
            _bytesTotal = selected.Where(r => !r.IsDirectory).Sum(r => r.Size);

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

        var target = VfsPath.Combine(_destPath, relative);

        if (record.IsDirectory)
        {
            await _destFs.CreateDirectoryAsync(target, ct).ConfigureAwait(false);
            extracted.Add(record);
            _filesProcessed++;
            ReportProgress(VfsPath.GetName(relative));
            return;
        }

        if (content != null && await ExtractAsync(record, content, target, ct).ConfigureAwait(false))
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

    private async Task<bool> ExtractAsync(
        ArchiveEntryRecord record,
        Stream content,
        string target,
        CancellationToken ct)
    {
        var parent = VfsPath.GetParent(target);
        if (!string.IsNullOrEmpty(parent))
            await _destFs.CreateDirectoryAsync(parent, ct).ConfigureAwait(false);

        var actualTarget = target;

        if (await _destFs.ExistsAsync(target, ct).ConfigureAwait(false))
        {
            var sourceInfo = new FileEntry(
                ArchivePath.MakePath(_archivePath, record.FullName),
                false, true, record.Size, lastWriteTimeUtc: record.LastWriteTimeUtc);

            var action = OverwriteAction.Skip;
            string? newName = null;

            if (_options.OverwriteResolver != null)
            {
                var destInfo = await _destFs.GetFileInfoAsync(target, ct).ConfigureAwait(false);
                action = _options.OverwriteResolver(sourceInfo.FullPath, target, sourceInfo, destInfo, out newName);
            }
            else if (_options.Overwrite)
            {
                action = OverwriteAction.Overwrite;
            }

            if (action is OverwriteAction.Skip or OverwriteAction.SkipAll)
                return false;

            if (action == OverwriteAction.Rename && !string.IsNullOrEmpty(newName))
                actualTarget = VfsPath.ChangeName(target, newName);
        }

        try
        {
            using (var counting = new ProgressStream(content, chunk =>
            {
                _bytesProcessed += chunk;
                ReportThrottled(record.FullName);
            }))
            {
                await _destFs.CopyFromStreamAsync(actualTarget, counting, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Warning($"Unpack failed for {record.FullName}: {ex.Message}");
            return false;
        }

        if (_options.CopyTimestamps && _destFs is LocalFileSystem && record.LastWriteTimeUtc != default)
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
        }
    }

    private void ReportThrottled(string currentFile)
    {
        var now = Environment.TickCount64;
        if (now - _lastReportTicks < 250) return;
        _lastReportTicks = now;
        ReportProgress(currentFile);
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
