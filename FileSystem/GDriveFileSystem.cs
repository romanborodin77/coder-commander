using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Models;
using CoderCommander.Services;
using GDriveFile = Google.Apis.Drive.v3.Data.File;
using Google.Apis.Drive.v3;

namespace CoderCommander.FileSystem;

/// <summary>
/// Реализация <see cref="IFileSystem"/> для Google Drive (OAuth2 через Google.Apis.Drive.v3).
/// IFileSystem implementation for Google Drive (OAuth2 via Google.Apis.Drive.v3).
/// Google Drive использует mimeType <c>application/vnd.google-apps.folder</c> для папок.
/// Google Drive uses mimeType <c>application/vnd.google-apps.folder</c> for folders.
/// </summary>
public sealed class GDriveFileSystem : CloudFileSystem, IDisposable
{
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    private const string FieldsList = "files(id,name,mimeType,size,modifiedTime,createdTime)";
    private const string FieldsSingle = "id,name,mimeType,size,modifiedTime,createdTime,parents";

    private DriveService? _service;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _refreshToken;

    /// <summary>Кэш путей к ID папок для ускорения навигации. / Path-to-folder-ID cache for faster navigation.</summary>
    private readonly Dictionary<string, string> _folderCache = new();

    /// <inheritdoc/>
    public override string Name => "Google Drive";

    /// <inheritdoc/>
    public override bool IsConnected => _service is not null;

