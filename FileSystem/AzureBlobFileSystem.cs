using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CoderCommander.Services;
using CoderCommander.Models;

namespace CoderCommander.FileSystem;

/// <summary>
/// Реализация <see cref="IFileSystem"/> для Azure Blob Storage.
/// IFileSystem implementation for Azure Blob Storage.
/// </summary>
public sealed class AzureBlobFileSystem : CloudFileSystem, IDisposable
{
    private BlobContainerClient? _containerClient;
    private readonly string _connectionString;
    private readonly string _containerName;

    /// <inheritdoc/>
    public override string Name => $"Azure ({_containerName})";

    /// <inheritdoc/>
    public override bool IsConnected => _containerClient is not null;

    /// <summary>Имя контейнера. / Container name.</summary>
    public string ContainerName => _containerName;

    /// <summary>
    /// Создаёт Azure Blob filesystem по строке подключения и имени контейнера.
    /// Creates an Azure Blob filesystem from connection string and container name.
    /// </summary>
    public AzureBlobFileSystem(string connectionString, string containerName)
    {
        _connectionString = connectionString;
        _containerName = containerName;
    }

    /// <summary>
    /// Создаёт Azure Blob filesystem из профиля облачного хранилища.
    /// Creates an Azure Blob filesystem from a cloud storage profile.
    /// </summary>
    public AzureBlobFileSystem(CloudProfile profile)
    {
        _containerName = profile.BucketOrContainer ?? "";
        profile.Credentials.TryGetValue("ConnectionString", out var cs);
        _connectionString = cs ?? "";
    }

    /// <inheritdoc/>
    public override Task ConnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _containerClient = new BlobContainerClient(_connectionString, _containerName);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task DisconnectAsync()
    {
        _containerClient = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden = false, CancellationToken ct = default)
    {
        EnsureClient();
        var prefix = PathToKey(path);
        if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith('/'))
            prefix += '/';

        var result = new List<FileEntry>();
        await foreach (var item in _containerClient!.GetBlobsByHierarchyAsync(prefix: prefix, delimiter: "/", cancellationToken: ct))
        {
            ct.ThrowIfCancellationRequested();

            if (item.IsPrefix)
            {
                // Виртуальный каталог / Virtual directory.
                var dirName = item.Prefix.TrimEnd('/');
                var name = GetFileName(dirName);
                if (string.IsNullOrEmpty(name)) continue;
                if (!includeHidden && name.StartsWith('.')) continue;
                result.Add(new FileEntry(KeyToPath(item.Prefix), isDirectory: true, exists: true));
            }
            else if (item.IsBlob)
            {
                var blob = item.Blob;
                var name = GetFileName(blob.Name);
                if (string.IsNullOrEmpty(name)) continue;
                if (!includeHidden && name.StartsWith('.')) continue;
                result.Add(new FileEntry(
                    fullPath: KeyToPath(blob.Name),
                    isDirectory: false,
                    exists: true,
                    size: blob.Properties.ContentLength ?? 0,
                    attributes: FileAttributes.Normal,
                    createdTimeUtc: default,
                    lastWriteTimeUtc: blob.Properties.LastModified?.UtcDateTime ?? default));
            }
        }

        result.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    /// <inheritdoc/>
    public override async Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        EnsureClient();
        var key = PathToKey(path);
        if (string.IsNullOrEmpty(key))
            return new FileEntry("/", isDirectory: true, exists: true);

        var blobClient = _containerClient!.GetBlobClient(key);
        try
        {
            var props = await blobClient.GetPropertiesAsync(cancellationToken: ct);
            return new FileEntry(
                fullPath: path,
                isDirectory: false,
                exists: true,
                size: props.Value.ContentLength,
                attributes: FileAttributes.Normal,
                createdTimeUtc: default,
                lastWriteTimeUtc: props.Value.LastModified.UtcDateTime);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Проверяем как каталог / Check as directory.
            var prefix = key.TrimEnd('/') + '/';
            await foreach (var item in _containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
            {
                return new FileEntry(path, isDirectory: true, exists: true);
            }
            return null;
        }
    }

    /// <inheritdoc/>
    public override async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var info = await GetFileInfoAsync(path, ct);
        return info is not null && info.Exists;
    }

    /// <inheritdoc/>
    public override async Task CopyAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
    {
        EnsureClient();
        var srcKey = PathToKey(source);
        var dstKey = PathToKey(destination);
        var srcBlob = _containerClient!.GetBlobClient(srcKey);
        var dstBlob = _containerClient.GetBlobClient(dstKey);

        if (!overwrite)
        {
            if (await dstBlob.ExistsAsync(ct))
                throw new IOException($"Destination already exists: {destination}");
        }

        await dstBlob.StartCopyFromUriAsync(srcBlob.Uri, cancellationToken: ct);
    }

    /// <inheritdoc/>
    public override async Task MoveAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
    {
        await CopyAsync(source, destination, overwrite, ct);
        await DeleteAsync(source, false, ct);
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default)
    {
        EnsureClient();
        var key = PathToKey(path);
        if (string.IsNullOrEmpty(key)) return;

        var blobClient = _containerClient!.GetBlobClient(key);
        try
        {
            await blobClient.DeleteAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Может быть каталогом / May be a directory.
        }

        if (recursive)
        {
            var prefix = key.TrimEnd('/') + '/';
            await foreach (var blob in _containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
            {
                ct.ThrowIfCancellationRequested();
                await _containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: ct);
            }
        }
    }

    /// <inheritdoc/>
    public override Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        // Azure Blob не имеет настоящих каталогов; маркер не нужен.
        // Azure Blob has no real directories; no marker needed.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Загружает файл в Azure Blob с отчётом прогресса.
    /// Uploads a file to Azure Blob with progress reporting.
    /// </summary>
    internal async Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureClient();
        var key = PathToKey(remotePath);
        var blobClient = _containerClient!.GetBlobClient(key);
        using var fs = File.OpenRead(localPath);
        await blobClient.UploadAsync(fs, overwrite: true, cancellationToken: ct);
        progress?.Report(fs.Length);
    }

    /// <summary>
    /// Скачивает файл из Azure Blob с отчётом прогресса.
    /// Downloads a file from Azure Blob with progress reporting.
    /// </summary>
    internal async Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureClient();
        var key = PathToKey(remotePath);
        var blobClient = _containerClient!.GetBlobClient(key);
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var download = await blobClient.DownloadAsync(ct);
        using var fs = File.Create(localPath);
        await download.Value.Content.CopyToAsync(fs, ct);
        progress?.Report(fs.Length);
    }

    private void EnsureClient()
    {
        if (_containerClient is null)
            throw new InvalidOperationException("Azure Blob not connected. Call ConnectAsync first.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _containerClient = null;
    }
}
