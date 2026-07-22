using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Операция извлечения архива (ph5.1, exp.yml). Потоковое извлечение через SharpCompress ArchiveFactory.
/// Operation for extracting an archive (ph5.1). Streaming extraction via SharpCompress ArchiveFactory.
/// SharpCompress 0.50: ArchiveFactory.OpenArchive(filePath) + entry.WriteToDirectory().
/// Supported: all SharpCompress formats (ZIP, 7Z, RAR, TAR, GZ, BZ2, XZ, LZ).
/// Прогресс: IProgress&lt;OperationProgress&gt;, CancellationToken.
/// Опции: перезапись / пропуск, извлечение всех или выбранных записей.
/// Options: overwrite / skip, extract all or selected entries.
/// </summary>
public sealed class ArchiveExtractOperation : FileOperation
{
    private readonly string _archivePath;
    private readonly string _outputDirectory;
    private readonly IReadOnlyList<string>? _selectedEntries;
    private readonly bool _overwrite;
    private readonly string? _password;

    /// <summary>
    /// Конструктор операции извлечения. / Creates archive extraction operation.
    /// </summary>
    /// <param name="archivePath">Путь к архиву. / Path to the archive.</param>
    /// <param name="outputDirectory">Каталог назначения. / Destination directory.</param>
    /// <param name="selectedEntries">Список относительных путей записей для извлечения (null = все). / Selected entry relative paths (null = all).</param>
    /// <param name="overwrite">Перезаписывать существующие файлы. / Overwrite existing files.</param>
    /// <param name="password">Пароль для расшифровки архива (опционально). / Password for archive decryption (optional).</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    public ArchiveExtractOperation(
        string archivePath,
        string outputDirectory,
        IReadOnlyList<string>? selectedEntries = null,
        bool overwrite = true,
        string? password = null,
        IProgress<OperationProgress>? progress = null)
        : base(progress)
    {
        _archivePath = archivePath ?? throw new ArgumentNullException(nameof(archivePath));
        _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        _selectedEntries = selectedEntries;
        _overwrite = overwrite;
        _password = password;
    }

    /// <summary>
    /// Основная логика извлечения: перечисление записей, извлечение через SharpCompress.
    /// Core extraction logic: entry enumeration, extraction via SharpCompress.
    /// SharpCompress 0.50: ArchiveFactory.OpenArchive(filePath) + entry.WriteToDirectory().
    /// Потоковое извлечение без полной загрузки архива в память.
    /// Streaming extraction without full archive in-memory loading.
    /// </summary>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        // Убеждаемся, что директория назначения существует.
        // Ensure destination directory exists.
        if (!Directory.Exists(_outputDirectory))
            Directory.CreateDirectory(_outputDirectory);

        var extractionOptions = new ExtractionOptions
        {
            ExtractFullPath = true,
            Overwrite = _overwrite
        };

        // Создаём HashSet для быстрой проверки выбранных записей.
        // Create HashSet for fast selected-entries lookup.
        HashSet<string>? selectedSet = null;
        if (_selectedEntries is { Count: > 0 })
        {
            selectedSet = new HashSet<string>(
                _selectedEntries.Select(e => e.Replace('\\', '/')),
                StringComparer.OrdinalIgnoreCase);
        }

        // SharpCompress 0.50: ArchiveFactory.OpenArchive(filePath).
        // SharpCompress 0.50: ArchiveFactory.OpenArchive(filePath).
        var readerOptions = new ReaderOptions();
        if (!string.IsNullOrEmpty(_password))
            readerOptions.Password = _password;
        using var archive = ArchiveFactory.OpenArchive(_archivePath, readerOptions);

        // Собираем записи для извлечения. / Collect entries for extraction.
        var entries = archive.Entries
            .Where(e => !e.IsDirectory)
            .ToList();

        int filesTotal = selectedSet is not null
            ? entries.Count(e => selectedSet.Contains(e.Key ?? string.Empty))
            : entries.Count;

        if (filesTotal == 0)
        {
            filesTotal = entries.Count; // Извлекаем все, если фильтр не совпал / Extract all if filter didn't match
            selectedSet = null;
        }

        int filesDone = 0;
        long totalBytes = entries
            .Where(e => selectedSet is null || selectedSet.Contains(e.Key ?? string.Empty))
            .Sum(e => (long)e.Size);
        long doneBytes = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var entryKey = entry.Key ?? string.Empty;

            // Если выбраны конкретные записи — пропускаем остальные.
            // If specific entries selected — skip others.
            if (selectedSet is not null && !selectedSet.Contains(entryKey))
                continue;

            try
            {
                // Проверяем существование файла при политике Skip.
                // Check file existence for Skip policy.
                var destPath = Path.Combine(_outputDirectory, entryKey.Replace('/', '\\'));

                if (!_overwrite && File.Exists(destPath))
                {
                    filesDone++;
                    doneBytes += (long)entry.Size;
                    continue;
                }

                // Обеспечиваем существование родительского каталога.
                // Ensure parent directory exists.
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                // Извлекаем запись. / Extract entry.
                entry.WriteToDirectory(_outputDirectory, extractionOptions);

                doneBytes += (long)entry.Size;
            }
            catch (Exception ex)
            {
                LogService.Warn($"Archive extract failed: {entryKey}: {ex.Message}",
                    nameof(ArchiveExtractOperation));
            }

            filesDone++;

            Report(new OperationProgress(
                currentFile: entryKey,
                bytesDone: doneBytes,
                bytesTotal: totalBytes,
                totalBytesDone: doneBytes,
                totalBytes: totalBytes,
                filesDone: filesDone,
                filesTotal: filesTotal));
        }

        // Финальный прогресс 100%. / Final progress 100%.
        Report(new OperationProgress(
            currentFile: _archivePath,
            bytesDone: totalBytes,
            bytesTotal: totalBytes,
            totalBytesDone: totalBytes,
            totalBytes: totalBytes,
            filesDone: filesTotal,
            filesTotal: filesTotal));
    }
}
