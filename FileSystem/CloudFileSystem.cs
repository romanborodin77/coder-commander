using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.FileSystem;

/// <summary>
/// Базовый абстрактный класс для облачных файловых систем.
/// Base abstract class for cloud file systems.
/// Реализует <see cref="IFileSystem"/> через облачные API.
/// Implements <see cref="IFileSystem"/> via cloud APIs.
/// </summary>
public abstract class CloudFileSystem : IFileSystem
{
    /// <summary>Человекочитаемое имя источника. / Human-readable source name.</summary>
    public abstract string Name { get; }

    /// <summary>Подключено ли хранилище. / Whether the storage is connected.</summary>
    public abstract bool IsConnected { get; }

    /// <summary>
    /// Устанавливает подключение к облачному хранилищу.
    /// Connects to the cloud storage.
    /// </summary>
    public abstract Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Закрывает подключение к облачному хранилищу.
    /// Disconnects from the cloud storage.
    /// </summary>
    public abstract Task DisconnectAsync();

    /// <inheritdoc/>
    public abstract Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden = false, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task CopyAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task MoveAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default);

    /// <inheritdoc/>
    public abstract Task CreateDirectoryAsync(string path, CancellationToken ct = default);

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Облачные хранилища не поддерживают установку атрибутов.</exception>
    public virtual Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException("Cloud storage does not support SetAttributes."));

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Облачные хранилища не поддерживают жёсткие ссылки.</exception>
    public virtual Task CreateHardlinkAsync(string source, string linkPath, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException("Cloud storage does not support hard links."));

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">Облачные хранилища не поддерживают символические ссылки.</exception>
    public virtual Task CreateSymbolicLinkAsync(string target, string linkPath, bool isDirectory = false, CancellationToken ct = default)
        => Task.FromException(new NotSupportedException("Cloud storage does not support symbolic links."));

    /// <summary>
    /// Нормализует путь: всегда через '/', без дублирующихся разделителей.
    /// Normalizes path: always uses '/', no duplicate separators.
    /// </summary>
    protected static string NormalizePath(string dir, string name)
    {
        var baseDir = dir.TrimEnd('/');
        return baseDir.Length == 0 ? "/" + name : baseDir + "/" + name;
    }

    /// <summary>
    /// Получает имя файла или каталога из полного пути.
    /// Gets the file or directory name from a full path.
    /// </summary>
    protected static string GetFileName(string path)
    {
        var trimmed = path.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
    }

    /// <summary>
    /// Получает родительский путь из полного пути.
    /// Gets the parent path from a full path.
    /// </summary>
    protected static string GetParentPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx <= 0 ? "/" : trimmed[..idx];
    }

    /// <summary>
    /// Извлекает ключ объекта из пути (без лидирующего '/').
    /// Extracts the object key from the path (without leading '/').
    /// </summary>
    protected static string PathToKey(string path)
    {
        return path.TrimStart('/');
    }

    /// <summary>
    /// Формирует путь из ключа объекта (с лидирующим '/').
    /// Forms a path from an object key (with leading '/').
    /// </summary>
    protected static string KeyToPath(string key)
    {
        if (string.IsNullOrEmpty(key)) return "/";
        return "/" + key.TrimStart('/');
    }
}
