using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Common.Options;
using SharpCompress.Providers.Default;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Writers.Tar;
using SharpCompress.Writers.Zip;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Операция создания архива (ph5.1, exp.yml).
/// Operation for creating an archive (ph5.1).
/// Поддерживаемые форматы / Supported formats:
///   ZIP, 7Z, TAR, TAR.GZ, TAR.BZ2, GZIP (single file), BZIP2 (single file).
/// Прогресс: IProgress&lt;OperationProgress&gt;, CancellationToken.
/// Options: уровень сжатия (None/BestSpeed/BestCompression).
/// </summary>
public sealed class ArchiveCreateOperation : FileOperation
{
    private readonly IReadOnlyList<string> _sourceFiles;
    private readonly string _outputPath;
    private readonly string _baseDirectory;
    private readonly CompressionLevel _compressionLevel;
    private readonly ArchiveFormat _format;
    private readonly string? _password;

    /// <summary>
    /// Формат архива. / Archive format.
    /// </summary>
    public enum ArchiveFormat
    {
        /// <summary>ZIP-архив. / ZIP archive.</summary>
        Zip,

        /// <summary>7Z-архив (LZMA). / 7Z archive (LZMA).</summary>
        SevenZip,

        /// <summary>TAR (без сжатия). / TAR (uncompressed).</summary>
        Tar,

        /// <summary>TAR + GZIP. / TAR + GZIP.</summary>
        TarGz,

        /// <summary>TAR + BZIP2. / TAR + BZIP2.</summary>
        TarBz2,

        /// <summary>GZIP (один файл). / GZIP (single file).</summary>
        GZip,

        /// <summary>BZIP2 (один файл). / BZIP2 (single file).</summary>
        BZip2
    }

    /// <summary>
    /// Уровень сжатия. / Compression level.
    /// </summary>
    public enum CompressionLevel
    {
        /// <summary>Без сжатия (Store). / No compression (Store).</summary>
        None,

        /// <summary>Максимальная скорость (Fastest). / Maximum speed (Fastest).</summary>
        BestSpeed,

        /// <summary>Оптимальный (по умолчанию). / Optimal (default).</summary>
        Optimal,

        /// <summary>Максимальное сжатие (Best). / Maximum compression (Best).</summary>
        BestCompression
    }

