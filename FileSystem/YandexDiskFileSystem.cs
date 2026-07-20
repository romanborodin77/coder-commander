using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.FileSystem;

/// <summary>
/// Реализация <see cref="IFileSystem"/> для Yandex Disk (OAuth2 REST API).
/// IFileSystem implementation for Yandex Disk (OAuth2 REST API).
/// Документация / Docs: https://yandex.ru/dev/disk/rest/
/// </summary>
public sealed class YandexDiskFileSystem : CloudFileSystem, IDisposable
{
    private const string BaseUrl = "https://cloud-api.yandex.net/v1/disk/";

    private HttpClient? _http;
    private readonly string _token;
    private readonly string _rootPath;

    /// <inheritdoc/>
    public override string Name => $"Yandex Disk ({_rootPath})";

    /// <inheritdoc/>
    public override bool IsConnected => _http is not null;

    /// <summary>Корневая папка. / Root path.</summary>
    public string RootPath => _rootPath;

    /// <summary>
    /// Создаёт Yandex Disk filesystem из OAuth-токена.
    /// Creates a Yandex Disk filesystem from an OAuth token.
    /// </summary>
    public YandexDiskFileSystem(string token, string? rootPath = null)
    {
        _token = token;
        _rootPath = string.IsNullOrWhiteSpace(rootPath) ? "/" : rootPath.TrimEnd('/');
    }

    /// <summary>
    /// Создаёт Yandex Disk filesystem из профиля облачного хранилища.
    /// Creates a Yandex Disk filesystem from a cloud storage profile.
    /// </summary>
    public YandexDiskFileSystem(CloudProfile profile)
    {
        profile.Credentials.TryGetValue("OAuthToken", out var tk);
        _token = tk ?? "";
        _rootPath = string.IsNullOrWhiteSpace(profile.RootPath) ? "/" : profile.RootPath.TrimEnd('/');
    }

