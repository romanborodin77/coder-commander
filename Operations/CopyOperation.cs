using CoderCommander.Archives;
using CoderCommander.FileSystem;
using CoderCommander.Services;

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
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var plan = await FlattenAsync(_sourceFs, _files, _sourceBasePath, _destPath, ct).ConfigureAwait(false);

        _filesTotal = plan.Count(p => !p.Entry.IsDirectory);
        _bytesTotal = plan.Where(p => !p.Entry.IsDirectory).Sum(p => p.Entry.Size);

        await _destFs.CreateDirectoryAsync(_destPath, ct).ConfigureAwait(false);

        foreach (var (entry, destFullPath) in plan)
        {
            ct.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
            {
                await _destFs.CreateDirectoryAsync(destFullPath, ct).ConfigureAwait(false);
                continue;
            }

            await CopyFileWithProgress(entry, destFullPath, ct).ConfigureAwait(false);

            _filesProcessed++;
            ReportProgress(entry.Name);
        }
    }

    /// <summary>
    /// Expands the selection into every entry that has to be created, keeping folders ahead of
    /// their content so that the destination tree is always built top-down.
    /// </summary>
    internal static async Task<List<(FileEntry Entry, string Destination)>> FlattenAsync(
        IFileSystem sourceFs,
        IReadOnlyList<FileEntry> roots,
        string sourceBasePath,
        string destPath,
        CancellationToken ct)
    {
        var plan = new List<(FileEntry, string)>();

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();

            var rootDest = VfsPath.Combine(destPath, VfsPath.GetRelative(sourceBasePath, root.FullPath));
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
                continue;
            }

            foreach (var child in children.OrderBy(c => c.FullPath, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                plan.Add((child, VfsPath.Combine(rootDest, VfsPath.GetRelative(root.FullPath, child.FullPath))));
            }
        }

        return plan;
    }

    private async Task CopyFileWithProgress(FileEntry file, string destPath, CancellationToken ct)
    {
        var destDir = VfsPath.GetParent(destPath);
        if (!string.IsNullOrEmpty(destDir))
            await _destFs.CreateDirectoryAsync(destDir, ct).ConfigureAwait(false);

        var resolution = await ConflictResolver.ResolveAsync(_destFs, file.FullPath, destPath, file, _options, ct).ConfigureAwait(false);
        if (!resolution.Proceed)
            return;
        var actualDestPath = resolution.TargetPath;

        using (var src = await _sourceFs.OpenReadAsync(file.FullPath, ct).ConfigureAwait(false))
        {
            await _destFs.CopyFromStreamAsync(actualDestPath, src, ct).ConfigureAwait(false);
        }

        Interlocked.Add(ref _bytesProcessed, file.Size);

        if (_options.CopyAttributes && file.Attributes != default)
            await TryApplyAttributes(actualDestPath, file.Attributes, ct).ConfigureAwait(false);
        if (_options.CopyTimestamps)
            ApplyTimestamps(_destFs, actualDestPath, file);
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
        if (destFs is not LocalFileSystem)
            return;

        try
        {
            if (source.CreatedTimeUtc != default) File.SetCreationTimeUtc(path, source.CreatedTimeUtc);
            if (source.LastWriteTimeUtc != default) File.SetLastWriteTimeUtc(path, source.LastWriteTimeUtc);
            if (source.LastAccessTimeUtc != default) File.SetLastAccessTimeUtc(path, source.LastAccessTimeUtc);
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
            Percent = _filesTotal > 0 ? _filesProcessed * 100 / _filesTotal : 0,
            CurrentFile = currentFile,
            BytesProcessed = _bytesProcessed,
            BytesTotal = _bytesTotal,
            FilesProcessed = _filesProcessed,
            FilesTotal = _filesTotal
        });
    }
}
