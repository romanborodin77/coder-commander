using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.FileSystem;

/// <summary>
/// Адаптер IFileSystem поверх результатов поиска (ph2.2 / exp.yml).
/// Adapter exposing search results through the IFileSystem contract.
/// Перечисление возвращает плоский список найденных файлов с их
/// реальными путями, размером и датой. Модифицирующие операции
/// (копирование / перенос / удаление / атрибуты / ссылки) делегируются
/// локальной ФС, так как результаты указывают на реальные файлы.
/// Enumeration returns the flat list of found files with their real paths, sizes
/// and dates. Mutations are delegated to the local FS, since results point at
/// real files.
/// </summary>
public sealed class SearchResultSource : ISearchResultSource
{
    private readonly LocalFileSystem _local = LocalFileSystem.Instance;
    private readonly object _syncRoot = new();
    private List<SearchResult> _results;
    private List<FileEntry> _entries = new();

    /// <inheritdoc/>
    public string Name => "Результаты поиска";

    /// <inheritdoc/>
    public IReadOnlyList<SearchResult> Results
    {
        get
        {
            lock (_syncRoot)
            {
                return _results;
            }
        }
    }

    /// <summary>
    /// Создаёт виртуальный источник из результатов поиска.
    /// Creates the virtual source from search results.
    /// </summary>
    public SearchResultSource(IEnumerable<SearchResult> results)
    {
        _results = results?.ToList() ?? new List<SearchResult>();
        SyncWithFileSystem();
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        List<FileEntry> snapshot;
        lock (_syncRoot)
        {
            snapshot = _entries;
        }
        return Task.FromResult<IReadOnlyList<FileEntry>>(snapshot);
    }

    /// <inheritdoc/>
    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        FileEntry? e;
        lock (_syncRoot)
        {
            e = _entries.FirstOrDefault(x => string.Equals(x.FullPath, path, StringComparison.OrdinalIgnoreCase));
        }
        return Task.FromResult(e);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => _local.ExistsAsync(path, ct);

    /// <inheritdoc/>
    public Task CopyAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
        => _local.CopyAsync(source, destination, overwrite, ct);

    /// <inheritdoc/>
    public Task MoveAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
        => _local.MoveAsync(source, destination, overwrite, ct);

    /// <inheritdoc/>
    public Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default)
        => _local.DeleteAsync(path, recursive, ct);

    /// <inheritdoc/>
    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        => _local.CreateDirectoryAsync(path, ct);

    /// <inheritdoc/>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default)
        => _local.SetAttributesAsync(path, attributes, ct);

    /// <inheritdoc/>
    public Task CreateHardlinkAsync(string source, string linkPath, CancellationToken ct = default)
        => _local.CreateHardlinkAsync(source, linkPath, ct);

    /// <inheritdoc/>
    public Task CreateSymbolicLinkAsync(string target, string linkPath, bool isDirectory = false, CancellationToken ct = default)
        => _local.CreateSymbolicLinkAsync(target, linkPath, isDirectory, ct);

    /// <inheritdoc/>
    public void SyncWithFileSystem()
    {
        List<FileEntry> entries;
        List<SearchResult> resultsSnapshot;
        lock (_syncRoot)
        {
            resultsSnapshot = _results;
        }
        entries = new List<FileEntry>(resultsSnapshot.Count);
        foreach (var r in resultsSnapshot)
        {
            try
            {
                if (!File.Exists(r.FullPath)) continue; // удалённые — исключаем / dropped if deleted
                var info = new FileInfo(r.FullPath);
                entries.Add(new FileEntry(
                    info.FullName, false, info.Exists,
                    info.Exists ? info.Length : 0,
                    info.Exists ? info.Attributes : default,
                    info.Exists ? info.CreationTimeUtc : default,
                    info.Exists ? info.LastWriteTimeUtc : default,
                    info.Exists ? info.LastAccessTimeUtc : default));
            }
            catch (Exception ex)
            {
                LogService.Warn($"SearchResultSource: пропуск {r.FullPath}: {ex.Message}", nameof(SearchResultSource));
            }
        }
        lock (_syncRoot)
        {
            _entries = entries;
        }
    }

    /// <inheritdoc/>
    public void UpdatePath(string oldPath, string newPath)
    {
        lock (_syncRoot)
        {
            for (int i = 0; i < _results.Count; i++)
            {
                if (string.Equals(_results[i].FullPath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    var r = _results[i];
                    _results[i] = new SearchResult(newPath, r.Size, r.MatchLine, r.LineNumber, r.EncodingName);
                    break;
                }
            }
        }
        SyncWithFileSystem();
    }
}
