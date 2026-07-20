using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Операция копирования/переноса между разными IFileSystem (Local ↔ SFTP).
/// Copy/move operation between different IFileSystem instances (Local ↔ SFTP).
/// Работает через промежуточный временный файл при необходимости (SFTP↔SFTP),
/// или напрямую upload/download для Local↔SFTP.
/// Works via an intermediate temp file when needed (SFTP↔SFTP),
/// or directly via upload/download for Local↔SFTP.
/// Поддерживает рекурсивное копирование каталогов, прогресс и отмену.
/// Supports recursive directory copy, progress reporting, and cancellation.
/// </summary>
public class CrossVfsCopyOperation : FileOperation
{
    private readonly IFileSystem _sourceFs;
    private readonly IFileSystem _destFs;
    private readonly List<string> _sources;
    private readonly string _destDir;
    private readonly bool _isMove;
    private readonly OverwritePolicy _policy;

    private long _totalBytes;
    private long _doneBytes;
    private int _totalFiles;
    private int _filesDone;
    private long _copied;
    private long _skipped;
    private long _failed;
    private readonly List<string> _errors = new();

    /// <summary>Количество скопированных/перенесённых файлов. / Number of copied/moved files.</summary>
    public long Copied => _copied;

    /// <summary>Количество пропущенных файлов. / Number of skipped files.</summary>
    public long Skipped => _skipped;

    /// <summary>Количество неудач. / Number of failures.</summary>
    public long Failed => _failed;

    /// <summary>Список ошибок. / Error list.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Создаёт операцию кросс-VFS копирования/переноса.
    /// Creates a cross-VFS copy/move operation.
    /// </summary>
    /// <param name="sourceFs">Источник файловой системы. / Source file system.</param>
    /// <param name="destFs">Целевая файловая система. / Destination file system.</param>
    /// <param name="sources">Список исходных путей. / List of source paths.</param>
    /// <param name="destDir">Целевая директория (на destFs). / Destination directory (on destFs).</param>
    /// <param name="isMove">True для переноса, false для копирования. / True for move, false for copy.</param>
    /// <param name="policy">Политика перезаписи. / Overwrite policy.</param>
    /// <param name="progress">Провайдер прогресса. / Progress provider.</param>
    public CrossVfsCopyOperation(
        IFileSystem sourceFs,
        IFileSystem destFs,
        IEnumerable<string> sources,
        string destDir,
        bool isMove,
        OverwritePolicy policy,
        IProgress<OperationProgress>? progress = null)
        : base(progress)
    {
        _sourceFs = sourceFs ?? throw new ArgumentNullException(nameof(sourceFs));
        _destFs = destFs ?? throw new ArgumentNullException(nameof(destFs));
        _sources = new List<string>(sources ?? throw new ArgumentNullException(nameof(sources)));
        _destDir = destDir ?? throw new ArgumentNullException(nameof(destDir));
        _isMove = isMove;
        _policy = policy;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        // Предпросмотр объёмов для корректного прогресса / Pre-scan sizes for accurate progress.
        foreach (var src in _sources)
        {
            ct.ThrowIfCancellationRequested();
            var info = await _sourceFs.GetFileInfoAsync(src, ct).ConfigureAwait(false);
            if (info is null) continue;
            if (info.IsDirectory)
            {
                var (bytes, files) = await ScanSourceAsync(src, ct).ConfigureAwait(false);
                _totalBytes += bytes;
                _totalFiles += files;
            }
            else
            {
                _totalBytes += info.Size;
                _totalFiles++;
            }
        }

        // Обработка каждого исходного элемента / Process each source element.
        foreach (var src in _sources)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(src.TrimEnd('/', '\\'));
            var dst = NormalizeDestPath(_destDir, name);
            try
            {
                await TransferAsync(src, dst, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failed++;
                _errors.Add($"{src}: {ex.Message}");
                LogService.Error($"Cross-VFS transfer failed for {src}: {ex.Message}", nameof(CrossVfsCopyOperation), ex);
            }
        }
    }

