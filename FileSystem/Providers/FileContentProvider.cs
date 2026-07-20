using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Providers;

/// <summary>
/// Стандартный провайдер содержимого для локальной файловой системы (ph4.3 / exp.yml).
/// Default content provider for the local filesystem (ph4.3).
/// Предоставляет <see cref="File.OpenRead"/> для чтения содержимого и
/// <see cref="Directory.EnumerateFileSystemEntries"/> для перечисления каталогов.
/// Provides File.OpenRead for content reading and
/// Directory.EnumerateFileSystemEntries for directory enumeration.
///
/// Провайдер всегда обрабатывает любой путь, для которого существует локальный файл/каталог.
/// Provider always handles any path where a local file/directory exists.
/// </summary>
public sealed class FileContentProvider : IContentProvider
{
    /// <inheritdoc/>
    public string Name => "Local FS";

    /// <inheritdoc/>
    public bool CanHandle(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            // Путь с недопустимыми символами и т.п.
            // Path with invalid characters, etc.
            return false;
        }
    }

    /// <inheritdoc/>
    public Task<Stream?> OpenContentAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
            // FileShare.ReadWrite | Delete — разрешаем параллельный доступ и запись.
            // FileShare.ReadWrite | Delete — allow concurrent access and writing.
            Stream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 81920, useAsync: true);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception ex)
        {
            LogService.Warn($"Failed to open content for '{path}': {ex.Message}", nameof(FileContentProvider));
            return Task.FromResult<Stream?>(null);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FileEntry>> EnumerateContentAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var result = new List<FileEntry>();

        try
        {
            if (!Directory.Exists(path))
                return Task.FromResult<IReadOnlyList<FileEntry>>(result);

            var opt = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false,
            };

            foreach (var entryPath in Directory.EnumerateFileSystemEntries(path, "*", opt))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var attrs = File.GetAttributes(entryPath);
                    bool isDir = attrs.HasFlag(FileAttributes.Directory);
                    if (isDir)
                    {
                        var di = new DirectoryInfo(entryPath);
                        result.Add(new FileEntry(
                            di.FullName, true, di.Exists, 0,
                            di.Exists ? di.Attributes : default,
                            di.Exists ? di.CreationTimeUtc : default,
                            di.Exists ? di.LastWriteTimeUtc : default,
                            di.Exists ? di.LastAccessTimeUtc : default));
                    }
                    else
                    {
                        var fi = new FileInfo(entryPath);
                        result.Add(new FileEntry(
                            fi.FullName, false, fi.Exists, fi.Exists ? fi.Length : 0,
                            fi.Exists ? fi.Attributes : default,
                            fi.Exists ? fi.CreationTimeUtc : default,
                            fi.Exists ? fi.LastWriteTimeUtc : default,
                            fi.Exists ? fi.LastAccessTimeUtc : default));
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warn($"Error enumerating '{entryPath}': {ex.Message}", nameof(FileContentProvider));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            LogService.Warn($"Access denied: {path}", nameof(FileContentProvider));
        }
        catch (IOException ex)
        {
            LogService.Error($"IO error enumerating '{path}'", nameof(FileContentProvider), ex);
        }

        return Task.FromResult<IReadOnlyList<FileEntry>>(result);
    }
}