    /// <summary>
    /// Создаёт GDrive-filesystem из параметров OAuth2.
    /// Creates a GDrive filesystem from OAuth2 parameters.
    /// </summary>
    public GDriveFileSystem(string clientId, string clientSecret, string refreshToken)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _refreshToken = refreshToken;
    }

    /// <summary>
    /// Создаёт GDrive-filesystem из профиля облачного хранилища.
    /// Creates a GDrive filesystem from a cloud storage profile.
    /// </summary>
    public GDriveFileSystem(CloudProfile profile)
    {
        profile.Credentials.TryGetValue("ClientId", out var cid);
        profile.Credentials.TryGetValue("ClientSecret", out var cs);
        profile.Credentials.TryGetValue("RefreshToken", out var rt);
        _clientId = cid ?? "";
        _clientSecret = cs ?? "";
        _refreshToken = rt ?? "";
    }

    /// <inheritdoc/>
    public override Task ConnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_refreshToken))
            return Task.FromException(new InvalidOperationException(
                "Google Drive not authorized. Please click 'Authorize' to get a Refresh Token."));

        try
        {
            _service = GoogleOAuthService.CreateDriveService(_clientId, _clientSecret, _refreshToken);
            _folderCache["/"] = "root";
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(new InvalidOperationException(
                $"Google Drive authorization failed: {ex.Message}", ex));
        }
    }

    /// <inheritdoc/>
    public override Task DisconnectAsync()
    {
        _service?.Dispose();
        _service = null;
        _folderCache.Clear();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task<IReadOnlyList<FileEntry>> EnumerateAsync(
        string path, bool includeHidden = false, CancellationToken ct = default)
    {
        EnsureService();
        var parentId = await ResolvePathToIdAsync(path, ct)
            ?? throw new DirectoryNotFoundException($"Path not found in Google Drive: {path}");

        var result = new List<FileEntry>();
        string? pageToken = null;

        do
        {
            ct.ThrowIfCancellationRequested();
            var query = $"'{parentId}' in parents and trashed = false";
            var request = _service!.Files.List();
            request.Q = query;
            request.Fields = $"nextPageToken, {FieldsList}";
            request.PageSize = 100;
            request.PageToken = pageToken;

            var response = await request.ExecuteAsync(ct);

            if (response.Files != null)
            {
                foreach (var file in response.Files)
                {
                    var isDir = file.MimeType == FolderMimeType;
                    var fullPath = NormalizePath(path, file.Name);
                    var lastWrite = file.ModifiedTimeDateTimeOffset?.UtcDateTime ?? default;
                    var created = file.CreatedTimeDateTimeOffset?.UtcDateTime ?? default;
                    long size = file.Size ?? 0;

                    result.Add(new FileEntry(
                        fullPath: fullPath,
                        isDirectory: isDir,
                        exists: true,
                        size: size,
                        attributes: System.IO.FileAttributes.Normal,
                        createdTimeUtc: created,
                        lastWriteTimeUtc: lastWrite));

                    if (isDir)
                        _folderCache[fullPath] = file.Id;
                }
            }

            pageToken = response.NextPageToken;
        } while (pageToken != null);

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
        EnsureService();
        if (path == "/" || path == "")
            return new FileEntry("/", isDirectory: true, exists: true);

        var fileId = await ResolvePathToIdAsync(path, ct);
        if (fileId is null) return null;

        try
        {
            var request = _service!.Files.Get(fileId);
            request.Fields = FieldsSingle;
            var file = await request.ExecuteAsync(ct);
            var isDir = file.MimeType == FolderMimeType;
            return new FileEntry(
                fullPath: path,
                isDirectory: isDir,
                exists: true,
                size: file.Size ?? 0,
                attributes: System.IO.FileAttributes.Normal,
                createdTimeUtc: file.CreatedTimeDateTimeOffset?.UtcDateTime ?? default,
                lastWriteTimeUtc: file.ModifiedTimeDateTimeOffset?.UtcDateTime ?? default);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _folderCache.Remove(path);
            return null;
        }
    }

    /// <inheritdoc/>
    public override async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        if (path == "/") return true;
        var id = await ResolvePathToIdAsync(path, ct);
        return id is not null;
    }

    /// <inheritdoc/>
    public override async Task CopyAsync(string source, string destination,
        bool overwrite = false, CancellationToken ct = default)
    {
        EnsureService();
        var srcId = await ResolvePathToIdAsync(source, ct)
            ?? throw new FileNotFoundException($"Source not found in Google Drive: {source}");

        var dstParentPath = GetParentPath(destination);
        var dstName = GetFileName(destination);
        var dstParentId = await ResolvePathToIdAsync(dstParentPath, ct)
            ?? throw new DirectoryNotFoundException($"Destination parent not found: {dstParentPath}");

        if (!overwrite && await ExistsAsync(destination, ct))
            throw new IOException($"Destination already exists in Google Drive: {destination}");

        var body = new GDriveFile
        {
            Name = dstName,
            Parents = new List<string> { dstParentId },
        };
        var request = _service!.Files.Copy(body, srcId);
        await request.ExecuteAsync(ct);
    }

    /// <inheritdoc/>
    public override async Task MoveAsync(string source, string destination,
        bool overwrite = false, CancellationToken ct = default)
    {
        EnsureService();
        var fileId = await ResolvePathToIdAsync(source, ct)
            ?? throw new FileNotFoundException($"Source not found in Google Drive: {source}");

        var srcParentPath = GetParentPath(source);
        var dstParentPath = GetParentPath(destination);
        var dstName = GetFileName(destination);

        if (!overwrite && await ExistsAsync(destination, ct))
            throw new IOException($"Destination already exists in Google Drive: {destination}");

        var srcParentId = await ResolvePathToIdAsync(srcParentPath, ct) ?? "root";
        var dstParentId = await ResolvePathToIdAsync(dstParentPath, ct)
            ?? throw new DirectoryNotFoundException($"Destination parent not found: {dstParentPath}");

        var body = new GDriveFile { Name = dstName };
        var request = _service!.Files.Update(body, fileId);

        if (srcParentId != dstParentId)
        {
            request.AddParents = dstParentId;
            request.RemoveParents = srcParentId;
        }

        await request.ExecuteAsync(ct);

        InvalidateCacheForPath(source);
        if (await ResolvePathToIdAsync(source, ct) is null)
            _folderCache.Remove(source);

        var info = await GetFileInfoAsync(destination, ct);
        if (info?.IsDirectory == true)
            _folderCache[destination] = fileId;
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default)
    {
        EnsureService();
        var fileId = await ResolvePathToIdAsync(path, ct);
        if (fileId is null) return;

        await _service!.Files.Delete(fileId).ExecuteAsync(ct);
        InvalidateCacheForPath(path);
    }

    /// <inheritdoc/>
    public override async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        EnsureService();
        if (path == "/") return;

        var parentPath = GetParentPath(path);
        var name = GetFileName(path);
        var parentId = await ResolvePathToIdAsync(parentPath, ct)
            ?? throw new DirectoryNotFoundException($"Parent not found in Google Drive: {parentPath}");

        var body = new GDriveFile
        {
            Name = name,
            MimeType = FolderMimeType,
            Parents = new List<string> { parentId },
        };
        var result = await _service!.Files.Create(body).ExecuteAsync(ct);
        _folderCache[path] = result.Id;
    }

    /// <summary>
    /// Скачивает файл из Google Drive с отчётом прогресса.
    /// Downloads a file from Google Drive with progress reporting.
    /// </summary>
    internal async Task DownloadFileAsync(string remotePath, string localPath,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureService();
        var fileId = await ResolvePathToIdAsync(remotePath, ct)
            ?? throw new FileNotFoundException($"File not found in Google Drive: {remotePath}");

        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var request = _service!.Files.Get(fileId);
        using var fileStream = File.Create(localPath);
        await request.DownloadAsync(fileStream, ct);

        progress?.Report(fileStream.Length);
    }

    /// <summary>
    /// Загружает локальный файл в Google Drive с отчётом прогресса.
    /// Uploads a local file to Google Drive with progress reporting.
    /// </summary>
    internal async Task UploadFileAsync(string localPath, string remotePath,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureService();
        var parentPath = GetParentPath(remotePath);
        var name = GetFileName(remotePath);
        var parentId = await ResolvePathToIdAsync(parentPath, ct)
            ?? throw new DirectoryNotFoundException($"Parent folder not found: {parentPath}");

        using var stream = File.OpenRead(localPath);
        var body = new GDriveFile
        {
            Name = name,
            Parents = new List<string> { parentId },
        };

        var mimeType = "application/octet-stream";
        var ext = Path.GetExtension(localPath).ToLowerInvariant();
        if (ext == ".txt") mimeType = "text/plain";
        else if (ext == ".json") mimeType = "application/json";
        else if (ext == ".xml") mimeType = "application/xml";
        else if (ext == ".html" || ext == ".htm") mimeType = "text/html";
        else if (ext == ".pdf") mimeType = "application/pdf";
        else if (ext == ".png") mimeType = "image/png";
        else if (ext == ".jpg" || ext == ".jpeg") mimeType = "image/jpeg";
        else if (ext == ".gif") mimeType = "image/gif";
        else if (ext == ".svg") mimeType = "image/svg+xml";
        else if (ext == ".zip") mimeType = "application/zip";
        else if (ext == ".cs") mimeType = "text/plain";
        else if (ext == ".py") mimeType = "text/plain";
        else if (ext == ".js") mimeType = "text/plain";

        var request = _service!.Files.Create(body, stream, mimeType);
        await request.UploadAsync(ct);

        progress?.Report(stream.Length);
    }

    private async Task<string?> ResolvePathToIdAsync(string path, CancellationToken ct)
    {
        if (path == "/" || path == "") return "root";
        if (_folderCache.TryGetValue(path, out var cached)) return cached;

        var segments = path.Trim('/').Split('/');
        var currentId = "root";
        var currentPath = "";

        foreach (var segment in segments)
        {
            ct.ThrowIfCancellationRequested();
            var nextPath = currentPath + "/" + segment;

            if (_folderCache.TryGetValue(nextPath, out var id))
            {
                currentId = id;
                currentPath = nextPath;
                continue;
            }

            var query = $"name = '{EscapeQuery(segment)}' and '{currentId}' in parents and trashed = false";
            var request = _service!.Files.List();
            request.Q = query;
            request.Fields = "files(id, name, mimeType)";
            request.PageSize = 1;

            var response = await request.ExecuteAsync(ct);
            var item = response.Files?.FirstOrDefault();

            if (item is null) return null;

            _folderCache[nextPath] = item.Id;
            currentId = item.Id;
            currentPath = nextPath;
        }

        return currentId;
    }

    private static string EscapeQuery(string value)
    {
        return value.Replace("'", "\\'").Replace("\\", "\\\\");
    }

    private void InvalidateCacheForPath(string path)
    {
        var keys = _folderCache.Keys.Where(k => k == path || k.StartsWith(path + "/")).ToList();
        foreach (var key in keys)
            _folderCache.Remove(key);
    }

    private void EnsureService()
    {
        if (_service is null)
            throw new InvalidOperationException("Google Drive not connected. Call ConnectAsync first.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _service?.Dispose();
        _service = null;
    }
}
