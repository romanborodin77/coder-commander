using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Общая основа для переноса данных (копирование/перенос): предпросмотр объёмов,
/// рекурсивный обход, политики перезаписи, прогресс и отмена.
/// Shared base for data transfer (copy/move): size pre-scan, recursive traversal,
/// overwrite policies, progress and cancellation (ph0.2/ph0.3, exp.yml).
/// </summary>
public abstract class TransferOperation : FileOperation
{
    private readonly IFileSystem _fs;
    private readonly string _destDir;
    private readonly OverwritePolicy _policy;
    private readonly Func<string, string, OverwritePolicy>? _onConflict;
    private readonly List<string> _sources;
    private readonly TransferOptions _options;

    private long _totalBytes;
    private long _doneBytes;
    private int _totalFiles;
    private int _filesDone;

    private long _copied;
    private long _skipped;
    private long _failed;
    private readonly List<string> _errors = new();

    // Кэшированное решение для Ask + "применить ко всем". / Cached decision for Ask + "apply to all".
    private OverwritePolicy? _cachedAskPolicy;

    /// <summary>Используется ли перенос (true) вместо копирования (false). / Whether this is a move (true) vs copy (false).</summary>
    protected abstract bool IsMove { get; }

    /// <summary>Количество скопированных/перенесённых файлов. / Number of copied/moved files.</summary>
    public long Copied => _copied;

    /// <summary>Количество пропущенных файлов. / Number of skipped files.</summary>
    public long Skipped => _skipped;

    /// <summary>Количество неудач. / Number of failures.</summary>
    public long Failed => _failed;

    /// <summary>Список описаний ошибок. / List of error descriptions.</summary>
    public IReadOnlyList<string> Errors => _errors;

    protected TransferOperation(
        IFileSystem fs, IEnumerable<string> sources, string destDir,
        OverwritePolicy policy, Func<string, string, OverwritePolicy>? onConflict,
        IProgress<OperationProgress>? progress = null,
        TransferOptions? options = null)
        : base(progress)
    {
        _fs = fs;
        _destDir = destDir;
        _policy = policy;
        _onConflict = onConflict;
        _sources = new List<string>(sources);
        _options = options ?? new TransferOptions();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var skippedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in _sources)
        {
            if (Directory.Exists(s) && IsSubPathOf(_destDir, s))
                throw new InvalidOperationException($"Cannot move a folder into itself: {s}");
        }

        foreach (var s in _sources)
        {
            var name = Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var dstPath = Path.Combine(_destDir, name);
            var srcNorm = Path.GetFullPath(s).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dstNorm = Path.GetFullPath(dstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(srcNorm, dstNorm, StringComparison.OrdinalIgnoreCase))
            {
                _failed++;
                skippedSources.Add(srcNorm);
                var msg = $"Cannot copy/move file onto itself: {s}";
                _errors.Add(msg);
                LogService.Warn(msg, nameof(TransferOperation));
            }
        }

        foreach (var s in _sources)
        {
            ct.ThrowIfCancellationRequested();
            var (bytes, files) = await ScanAsync(s, ct).ConfigureAwait(false);
            _totalBytes += bytes;
            _totalFiles += files;
        }

        foreach (var s in _sources)
        {
            ct.ThrowIfCancellationRequested();
            var srcNorm = Path.GetFullPath(s).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (skippedSources.Contains(srcNorm)) continue;

            var name = Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var dstPath = Path.Combine(_destDir, name);
            LogService.Info($"ExecuteCoreAsync: source={s}, name={name}, destDir={_destDir}, dst={dstPath}", nameof(TransferOperation));
            try
            {
                await TransferEntryAsync(s, dstPath, ct).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex)
            {
                _failed++;
                var msg = $"{s}: Access denied. The file may be locked, read-only, or protected. Try closing other applications or running as administrator.";
                _errors.Add(msg);
                LogService.Error(msg, nameof(TransferOperation), ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failed++;
                _errors.Add($"{s}: {ex.Message}");
                LogService.Error($"Transfer failed for {s}: {ex.Message}", nameof(TransferOperation), ex);
            }
        }
    }

