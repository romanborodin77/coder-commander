using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Providers;

/// <summary>
/// Провайдер содержимого для архивов (ph4.3 / exp.yml).
/// Content provider for archives (ph4.3).
/// Использует <see cref="ArchiveHelper"/> (SharpCompress) для открытия файлов
/// внутри архивов через streaming без извлечения на диск.
/// Uses ArchiveHelper (SharpCompress) to open files inside archives
/// via streaming without extraction to disk.
///
/// Путь к элементу внутри архива формата: "path/to/archive.zip!/entry/path".
/// Path to an entry inside an archive: "path/to/archive.zip!/entry/path".
///
/// Поддерживаемые форматы: ZIP, 7Z, RAR, TAR, GZ, BZ2, XZ, LZ.
/// Supported formats: ZIP, 7Z, RAR, TAR, GZ, BZ2, XZ, LZ.
/// </summary>
public sealed class ArchiveContentProvider : IContentProvider
{
    /// <summary>Разделитель пути архива и записи внутри. / Archive and entry path separator.</summary>
    public const char ArchiveSeparator = '!';

    /// <inheritdoc/>
    public string Name => "Archive";

    /// <inheritdoc/>
    public bool CanHandle(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        // Путь формата "archive.zip!/entry/path"
        // Path in format "archive.zip!/entry/path"
        var sepIndex = path.IndexOf($"{ArchiveSeparator}/", StringComparison.Ordinal);
        if (sepIndex < 0)
            sepIndex = path.LastIndexOf(ArchiveSeparator);
        if (sepIndex > 0)
        {
            var archivePath = path[..sepIndex];
            return File.Exists(archivePath) && ArchiveHelper.IsArchive(archivePath);
        }

        // Просто путь к архиву — для перечисления содержимого
        // Just an archive path — for content enumeration
        return File.Exists(path) && ArchiveHelper.IsArchive(path);
    }

    /// <inheritdoc/>
    public async Task<Stream?> OpenContentAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var (archivePath, entryKey) = SplitPath(path);
            if (archivePath is null || entryKey is null)
                return null;

            return await ArchiveHelper.OpenEntryStreamAsync(archivePath, entryKey, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LogService.Warn($"Failed to open archive entry '{path}': {ex.Message}", nameof(ArchiveContentProvider));
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileEntry>> EnumerateContentAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new List<FileEntry>();

        try
        {
            var (archivePath, entryPrefix) = SplitPath(path);
            if (archivePath is null || !File.Exists(archivePath) || !ArchiveHelper.IsArchive(archivePath))
                return result;

            // Нормализуем префикс: "dir/sub" → "dir/sub/"
            // Normalize prefix: "dir/sub" → "dir/sub/"
            if (!string.IsNullOrEmpty(entryPrefix) && !entryPrefix.EndsWith('/'))
                entryPrefix += '/';

            await foreach (var entry in ArchiveHelper.EnumerateEntriesAsync(archivePath, ct))
            {
                ct.ThrowIfCancellationRequested();

                // Фильтруем по префиксу (только прямые потомки)
                // Filter by prefix (only direct children)
                var key = entry.Key?.Replace('\\', '/');
                if (string.IsNullOrEmpty(key)) continue;
                if (string.IsNullOrEmpty(entryPrefix))
                {
                    // Корень архива — показываем только элементы верхнего уровня
                    // Archive root — show only top-level entries
                    if (key.Contains('/'))
                    {
                        // Подкаталог — показываем как виртуальную папку
                        // Subdirectory — show as virtual folder
                        var dirName = key.TrimEnd('/').Split('/')[0];
                        if (result.All(r => r.Name != dirName))
                        {
                            result.Add(new FileEntry(
                                $"{archivePath}{ArchiveSeparator}{dirName}",
                                isDirectory: true, exists: true, size: 0));
                        }
                    }
                    else
                    {
                        result.Add(new FileEntry(
                            $"{archivePath}{ArchiveSeparator}{key}",
                            isDirectory: false, exists: true, size: entry.Size,
                            lastWriteTimeUtc: entry.LastModified.UtcDateTime));
                    }
                }
                else
                {
                    // Внутри подкаталога — фильтруем по префиксу
                    // Inside subdirectory — filter by prefix
                    if (!key.StartsWith(entryPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var relative = key[entryPrefix.Length..];
                    if (string.IsNullOrEmpty(relative)) continue;

                    if (relative.Contains('/'))
                    {
                        // Вложенная папка — показываем как виртуальную папку
                        // Nested folder — show as virtual folder
                        var dirName = relative.TrimEnd('/').Split('/')[0];
                        var virtualPath = $"{archivePath}{ArchiveSeparator}{entryPrefix.TrimEnd('/')}/{dirName}";
                        if (result.All(r => r.FullPath != virtualPath))
                        {
                            result.Add(new FileEntry(
                                virtualPath,
                                isDirectory: true, exists: true, size: 0));
                        }
                    }
                    else
                    {
                        result.Add(new FileEntry(
                            $"{archivePath}{ArchiveSeparator}{entryPrefix}{relative}",
                            isDirectory: false, exists: true, size: entry.Size,
                            lastWriteTimeUtc: entry.LastModified.UtcDateTime));
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            LogService.Error($"Error enumerating archive '{path}'", nameof(ArchiveContentProvider), ex);
        }

        return result;
    }

    /// <summary>
    /// Разделяет путь "archive.zip!/entry/key" на (archivePath, entryKey).
    /// Splits "archive.zip!/entry/key" into (archivePath, entryKey).
    /// Если разделителя нет — archivePath=path, entryKey=null.
    /// If no separator — archivePath=path, entryKey=null.
    /// </summary>
    public static (string? archivePath, string? entryKey) SplitPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return (null, null);

        var sepIndex = path.IndexOf($"{ArchiveSeparator}/", StringComparison.Ordinal);
        if (sepIndex < 0)
            sepIndex = path.LastIndexOf(ArchiveSeparator);
        if (sepIndex < 0 || sepIndex >= path.Length - 1)
            return (path, null);

        return (path[..sepIndex], path[(sepIndex + 1)..]);
    }

    /// <summary>
    /// Формирует составной путь для элемента внутри архива.
    /// Builds a composite path for an entry inside an archive.
    /// </summary>
    public static string CombinePath(string archivePath, string entryKey)
        => $"{archivePath}{ArchiveSeparator}{entryKey}";
}