    /// <summary>
    /// Конструктор операции создания архива. / Creates archive creation operation.
    /// </summary>
    /// <param name="sourceFiles">Список полных путей файлов для архивации. / List of full file paths to archive.</param>
    /// <param name="outputPath">Путь к создаваемому архиву. / Path to the archive to create.</param>
    /// <param name="baseDirectory">Базовый каталог для вычисления относительных путей. / Base directory for relative paths.</param>
    /// <param name="format">Формат архива. / Archive format.</param>
    /// <param name="compressionLevel">Уровень сжатия. / Compression level.</param>
    /// <param name="password">Пароль для шифрования архива (опционально). / Password for archive encryption (optional).</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    public ArchiveCreateOperation(
        IReadOnlyList<string> sourceFiles,
        string outputPath,
        string baseDirectory,
        ArchiveFormat format = ArchiveFormat.Zip,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        string? password = null,
        IProgress<OperationProgress>? progress = null)
        : base(progress)
    {
        _sourceFiles = sourceFiles ?? throw new ArgumentNullException(nameof(sourceFiles));
        _outputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        _format = format;
        _compressionLevel = compressionLevel;
        _password = password;
    }

    /// <summary>
    /// Основная логика создания архива.
    /// Core archive creation logic.
    /// </summary>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        // Подсчёт общего объёма. / Compute total size.
        int filesTotal = _sourceFiles.Count;
        long totalBytes = 0;
        foreach (var file in _sourceFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var fi = new FileInfo(file);
                if (fi.Exists)
                    totalBytes += fi.Length;
            }
            catch { /* пропускаем недоступные файлы / skip inaccessible files */ }
        }

        // Убеждаемся, что директория назначения существует.
        // Ensure destination directory exists.
        var outputDir = Path.GetDirectoryName(_outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Для GZIP/BZIP2 (один файл) — специальная логика.
        // For GZIP/BZIP2 (single file) — special logic.
        if (_format == ArchiveFormat.GZip || _format == ArchiveFormat.BZip2)
        {
            await CompressSingleFileAsync(ct, totalBytes, filesTotal).ConfigureAwait(false);
            return;
        }

        // Для TAR.GZ / TAR.BZ2 — пишем TAR внутрь потока сжатия.
        // For TAR.GZ / TAR.BZ2 — write TAR inside compression stream.
        if (_format == ArchiveFormat.TarGz || _format == ArchiveFormat.TarBz2)
        {
            await CreateTarCompressedAsync(ct, totalBytes, filesTotal).ConfigureAwait(false);
            return;
        }

        // Форматы с WriterFactory: ZIP, TAR, 7Z.
        // Formats with WriterFactory: ZIP, TAR, 7Z.
        await CreateWithWriterAsync(ct, totalBytes, filesTotal).ConfigureAwait(false);
    }

    /// <summary>
    /// Создание архива через WriterFactory (ZIP, TAR, 7Z).
    /// Create archive via WriterFactory (ZIP, TAR, 7Z).
    /// </summary>
    private async Task CreateWithWriterAsync(CancellationToken ct, long totalBytes, int filesTotal)
    {
        var (archiveType, writerOptions) = _format switch
        {
            ArchiveFormat.Tar => (ArchiveType.Tar, (IWriterOptions)new TarWriterOptions(CompressionType.None, finalizeArchiveOnClose: true)),
            ArchiveFormat.SevenZip => (ArchiveType.SevenZip, (IWriterOptions)CreateSevenZipOptions()),
            _ => (ArchiveType.Zip, (IWriterOptions)CreateZipOptions())
        };

        int filesDone = 0;
        long doneBytes = 0;

        using (var archive = WriterFactory.OpenWriter(_outputPath, archiveType, writerOptions))
        {
            foreach (var file in _sourceFiles)
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(file)) continue;

                string entryPath = ComputeEntryPath(file);

                try
                {
                    var fi = new FileInfo(file);
                    long fileLen = fi.Exists ? fi.Length : 0;

                    using var fileStream = new FileStream(
                        file, FileMode.Open, FileAccess.Read, FileShare.Read,
                        bufferSize: 1024 * 1024, useAsync: true);

                    archive.Write(entryPath, fileStream, fi.LastWriteTimeUtc);
                    doneBytes += fileLen;
                }
                catch (Exception ex)
                {
                    LogService.Warn($"Archive write failed [{_format}]: {file}: {ex.Message}",
                        nameof(ArchiveCreateOperation));
                }

                filesDone++;
                ReportProgress(entryPath, doneBytes, totalBytes, filesDone, filesTotal);
            }
        }

        ReportFinal(_outputPath, totalBytes, filesTotal);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Создание TAR.GZ / TAR.BZ2: TAR внутри потока сжатия.
    /// Create TAR.GZ / TAR.BZ2: TAR inside compression stream.
    /// </summary>
    private async Task CreateTarCompressedAsync(CancellationToken ct, long totalBytes, int filesTotal)
    {
        int filesDone = 0;
        long doneBytes = 0;

        var tarOptions = new TarWriterOptions(CompressionType.None, finalizeArchiveOnClose: true);

        // Внешний FileStream → поток сжатия → TAR Writer.
        // Outer FileStream → compression stream → TAR Writer.
        using var fs = new FileStream(_outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 1024 * 1024, useAsync: true);

        int bz2Level = MapToIntCompressionLevel();

        Stream compressedStream = _format == ArchiveFormat.TarGz
            ? CreateGZipStream(fs, leaveOpen: false)
            : new BZip2CompressionProvider().CreateCompressStream(fs, bz2Level);

        using (compressedStream)
        using (var archive = WriterFactory.OpenWriter(compressedStream, ArchiveType.Tar, tarOptions))
        {
            foreach (var file in _sourceFiles)
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(file)) continue;

                string entryPath = ComputeEntryPath(file);

                try
                {
                    var fi = new FileInfo(file);
                    long fileLen = fi.Exists ? fi.Length : 0;

                    using var fileStream = new FileStream(
                        file, FileMode.Open, FileAccess.Read, FileShare.Read,
                        bufferSize: 1024 * 1024, useAsync: true);

                    archive.Write(entryPath, fileStream, fi.LastWriteTimeUtc);
                    doneBytes += fileLen;
                }
                catch (Exception ex)
                {
                    LogService.Warn($"TAR compressed write failed: {file}: {ex.Message}",
                        nameof(ArchiveCreateOperation));
                }

                filesDone++;
                ReportProgress(entryPath, doneBytes, totalBytes, filesDone, filesTotal);
            }
        }

        ReportFinal(_outputPath, totalBytes, filesTotal);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Сжатие одного файла в GZIP или BZIP2.
    /// Compress a single file to GZIP or BZIP2.
    /// </summary>
    private async Task CompressSingleFileAsync(CancellationToken ct, long totalBytes, int filesTotal)
    {
        if (_sourceFiles.Count == 0)
        {
            ReportFinal(_outputPath, 0, 0);
            return;
        }

        // Для GZIP/BZIP2 берём только первый файл.
        // For GZIP/BZIP2 use only the first file.
        var sourceFile = _sourceFiles[0];
        if (!File.Exists(sourceFile))
        {
            throw new FileNotFoundException("Source file not found for compression.", sourceFile);
        }

        var fi = new FileInfo(sourceFile);
        long fileLen = fi.Length;

        using var inputStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 1024 * 1024, useAsync: true);
        using var outputStream = new FileStream(_outputPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 1024 * 1024, useAsync: true);

        if (_format == ArchiveFormat.GZip)
        {
            using var gzStream = CreateGZipStream(outputStream, leaveOpen: true);
            await CopyStreamAsync(inputStream, gzStream, ct).ConfigureAwait(false);
        }
        else // BZip2
        {
            int bz2Level = MapToIntCompressionLevel();
            using var bz2Stream = new BZip2CompressionProvider().CreateCompressStream(outputStream, bz2Level);
            await CopyStreamAsync(inputStream, bz2Stream, ct).ConfigureAwait(false);
        }

        ReportFinal(_outputPath, fileLen, 1);
    }

    /// <summary>
    /// Создаёт параметры ZIP-записи. / Creates ZIP writer options.
    /// </summary>
    private ZipWriterOptions CreateZipOptions()
    {
        var compressionType = _compressionLevel == CompressionLevel.None
            ? CompressionType.None
            : CompressionType.Deflate;

        int deflateLevel = _compressionLevel switch
        {
            CompressionLevel.None => 0,
            CompressionLevel.BestSpeed => 1,
            CompressionLevel.Optimal => 6,
            CompressionLevel.BestCompression => 9,
            _ => 6
        };

        return new ZipWriterOptions(compressionType)
        {
            UseZip64 = true,
            CompressionLevel = deflateLevel
        };
    }

    /// <summary>
    /// Создаёт параметры 7Z-записи. / Creates 7Z writer options.
    /// </summary>
    private static SevenZipWriterOptions CreateSevenZipOptions()
    {
        return new SevenZipWriterOptions(CompressionType.LZMA)
        {
            CompressHeader = true
        };
    }

    /// <summary>
    /// Создаёт GZipStream с учётом уровня сжатия.
    /// Creates GZipStream with compression level.
    /// </summary>
    private GZipStream CreateGZipStream(Stream stream, bool leaveOpen)
    {
        var gzLevel = _compressionLevel switch
        {
            CompressionLevel.None => System.IO.Compression.CompressionLevel.NoCompression,
            CompressionLevel.BestSpeed => System.IO.Compression.CompressionLevel.Fastest,
            CompressionLevel.BestCompression => System.IO.Compression.CompressionLevel.SmallestSize,
            _ => System.IO.Compression.CompressionLevel.Optimal
        };
        return new GZipStream(stream, gzLevel, leaveOpen);
    }

    /// <summary>
    /// Преобразует уровень сжатия в числовое значение для BZip2.
    /// Maps compression level to numeric value for BZip2.
    /// </summary>
    private int MapToIntCompressionLevel()
    {
        return _compressionLevel switch
        {
            CompressionLevel.None => 1,
            CompressionLevel.BestSpeed => 1,
            CompressionLevel.Optimal => 5,
            CompressionLevel.BestCompression => 9,
            _ => 5
        };
    }

    /// <summary>
    /// Копирует поток. / Copies stream.
    /// </summary>
    private static async Task CopyStreamAsync(Stream source, Stream dest, CancellationToken ct)
    {
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            await dest.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Вычисляет относительный путь записи внутри архива.
    /// Computes relative entry path inside archive.
    /// </summary>
    private string ComputeEntryPath(string file)
    {
        string entryPath;
        try
        {
            entryPath = Path.GetRelativePath(_baseDirectory, file);
        }
        catch
        {
            entryPath = Path.GetFileName(file);
        }

        return entryPath.Replace('\\', '/');
    }

    private void ReportProgress(string currentFile, long doneBytes, long totalBytes, int filesDone, int filesTotal)
    {
        Report(new OperationProgress(
            currentFile: currentFile,
            bytesDone: doneBytes,
            bytesTotal: totalBytes,
            totalBytesDone: doneBytes,
            totalBytes: totalBytes,
            filesDone: filesDone,
            filesTotal: filesTotal));
    }

    private void ReportFinal(string currentFile, long totalBytes, int filesTotal)
    {
        Report(new OperationProgress(
            currentFile: currentFile,
            bytesDone: totalBytes,
            bytesTotal: totalBytes,
            totalBytesDone: totalBytes,
            totalBytes: totalBytes,
            filesDone: filesTotal,
            filesTotal: filesTotal));
    }
}