    /// <inheritdoc/>
    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_token))
            throw new InvalidOperationException("OAuth token is empty. Get one at https://oauth.yandex.ru/");

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", _token);

        // Проверяем токен: GET /v1/disk/ / Verify token.
        var resp = await _http.GetAsync(BaseUrl, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _http.Dispose();
            _http = null;
            throw new HttpRequestException($"Yandex Disk auth failed ({resp.StatusCode}). Check OAuth token.");
        }
    }

    /// <inheritdoc/>
    public override Task DisconnectAsync()
    {
        _http?.Dispose();
        _http = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden = false, CancellationToken ct = default)
    {
        EnsureClient();
        var ydPath = BuildApiPath(path);
        var url = $"{BaseUrl}resources?path={Uri.EscapeDataString(ydPath)}&fields=_embedded.items";

        var resp = await _http!.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var result = new List<FileEntry>();
        if (!doc.RootElement.TryGetProperty("_embedded", out var embedded))
            return result;
        if (!embedded.TryGetProperty("items", out var items))
            return result;

        foreach (var item in items.EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            var name = item.GetStringOrDefault("name");
            if (string.IsNullOrEmpty(name)) continue;
            if (!includeHidden && name.StartsWith('.')) continue;

            var type = item.GetStringOrDefault("type"); // "dir" or "file"
            var fullPath = item.GetStringOrDefault("path") ?? path.TrimEnd('/') + "/" + name;
            // API возвращает path вида "disk:/foo/bar" — нормализуем / API returns "disk:/foo/bar" — normalize.
            var normalizedPath = NormalizeApiPathToEntryPath(fullPath, ydPath, name);

            var isDir = string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase);
            var size = item.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
            var modified = item.TryGetProperty("modified", out var modEl)
                ? DateTime.TryParse(modEl.GetString(), out var dt) ? dt.ToUniversalTime() : default
                : default;
            var created = item.TryGetProperty("created", out var crEl)
                ? DateTime.TryParse(crEl.GetString(), out var cdt) ? cdt.ToUniversalTime() : default
                : default;

            result.Add(new FileEntry(
                fullPath: normalizedPath,
                isDirectory: isDir,
                exists: true,
                size: isDir ? 0 : size,
                attributes: System.IO.FileAttributes.Normal,
                createdTimeUtc: created,
                lastWriteTimeUtc: modified));
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
        var ydPath = BuildApiPath(path);
        var url = $"{BaseUrl}resources?path={Uri.EscapeDataString(ydPath)}";
        var resp = await _http!.GetAsync(url, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var type = root.GetStringOrDefault("type");
        var isDir = string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase);
        var name = root.GetStringOrDefault("name") ?? GetFileName(path);
        var size = root.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0;
        var modified = root.TryGetProperty("modified", out var modEl)
            ? DateTime.TryParse(modEl.GetString(), out var dt) ? dt.ToUniversalTime() : default : default;
        var created = root.TryGetProperty("created", out var crEl)
            ? DateTime.TryParse(crEl.GetString(), out var cdt) ? cdt.ToUniversalTime() : default : default;

        return new FileEntry(
            fullPath: path,
            isDirectory: isDir,
            exists: true,
            size: isDir ? 0 : size,
            attributes: System.IO.FileAttributes.Normal,
            createdTimeUtc: created,
            lastWriteTimeUtc: modified);
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
        if (!overwrite && await ExistsAsync(destination, ct))
            throw new IOException($"Destination already exists: {destination}");

        var body = JsonSerializer.Serialize(new { path = BuildApiPath(destination), overwrite = overwrite });
        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}resources/copy?path={Uri.EscapeDataString(BuildApiPath(source))}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var resp = await _http!.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        // API возвращает асинхронную задачу — ждём завершения / API returns an async task — await it.
        await WaitForOperationAsync(resp, ct);
    }

    /// <inheritdoc/>
    public override async Task MoveAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
    {
        EnsureClient();
        if (!overwrite && await ExistsAsync(destination, ct))
            throw new IOException($"Destination already exists: {destination}");

        var body = JsonSerializer.Serialize(new { path = BuildApiPath(destination), overwrite = overwrite });
        var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}resources/move?path={Uri.EscapeDataString(BuildApiPath(source))}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var resp = await _http!.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        await WaitForOperationAsync(resp, ct);
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default)
    {
        EnsureClient();
        var ydPath = BuildApiPath(path);
        var url = $"{BaseUrl}resources?path={Uri.EscapeDataString(ydPath)}&permanently=true";
        var resp = await _http!.DeleteAsync(url, ct);
        if (resp.StatusCode != HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
        await WaitForOperationAsync(resp, ct);
    }

    /// <inheritdoc/>
    public override async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        EnsureClient();
        var ydPath = BuildApiPath(path);
        var url = $"{BaseUrl}resources?path={Uri.EscapeDataString(ydPath)}";
        var resp = await _http!.PutAsync(url, content: null, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Скачивает файл с Yandex Disk с отчётом прогресса.
    /// Downloads a file from Yandex Disk with progress reporting.
    /// </summary>
    internal async Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureClient();
        var ydPath = BuildApiPath(remotePath);
        // Получаем ссылку на скачивание / Get download href.
        var url = $"{BaseUrl}resources/download?path={Uri.EscapeDataString(ydPath)}";
        var resp = await _http!.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var href = doc.RootElement.GetStringOrDefault("href")
            ?? throw new InvalidOperationException("Yandex Disk: download href not found");

        // Скачиваем файл / Download the file.
        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var dlResp = await _http.GetAsync(href, HttpCompletionOption.ResponseHeadersRead, ct);
        dlResp.EnsureSuccessStatusCode();
        using var stream = await dlResp.Content.ReadAsStreamAsync(ct);
        using var fs = File.Create(localPath);
        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            progress?.Report(totalRead);
        }
    }

    /// <summary>
    /// Загружает файл на Yandex Disk с отчётом прогресса.
    /// Uploads a file to Yandex Disk with progress reporting.
    /// </summary>
    internal async Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureClient();
        var ydPath = BuildApiPath(remotePath);
        // Получаем ссылку на загрузку / Get upload href.
        var url = $"{BaseUrl}resources/upload?path={Uri.EscapeDataString(ydPath)}&overwrite=true";
        var resp = await _http!.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var href = doc.RootElement.GetStringOrDefault("href")
            ?? throw new InvalidOperationException("Yandex Disk: upload href not found");

        using var fs = File.OpenRead(localPath);
        using var content = new StreamContent(fs);
        using var uploadResp = await _http.PutAsync(href, content, ct);
        uploadResp.EnsureSuccessStatusCode();
        progress?.Report(fs.Length);
    }

    private void EnsureClient()
    {
        if (_http is null)
            throw new InvalidOperationException("Yandex Disk not connected. Call ConnectAsync first.");
    }

    /// <summary>
    /// Строит API-путь (с префиксом диска, если нужно).
    /// Builds an API path (with disk prefix if needed).
    /// </summary>
    private string BuildApiPath(string path)
    {
        var p = path.TrimStart('/');
        // Yandex Disk API ожидает путь вида "/foo/bar" или "disk:/foo/bar".
        // API expects path like "/foo/bar" or "disk:/foo/bar".
        if (p.StartsWith("disk:/") || p.StartsWith("disk:\\"))
            return "/" + p.Substring("disk:/".Length);
        return "/" + p;
    }

    /// <summary>
    /// Нормализует путь из ответа API ("disk:/...") в путь относительно _rootPath.
    /// Normalizes API response path ("disk:/...") to a path relative to _rootPath.
    /// </summary>
    private string NormalizeApiPathToEntryPath(string apiPath, string parentApiPath, string name)
    {
        // API возвращает "disk:/foo/bar/name" — извлекаем относительный путь.
        // API returns "disk:/foo/bar/name" — extract relative path.
        var normalized = apiPath;
        if (normalized.StartsWith("disk:"))
            normalized = normalized.Substring("disk:".Length);

        if (_rootPath == "/" || string.IsNullOrWhiteSpace(_rootPath))
            return normalized;
        return normalized;
    }

    /// <summary>
    /// Ждёт завершения асинхронной операции Yandex Disk API (polling href).
    /// Waits for a Yandex Disk async operation to complete (polling href).
    /// </summary>
    private async Task WaitForOperationAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.Content is null || resp.Content.Headers.ContentLength == 0) return;
        string body;
        try
        {
            body = await resp.Content.ReadAsStringAsync(ct);
        }
        catch { return; }
        if (string.IsNullOrWhiteSpace(body)) return;

        using var doc = JsonDocument.Parse(body);
        var status = doc.RootElement.GetStringOrDefault("status");
        if (status is null) return;

        // Если вернулась асинхронная задача, опрашиваем её статус.
        // If an async task is returned, poll its status.
        if (string.Equals(status, "in-progress", StringComparison.OrdinalIgnoreCase))
        {
            var href = doc.RootElement.GetStringOrDefault("href");
            if (string.IsNullOrEmpty(href)) return;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(500, ct);
                using var pollResp = await _http!.GetAsync(href, ct);
                pollResp.EnsureSuccessStatusCode();
                var pollBody = await pollResp.Content.ReadAsStringAsync(ct);
                using var pollDoc = JsonDocument.Parse(pollBody);
                var pollStatus = pollDoc.RootElement.GetStringOrDefault("status");
                if (string.Equals(pollStatus, "success", StringComparison.OrdinalIgnoreCase)) return;
                if (string.Equals(pollStatus, "failed", StringComparison.OrdinalIgnoreCase))
                    throw new HttpRequestException("Yandex Disk operation failed");
                // in-progress — продолжаем / in-progress — keep polling.
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _http?.Dispose();
        _http = null;
    }
}

/// <summary>
/// Extension-методы для <see cref="JsonElement"/> (безопасное извлечение строк).
/// Extension methods for <see cref="JsonElement"/> (safe string extraction).
/// </summary>
internal static class JsonElementExtensions
{
    public static string? GetStringOrDefault(this JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