    private void WaitForPause(CancellationToken ct)
    {
        try
        {
            _options.PauseEvent?.Wait(ct);
        }
        catch (OperationCanceledException)
        {
            // Отмена во время паузы — пробрасываем исключение.
            // Cancel during pause — propagate exception.
            throw;
        }
    }

    private bool CheckSkip()
    {
        return _options.SkipCurrentFileFunc?.Invoke() ?? false;
    }

    private async Task TransferEntryAsync(string src, string dst, CancellationToken ct)
    {
        if (Directory.Exists(src)) await TransferDirectoryAsync(src, dst, ct).ConfigureAwait(false);
        else await TransferFileAsync(src, dst, ct).ConfigureAwait(false);
    }

    private async Task TransferDirectoryAsync(string src, string dst, CancellationToken ct)
    {
        LogService.Info($"TransferDirectoryAsync: src={src}, dst={dst}", nameof(TransferOperation));

        // FIXED: Symlink traversal — не следуем по reparse points при копировании.
        // Don't follow reparse points (symlinks/junctions) when copying.
        var srcDirInfo = new DirectoryInfo(src);
        if ((srcDirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            // Для symlink/junction — копируем как файл-ссылку, не рекурсивно.
            // For symlink/junction — copy as a link, not recursively.
            LogService.Info($"Skipping reparse point contents: {src}", nameof(TransferOperation));
            try { await _fs.CreateDirectoryAsync(dst, ct).ConfigureAwait(false); }
            catch { }
            return;
        }

        bool sameVolume = IsMove && SameVolume(src, dst);
        if (sameVolume)
        {
            ct.ThrowIfCancellationRequested();
            await _fs.MoveAsync(src, dst, overwrite: true, ct).ConfigureAwait(false);
            var (bytes, files) = await ScanAsync(src, CancellationToken.None).ConfigureAwait(false);
            _doneBytes += bytes;
            _filesDone += files;
            _copied += files;
            ReportProgress(dst);
            return;
        }

        ct.ThrowIfCancellationRequested();
        await _fs.CreateDirectoryAsync(dst, ct).ConfigureAwait(false);
        _filesDone++;
        _copied++;
        ReportProgress(dst);

        var dstDirInfo = new DirectoryInfo(dst);

        var entries = await _fs.EnumerateAsync(src, ct: ct).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            WaitForPause(ct);
            ct.ThrowIfCancellationRequested(); // Проверка после выхода из паузы / Check after pause

            if (IsSystemFolder(entry.Name))
            {
                LogService.Info($"Skipping system folder: {entry.Name}", nameof(TransferOperation));
                continue;
            }

            if (entry.IsDirectory)
            {
                await TransferDirectoryAsync(entry.FullPath, Path.Combine(dst, entry.Name), ct).ConfigureAwait(false);
            }
            else
            {
                await TransferFileAsync(entry.FullPath, Path.Combine(dst, entry.Name), ct).ConfigureAwait(false);
            }
        }

        ct.ThrowIfCancellationRequested();

        if (_options.CopyTimestamps)
        {
            try
            {
                dstDirInfo.CreationTimeUtc = srcDirInfo.CreationTimeUtc;
                dstDirInfo.LastWriteTimeUtc = srcDirInfo.LastWriteTimeUtc;
                dstDirInfo.LastAccessTimeUtc = srcDirInfo.LastAccessTimeUtc;
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to copy timestamps for directory {dst}: {ex.Message}", nameof(TransferOperation));
            }
        }

        if (_options.CopyNtfsPermissions && _fs is LocalFileSystem)
        {
            try
            {
                LocalFileSystem.CopyAcl(src, dst);
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to copy ACL for directory {dst}: {ex.Message}", nameof(TransferOperation));
            }
        }

        if (IsMove)
        {
            ct.ThrowIfCancellationRequested();
            await _fs.DeleteAsync(src, recursive: true, ct).ConfigureAwait(false);
        }
    }

