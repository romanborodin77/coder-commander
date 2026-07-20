using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CoderCommander.Services;
using CoderCommander.Models;

namespace CoderCommander.FileSystem;

/// <summary>
/// Реализация <see cref="IFileSystem"/> для Amazon S3 и S3-совместимых хранилищ.
/// IFileSystem implementation for Amazon S3 and S3-compatible storages.
/// </summary>
public sealed class S3FileSystem : CloudFileSystem, IDisposable
{
    private AmazonS3Client? _client;
    private readonly string _accessKey;
    private readonly string _secretKey;
    private readonly string _region;
    private readonly string _bucket;
    private readonly string? _endpoint;

    /// <inheritdoc/>
    public override string Name => $"S3 ({_bucket})";

    /// <inheritdoc/>
    public override bool IsConnected => _client is not null;

    /// <summary>Имя бакета. / Bucket name.</summary>
    public string Bucket => _bucket;

    /// <summary>
    /// Создаёт S3-filesystem по параметрам подключения.
    /// Creates an S3 filesystem from connection parameters.
    /// </summary>
    public S3FileSystem(string accessKey, string secretKey, string region, string bucket, string? endpoint = null)
    {
        _accessKey = accessKey;
        _secretKey = secretKey;
        _region = region;
        _bucket = bucket;
        _endpoint = endpoint;
    }

    /// <summary>
    /// Создаёт S3-filesystem из профиля облачного хранилища.
    /// Creates an S3 filesystem from a cloud storage profile.
    /// </summary>
    public S3FileSystem(CloudProfile profile)
    {
        _bucket = profile.BucketOrContainer ?? "";
        _region = profile.Region ?? "us-east-1";
        _endpoint = profile.Endpoint;
        profile.Credentials.TryGetValue("AccessKey", out var ak);
        profile.Credentials.TryGetValue("SecretKey", out var sk);
        _accessKey = ak ?? "";
        _secretKey = sk ?? "";
    }

    /// <inheritdoc/>
    public override Task ConnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var credentials = new BasicAWSCredentials(_accessKey, _secretKey);
        var config = new AmazonS3Config
        {
            RegionEndpoint = !string.IsNullOrEmpty(_endpoint) ? null : RegionEndpoint.GetBySystemName(_region)
        };
        if (!string.IsNullOrEmpty(_endpoint))
        {
            config.ServiceURL = _endpoint;
            config.ForcePathStyle = true;
        }
        _client = new AmazonS3Client(credentials, config);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task DisconnectAsync()
    {
        _client?.Dispose();
        _client = null;
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
        var request = new ListObjectsV2Request
        {
            BucketName = _bucket,
            Prefix = prefix,
            Delimiter = "/"
        };

        do
        {
            ct.ThrowIfCancellationRequested();
            var response = await _client!.ListObjectsV2Async(request, ct);

            // Виртуальные каталоги (CommonPrefixes) / Virtual directories (CommonPrefixes).
            foreach (var cp in response.CommonPrefixes)
            {
                var dirName = cp.TrimEnd('/');
                var name = GetFileName(dirName);
                if (string.IsNullOrEmpty(name)) continue;
                if (!includeHidden && name.StartsWith('.')) continue;
                var fullPath = KeyToPath(cp);
                result.Add(new FileEntry(fullPath, isDirectory: true, exists: true));
            }

            // Файлы / Files.
            foreach (var obj in response.S3Objects)
            {
                // Пропускаем сам префикс-каталог / Skip the directory prefix itself.
                if (obj.Key == prefix) continue;
                var name = GetFileName(obj.Key);
                if (string.IsNullOrEmpty(name)) continue;
                if (!includeHidden && name.StartsWith('.')) continue;

                result.Add(new FileEntry(
                    fullPath: KeyToPath(obj.Key),
                    isDirectory: false,
                    exists: true,
                    size: obj.Size,
                    attributes: FileAttributes.Normal,
                    createdTimeUtc: default,
                    lastWriteTimeUtc: obj.LastModified.ToUniversalTime()));
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (request.ContinuationToken is not null);

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

        // Проверяем как файл / Check as file.
        try
        {
            var meta = await _client!.GetObjectMetadataAsync(_bucket, key, ct);
            return new FileEntry(
                fullPath: path,
                isDirectory: false,
                exists: true,
                size: meta.ContentLength,
                attributes: FileAttributes.Normal,
                lastWriteTimeUtc: meta.LastModified.ToUniversalTime());
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Проверяем как каталог (префикс) / Check as directory (prefix).
            var prefix = key.TrimEnd('/') + '/';
            var listReq = new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = prefix,
                MaxKeys = 1
            };
            var listResp = await _client!.ListObjectsV2Async(listReq, ct);
            if (listResp?.S3Objects?.Count > 0)
                return new FileEntry(path, isDirectory: true, exists: true);

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

        if (!overwrite)
        {
            if (await ExistsAsync(destination, ct))
                throw new IOException($"Destination already exists: {destination}");
        }

        var request = new CopyObjectRequest
        {
            SourceBucket = _bucket,
            SourceKey = srcKey,
            DestinationBucket = _bucket,
            DestinationKey = dstKey
        };
        await _client!.CopyObjectAsync(request, ct);
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

        try
        {
            await _client!.DeleteObjectAsync(_bucket, key, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Может быть каталогом / May be a directory.
        }

        if (recursive)
        {
            var prefix = key.TrimEnd('/') + '/';
            var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix };
            do
            {
                ct.ThrowIfCancellationRequested();
                var response = await _client!.ListObjectsV2Async(request, ct);
                foreach (var obj in response.S3Objects)
                {
                    await _client.DeleteObjectAsync(_bucket, obj.Key, ct);
                }
                request.ContinuationToken = response.NextContinuationToken;
            } while (request.ContinuationToken is not null);
        }
    }

    /// <inheritdoc/>
    public override Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        EnsureClient();
        // В S3 нет настоящих каталогов, создаём маркерный объект / No real directories in S3, create a marker object.
        var key = PathToKey(path).TrimEnd('/') + '/';
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            ContentBody = ""
        };
        return _client!.PutObjectAsync(request, ct);
    }

    /// <summary>
    /// Загружает файл на S3 с отчётом прогресса.
    /// Uploads a file to S3 with progress reporting.
    /// </summary>
    internal async Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureClient();
        var key = PathToKey(remotePath);
        using var fs = File.OpenRead(localPath);
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = fs
        };
        // S3 SDK не поддерживает простой прогресс для PutObject, загружаем полностью.
        // S3 SDK doesn't have simple progress for PutObject, upload entirely.
        await _client!.PutObjectAsync(request, ct);
        progress?.Report(fs.Length);
    }

    /// <summary>
    /// Скачивает файл с S3 с отчётом прогресса.
    /// Downloads a file from S3 with progress reporting.
    /// </summary>
    internal async Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureClient();
        var key = PathToKey(remotePath);
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var request = new GetObjectRequest
        {
            BucketName = _bucket,
            Key = key
        };
        using var response = await _client!.GetObjectAsync(request, ct);
        using var fs = File.Create(localPath);
        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await response.ResponseStream.ReadAsync(buffer, ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            progress?.Report(totalRead);
        }
    }

    private void EnsureClient()
    {
        if (_client is null)
            throw new InvalidOperationException("S3 not connected. Call ConnectAsync first.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