    /// <summary>
    /// Передаёт один элемент (файл или каталог) из sourceFs в destFs.
    /// Transfers a single element (file or directory) from sourceFs to destFs.
    /// </summary>
    private async Task TransferAsync(string srcPath, string dstPath, CancellationToken ct)
    {
        var info = await _sourceFs.GetFileInfoAsync(srcPath, ct).ConfigureAwait(false);
        if (info is null) throw new FileNotFoundException($"Source not found: {srcPath}");

        if (info.IsDirectory)
        {
            await TransferDirectoryAsync(srcPath, dstPath, ct).ConfigureAwait(false);
        }
        else
        {
            await TransferFileAsync(srcPath, dstPath, info.Size, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Рекурсивно передаёт каталог из sourceFs в destFs.
    /// Recursively transfers a directory from sourceFs to destFs.
    /// </summary>
    private async Task TransferDirectoryAsync(string srcPath, string dstPath, CancellationToken ct)
    {
        await _destFs.CreateDirectoryAsync(dstPath, ct).ConfigureAwait(false);
        _filesDone++;
        _copied++;
        ReportProgress(srcPath);

        var entries = await _sourceFs.EnumerateAsync(srcPath, includeHidden: true, ct).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var childDst = NormalizeDestPath(dstPath, entry.Name);
            if (entry.IsDirectory)
            {
                await TransferDirectoryAsync(entry.FullPath, childDst, ct).ConfigureAwait(false);
            }
            else
            {
                await TransferFileAsync(entry.FullPath, childDst, entry.Size, ct).ConfigureAwait(false);
            }
        }

        if (_isMove)
        {
            await _sourceFs.DeleteAsync(srcPath, recursive: false, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Передаёт файл из sourceFs в destFs через промежуточный локальный поток.
    /// Transfers a file from sourceFs to destFs via an intermediate local stream.
    /// </summary>
    private async Task TransferFileAsync(string srcPath, string dstPath, long fileSize, CancellationToken ct)
    {
        // Проверка существования назначения и политика перезаписи / Check destination existence and overwrite policy.
        var dstExists = await _destFs.ExistsAsync(dstPath, ct).ConfigureAwait(false);
        if (dstExists)
        {
            var effectivePolicy = _policy;
            if (effectivePolicy == OverwritePolicy.Skip)
            {
                _skipped++;
                _filesDone++;
                _doneBytes += fileSize;
                ReportProgress(srcPath);
                return;
            }
            if (effectivePolicy == OverwritePolicy.OverwriteOlder || effectivePolicy == OverwritePolicy.OverwriteSmaller)
            {
                var dstInfo = await _destFs.GetFileInfoAsync(dstPath, ct).ConfigureAwait(false);
                if (dstInfo is not null)
                {
                    bool overwrite;
                    if (effectivePolicy == OverwritePolicy.OverwriteOlder)
                    {
                        var srcInfo = await _sourceFs.GetFileInfoAsync(srcPath, ct).ConfigureAwait(false);
                        if (srcInfo is null)
                        {
                            _skipped++;
                            _filesDone++;
                            _doneBytes += fileSize;
                            ReportProgress(srcPath);
                            return;
                        }
                        overwrite = srcInfo.LastWriteTimeUtc > dstInfo.LastWriteTimeUtc;
                    }
                    else
                    {
                        overwrite = fileSize < dstInfo.Size;
                    }
                    if (!overwrite)
                    {
                        _skipped++;
                        _filesDone++;
                        _doneBytes += fileSize;
                        ReportProgress(srcPath);
                        return;
                    }
                }
            }
        }

        // Определяем стратегию передачи / Determine transfer strategy.
        // Создаём провайдер прогресса для этого файла / Create per-file progress provider.
        long prevBytes = 0;
        var fileProgress = new Progress<long>(bytesTransferred =>
        {
            _doneBytes += bytesTransferred - prevBytes;
            prevBytes = bytesTransferred;
            Report(new OperationProgress(srcPath, bytesTransferred, fileSize, _doneBytes, _totalBytes, _filesDone, _totalFiles));
        });

        if (_sourceFs is LocalFileSystem && _destFs is SftpFileSystem dstSftp)
        {
            // Local → SFTP: прямая загрузка / Direct upload.
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(dstPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir))
                await dstSftp.CreateDirectoryAsync(dir, ct).ConfigureAwait(false);
            await dstSftp.UploadFileAsync(srcPath, dstPath, fileProgress, ct).ConfigureAwait(false);
        }
        else if (_sourceFs is SftpFileSystem srcSftp && _destFs is LocalFileSystem)
        {
            // SFTP → Local: прямое скачивание / Direct download.
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(dstPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await srcSftp.DownloadFileAsync(srcPath, dstPath, fileProgress, ct).ConfigureAwait(false);
        }
        else if (_sourceFs is SftpFileSystem srcSftp2 && _destFs is SftpFileSystem dstSftp2)
        {
            // SFTP → SFTP: через временный локальный файл / Via temporary local file.
            var tmpPath = Path.Combine(Path.GetTempPath(), $"xvfs_{Guid.NewGuid():N}");
            try
            {
                await srcSftp2.DownloadFileAsync(srcPath, tmpPath, null, ct).ConfigureAwait(false);
                await dstSftp2.UploadFileAsync(tmpPath, dstPath, fileProgress, ct).ConfigureAwait(false);
            }
            finally
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
                catch (Exception ex) { LogService.Warn($"Failed to delete temp file '{tmpPath}': {ex.Message}", nameof(CrossVfsCopyOperation)); }
            }
        }
        else if (_sourceFs is LocalFileSystem && IsCloudFileSystem(_destFs))
        {
            // Local → Cloud: прямая загрузка / Direct upload to cloud.
            ct.ThrowIfCancellationRequested();
            await CloudUploadAsync(_destFs, srcPath, dstPath, fileProgress, ct).ConfigureAwait(false);
        }
        else if (IsCloudFileSystem(_sourceFs) && _destFs is LocalFileSystem)
        {
            // Cloud → Local: прямое скачивание / Direct download from cloud.
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(dstPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await CloudDownloadAsync(_sourceFs, srcPath, dstPath, fileProgress, ct).ConfigureAwait(false);
        }
        else if (IsCloudFileSystem(_sourceFs) && IsCloudFileSystem(_destFs))
        {
            // Cloud → Cloud: через временный локальный файл / Via temporary local file.
            var tmpPath = Path.Combine(Path.GetTempPath(), $"xvfs_{Guid.NewGuid():N}");
            try
            {
                await CloudDownloadAsync(_sourceFs, srcPath, tmpPath, null, ct).ConfigureAwait(false);
                await CloudUploadAsync(_destFs, tmpPath, dstPath, fileProgress, ct).ConfigureAwait(false);
            }
            finally
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
                catch (Exception ex) { LogService.Warn($"Failed to delete temp file '{tmpPath}': {ex.Message}", nameof(CrossVfsCopyOperation)); }
            }
        }
        else
        {
            // Local → Local (или другой тип): generic fallback через copy / Generic fallback.
            ct.ThrowIfCancellationRequested();
            var dir = Path.GetDirectoryName(dstPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await _sourceFs.CopyAsync(srcPath, dstPath, overwrite: true, ct).ConfigureAwait(false);
        }

        _doneBytes += fileSize - prevBytes; // учёт остатка / account for remainder
        _filesDone++;
        _copied++;
        ReportProgress(srcPath);

        // Перенос: удаляем источник / Move: delete source.
        if (_isMove)
        {
            await _sourceFs.DeleteAsync(srcPath, false, ct).ConfigureAwait(false);
        }
    }

    private void ReportProgress(string currentFile)
        => Report(new OperationProgress(currentFile, 0, 0, _doneBytes, _totalBytes, _filesDone, _totalFiles));

    /// <summary>
    /// Проверяет, является ли файловая система облачной.
    /// Checks whether a file system is a cloud file system.
    /// </summary>
    private static bool IsCloudFileSystem(IFileSystem fs) => fs is CloudFileSystem;

    /// <summary>
    /// Скачивает файл из облачной файловой системы.
    /// Downloads a file from a cloud file system.
    /// </summary>
    private static async Task CloudDownloadAsync(IFileSystem fs, string remotePath, string localPath, IProgress<long>? progress, CancellationToken ct)
    {
        switch (fs)
        {
            case S3FileSystem s3: await s3.DownloadFileAsync(remotePath, localPath, progress, ct); break;
            case AzureBlobFileSystem az: await az.DownloadFileAsync(remotePath, localPath, progress, ct); break;
            case YandexDiskFileSystem yd: await yd.DownloadFileAsync(remotePath, localPath, progress, ct); break;
            case NextCloudFileSystem nc: await nc.DownloadFileAsync(remotePath, localPath, progress, ct); break;
            default: throw new NotSupportedException($"Download not supported for {fs.Name}");
        }
    }

    /// <summary>
    /// Загружает файл в облачную файловую систему.
    /// Uploads a file to a cloud file system.
    /// </summary>
    private static async Task CloudUploadAsync(IFileSystem fs, string localPath, string remotePath, IProgress<long>? progress, CancellationToken ct)
    {
        switch (fs)
        {
            case S3FileSystem s3: await s3.UploadFileAsync(localPath, remotePath, progress, ct); break;
            case AzureBlobFileSystem az: await az.UploadFileAsync(localPath, remotePath, progress, ct); break;
            case YandexDiskFileSystem yd: await yd.UploadFileAsync(localPath, remotePath, progress, ct); break;
            case NextCloudFileSystem nc: await nc.UploadFileAsync(localPath, remotePath, progress, ct); break;
            default: throw new NotSupportedException($"Upload not supported for {fs.Name}");
        }
    }

    /// <summary>
    /// Рекурсивно подсчитывает объём и число файлов в каталоге источника.
    /// Recursively counts the size and number of files in the source directory.
    /// </summary>
    private async Task<(long Bytes, int Files)> ScanSourceAsync(string path, CancellationToken ct)
    {
        long bytes = 0;
        int files = 1; // сам каталог / the directory itself
        var entries = await _sourceFs.EnumerateAsync(path, includeHidden: true, ct).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                var sub = await ScanSourceAsync(entry.FullPath, ct).ConfigureAwait(false);
                bytes += sub.Bytes;
                files += sub.Files;
            }
            else
            {
                bytes += entry.Size;
                files++;
            }
        }
        return (bytes, files);
    }

    private static string NormalizeDestPath(string dir, string name)
    {
        var baseDir = dir.TrimEnd('/', '\\');
        // Определяем разделитель по типу пути назначения / Detect separator from dest path style.
        if (baseDir.Contains('/')) return baseDir + "/" + name;
        return baseDir + "\\" + name;
    }
}