    private async Task TransferFileAsync(string src, string dst, CancellationToken ct)
    {
        WaitForPause(ct);
        ct.ThrowIfCancellationRequested();
        var info = new FileInfo(src);
        long size = info.Exists ? info.Length : 0;

        LogService.Info($"TransferFileAsync: src={src}, dst={dst}, size={size}", nameof(TransferOperation));

        string finalDst = dst;
        if (File.Exists(dst) || Directory.Exists(dst))
        {
            LogService.Info($"TransferFileAsync: destination exists, calling ResolvePolicy", nameof(TransferOperation));
            var policy = ResolvePolicy(src, dst);
            ct.ThrowIfCancellationRequested(); // Проверка после диалога конфликта / Check after conflict dialog
            if (policy == OverwritePolicy.Skip) { _skipped++; _filesDone++; ReportProgress(dst); return; }
            if (policy == OverwritePolicy.AutoRename) finalDst = LocalFileSystem.UniqueName(dst);
            else if (policy == OverwritePolicy.OverwriteOlder || policy == OverwritePolicy.OverwriteSmaller)
            {
                var di = new FileInfo(dst);
                bool overwrite = policy == OverwritePolicy.OverwriteOlder
                    ? info.LastWriteTimeUtc > di.LastWriteTimeUtc
                    : info.Length < di.Length;
                if (!overwrite) { _skipped++; _filesDone++; ReportProgress(dst); return; }
            }
        }
        else
        {
            LogService.Info($"TransferFileAsync: destination does not exist, proceeding with copy/move", nameof(TransferOperation));
        }

        bool sameVolume = IsMove && SameVolume(src, finalDst);
        if (IsMove && sameVolume)
        {
            ct.ThrowIfCancellationRequested(); // Проверка перед перемещением / Check before move
            await _fs.MoveAsync(src, finalDst, overwrite: true, ct).ConfigureAwait(false);
            _doneBytes += size;
            _filesDone++;
            _copied++;
            ReportProgress(finalDst);
            CopyAttributesIfNeeded(src, finalDst);
            return;
        }

        if (CheckSkip())
        {
            _skipped++;
            _filesDone++;
            _doneBytes += size;
            ReportProgress(finalDst);
            return;
        }

        ct.ThrowIfCancellationRequested(); // Проверка перед началом копирования / Check before copy starts

        if (_options.ReserveDiskSpace && size > 0)
        {
            try
            {
                using var prealloc = new FileStream(finalDst, FileMode.Create, FileAccess.Write, FileShare.None);
                prealloc.SetLength(size);
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to pre-allocate {size} bytes for {finalDst}: {ex.Message}", nameof(TransferOperation));
            }
        }

        long prev = 0;
        var bp = new Progress<long>(cur =>
        {
            _doneBytes += cur - prev;
            prev = cur;
            Report(new OperationProgress(finalDst, cur, size, _doneBytes, _totalBytes, _filesDone, _totalFiles));
        });

        try
        {
            ct.ThrowIfCancellationRequested();
            bool copied = await FileCopyHelper.CopyFileAsync(src, finalDst, bp, ct,
                _options.BufferSize, _options.PauseEvent, _options.SkipCurrentFileFunc).ConfigureAwait(false);
            if (!copied)
            {
                // Файл пропущен по флагу Skip во время копирования.
                // File was skipped via Skip flag during copy.
                _skipped++;
                _filesDone++;
                _doneBytes += size;
                ReportProgress(finalDst);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Отмена — удаляем неполный файл, пробрасываем исключение дальше.
            // Cancel — delete partial file, propagate exception.
            TryDeletePartialFile(finalDst);
            throw;
        }
        catch (Exception)
        {
            // Ошибка копирования — удаляем неполный файл.
            // Copy error — delete partial file.
            TryDeletePartialFile(finalDst);
            throw;
        }

        _doneBytes += size - prev;
        _filesDone++;
        _copied++;
        ReportProgress(finalDst);

        CopyAttributesIfNeeded(src, finalDst);

        if (IsMove) await _fs.DeleteAsync(src, false, ct).ConfigureAwait(false);
    }

    private void CopyAttributesIfNeeded(string src, string dst)
    {
        try
        {
            if (_options.CopyAttributes || _options.CopyTimestamps)
            {
                var srcInfo = new FileInfo(src);
                var dstInfo = new FileInfo(dst);

                if (_options.CopyAttributes)
                {
                    // Копируем атрибуты, но НЕ ReadOnly (иначе файл будет недоступен для записи).
                    // Copy attributes, but NOT ReadOnly (otherwise file will be write-protected).
                    var attrs = srcInfo.Attributes;
                    attrs &= ~FileAttributes.ReadOnly; // Снимаем ReadOnly / Remove ReadOnly
                    dstInfo.Attributes = attrs;
                }

                if (_options.CopyTimestamps)
                {
                    dstInfo.CreationTimeUtc = srcInfo.CreationTimeUtc;
                    dstInfo.LastWriteTimeUtc = srcInfo.LastWriteTimeUtc;
                    dstInfo.LastAccessTimeUtc = srcInfo.LastAccessTimeUtc;
                }
            }

            if (_options.CopyNtfsPermissions && _fs is LocalFileSystem)
            {
                LocalFileSystem.CopyAcl(src, dst);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Failed to copy attributes/timestamps for {dst}: {ex.Message}", nameof(TransferOperation));
        }
    }

    private OverwritePolicy ResolvePolicy(string src, string dst)
    {
        // Если уже есть кэшированное решение Ask (ApplyToAll), используем его.
        // If there's a cached Ask decision (ApplyToAll), use it.
        if (_cachedAskPolicy.HasValue) return _cachedAskPolicy.Value;

        // Проверяем, существует ли файл назначения. Если нет — пропускаем диалог.
        // Check if destination file exists. If not — skip dialog.
        bool exists = File.Exists(dst) || Directory.Exists(dst);
        LogService.Info($"ResolvePolicy: src={src}, dst={dst}, exists={exists}, policy={_policy}", nameof(TransferOperation));

        if (!exists)
        {
            return _policy == OverwritePolicy.Ask ? OverwritePolicy.Overwrite : _policy;
        }

        var policy = _onConflict?.Invoke(src, dst) ?? _policy;

        // Если политика Ask и нет колбэка — пропускаем (безопасное поведение по умолчанию).
        // If policy is Ask and no callback — skip (safe default behaviour).
        if (policy == OverwritePolicy.Ask && _onConflict is null)
        {
            _skipped++;
            _filesDone++;
            ReportProgress(dst);
            return OverwritePolicy.Skip;
        }

        return policy;
    }

    /// <summary>
    /// Устанавливает кэшированную политику Ask (вызывается из колбэка при «применить ко всем»).
    /// Sets the cached Ask policy (called from callback when "apply to all").
    /// </summary>
    internal void SetCachedAskPolicy(OverwritePolicy policy) => _cachedAskPolicy = policy;

    private static bool IsSystemFolder(string name)
        => string.Equals(name, "$Recycle.Bin", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "System Volume Information", StringComparison.OrdinalIgnoreCase);

    private bool SameVolume(string a, string b)
        => _fs is LocalFileSystem lfs
           && string.Equals(lfs.GetVolumeRoot(a), lfs.GetVolumeRoot(b), StringComparison.OrdinalIgnoreCase);

    private void ReportProgress(string currentFile)
        => Report(new OperationProgress(currentFile, 0, 0, _doneBytes, _totalBytes, _filesDone, _totalFiles));

    private static async Task<(long Bytes, int Files)> ScanAsync(string path, CancellationToken ct)
    {
        if (Directory.Exists(path))
        {
            long bytes = 0;
            int files = 1;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    bytes += new FileInfo(f).Length;
                }
                catch (FileNotFoundException) { }
                files++;
            }
            foreach (var d in Directory.EnumerateDirectories(path))
            {
                ct.ThrowIfCancellationRequested();
                var dirName = Path.GetFileName(d);
                if (IsSystemFolder(dirName)) continue;
                var sub = await ScanAsync(d, ct).ConfigureAwait(false);
                bytes += sub.Bytes;
                files += sub.Files;
            }
            return (bytes, files);
        }
        if (File.Exists(path)) { var fi = new FileInfo(path); return (fi.Length, 1); }
        return (0, 0);
    }

    private static bool IsSubPathOf(string candidate, string parent)
    {
        var p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Пытается удалить неполный файл назначения после отмены/ошибки копирования.
    /// Tries to delete partial destination file after cancel/copy error.
    /// </summary>
    private static void TryDeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Failed to delete partial file {path}: {ex.Message}", nameof(TransferOperation));
        }
    }
}
