using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.FileSystem;

/// <summary>
/// Реализация <see cref="IFileSystem"/> для NextCloud через WebDAV.
/// IFileSystem implementation for NextCloud via WebDAV.
/// Документация / Docs: https://docs.nextcloud.com/server/latest/developer_manual/client_apis/WebDAV/basic.html
/// </summary>
public sealed class NextCloudFileSystem : CloudFileSystem, IDisposable
{
    private static readonly XNamespace DavNs = "DAV:";

    private HttpClient? _http;
    private readonly string _serverUrl;
    private readonly string _username;
    private readonly string _password;
    private readonly string _rootPath;
    private string _davBase = "";

    /// <inheritdoc/>
    public override string Name => $"NextCloud ({_username}@{_serverUrl})";

    /// <inheritdoc/>
    public override bool IsConnected => _http is not null;

    /// <summary>URL сервера. / Server URL.</summary>
    public string ServerUrl => _serverUrl;

    /// <summary>Имя пользователя. / Username.</summary>
    public string Username => _username;

    /// <summary>
    /// Создаёт NextCloud filesystem из параметров подключения.
    /// Creates a NextCloud filesystem from connection parameters.
    /// </summary>
    public NextCloudFileSystem(string serverUrl, string username, string password, string? rootPath = null)
    {
        _serverUrl = serverUrl.TrimEnd('/');
        _username = username;
        _password = password;
        _rootPath = string.IsNullOrWhiteSpace(rootPath) ? "/" : rootPath.TrimEnd('/');
    }

    /// <summary>
    /// Создаёт NextCloud filesystem из профиля облачного хранилища.
    /// Creates a NextCloud filesystem from a cloud storage profile.
    /// </summary>
    public NextCloudFileSystem(CloudProfile profile)
    {
        _serverUrl = (profile.Endpoint ?? "").TrimEnd('/');
        profile.Credentials.TryGetValue("Username", out var un);
        profile.Credentials.TryGetValue("Password", out var pw);
        _username = un ?? "";
        _password = pw ?? "";
        _rootPath = string.IsNullOrWhiteSpace(profile.RootPath) ? "/" : profile.RootPath.TrimEnd('/');
    }

    /// <inheritdoc/>
    public override async Task ConnectAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_serverUrl))
            throw new InvalidOperationException("Server URL is empty");
        if (string.IsNullOrWhiteSpace(_username))
            throw new InvalidOperationException("Username is empty");

        _http = new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        _davBase = $"{_serverUrl}/remote.php/dav/files/{Uri.EscapeDataString(_username)}";

        // Проверяем подключение PROPFIND на корень / Verify connection with PROPFIND on root.
        var rootPath = BuildDavPath("/");
        var xml = BuildPropfindXml();
        var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), rootPath)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
        req.Headers.Add("Depth", "0");

        var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _http.Dispose();
            _http = null;
            throw new HttpRequestException($"NextCloud connection failed ({resp.StatusCode}). Check credentials and server URL.");
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
        var davUrl = BuildDavPath(path);
        var xml = BuildPropfindXml();

        var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), davUrl)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
        req.Headers.Add("Depth", "1");

        var resp = await _http!.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);

        return ParsePropfindResponse(body, path, includeHidden);
    }

    /// <inheritdoc/>
    public override async Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        EnsureClient();
        var davUrl = BuildDavPath(path);
        var xml = BuildPropfindXml();

        var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), davUrl)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
        req.Headers.Add("Depth", "0");

        var resp = await _http!.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);

        var entries = ParsePropfindResponse(body, GetParentPath(path), false);
        return entries.FirstOrDefault(e => string.Equals(GetFileName(e.FullPath), GetFileName(path), StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault();
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

        var srcUrl = BuildDavPath(source);
        var dstUrl = BuildDavPath(destination);

        var req = new HttpRequestMessage(new HttpMethod("COPY"), srcUrl);
        req.Headers.Add("Destination", dstUrl);
        req.Headers.Add("Overwrite", overwrite ? "T" : "F");

        var resp = await _http!.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public override async Task MoveAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
    {
        EnsureClient();
        if (!overwrite && await ExistsAsync(destination, ct))
            throw new IOException($"Destination already exists: {destination}");

        var srcUrl = BuildDavPath(source);
        var dstUrl = BuildDavPath(destination);

        var req = new HttpRequestMessage(new HttpMethod("MOVE"), srcUrl);
        req.Headers.Add("Destination", dstUrl);
        req.Headers.Add("Overwrite", overwrite ? "T" : "F");

        var resp = await _http!.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public override async Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default)
    {
        EnsureClient();
        var davUrl = BuildDavPath(path);
        var resp = await _http!.DeleteAsync(davUrl, ct);
        if (resp.StatusCode != HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }

    /// <inheritdoc/>
    public override async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        EnsureClient();
        var davUrl = BuildDavPath(path);
        if (!davUrl.EndsWith('/'))
            davUrl += '/';

        var req = new HttpRequestMessage(new HttpMethod("MKCOL"), davUrl);
        var resp = await _http!.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Скачивает файл из NextCloud с отчётом прогресса.
    /// Downloads a file from NextCloud with progress reporting.
    /// </summary>
    internal async Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureClient();
        var davUrl = BuildDavPath(remotePath);

        var dir = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var dlResp = await _http!.GetAsync(davUrl, HttpCompletionOption.ResponseHeadersRead, ct);
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
    /// Загружает файл в NextCloud с отчётом прогресса.
    /// Uploads a file to NextCloud with progress reporting.
    /// </summary>
    internal async Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        EnsureClient();
        var davUrl = BuildDavPath(remotePath);

        using var fs = File.OpenRead(localPath);
        using var content = new StreamContent(fs);
        using var uploadResp = await _http!.PutAsync(davUrl, content, ct);
        uploadResp.EnsureSuccessStatusCode();
        progress?.Report(fs.Length);
    }

    private void EnsureClient()
    {
        if (_http is null)
            throw new InvalidOperationException("NextCloud not connected. Call ConnectAsync first.");
    }

    /// <summary>
    /// Строит WebDAV URL для заданного пути.
    /// Builds a WebDAV URL for the given path.
    /// </summary>
    private string BuildDavPath(string path)
    {
        var p = path.TrimStart('/');
        if (_rootPath != "/" && !string.IsNullOrWhiteSpace(_rootPath))
        {
            var rootTrimmed = _rootPath.TrimStart('/').TrimEnd('/');
            if (!string.IsNullOrEmpty(rootTrimmed))
                p = string.IsNullOrEmpty(p) ? rootTrimmed : rootTrimmed + "/" + p;
        }

        if (string.IsNullOrEmpty(p))
            return _davBase + "/";

        var segments = p.Split('/');
        var encoded = string.Join("/", segments.Select(Uri.EscapeDataString));
        return _davBase + "/" + encoded;
    }

    /// <summary>
    /// Строит XML-тело PROPFIND запроса.
    /// Builds the PROPFIND request XML body.
    /// </summary>
    private static string BuildPropfindXml()
    {
        return """
            <?xml version="1.0" encoding="utf-8"?>
            <d:propfind xmlns:d="DAV:" xmlns:oc="http://owncloud.org/ns" xmlns:nc="http://nextcloud.org/ns">
              <d:prop>
                <d:displayname/>
                <d:getcontentlength/>
                <d:getlastmodified/>
                <d:resourcetype/>
                <d:getcontenttype/>
              </d:prop>
            </d:propfind>
            """;
    }

    /// <summary>
    /// Парсит XML-ответ PROPFIND (multistatus) в список FileEntry.
    /// Parses the PROPFIND multistatus XML response into a list of FileEntry.
    /// </summary>
    private IReadOnlyList<FileEntry> ParsePropfindResponse(string xml, string requestPath, bool includeHidden)
    {
        var result = new List<FileEntry>();
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch
        {
            return result;
        }

        var root = doc.Root;
        if (root is null) return result;

        var responses = root.Elements(DavNs + "response").ToList();
        var requestUri = BuildDavPath(requestPath).TrimEnd('/');

        foreach (var response in responses)
        {
            var href = response.Element(DavNs + "href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;

            // Пропускаем сам запрашиваемый ресурс (Depth: 1 возвращает и родителя) / Skip the requested resource itself.
            var decodedHref = Uri.UnescapeDataString(href).TrimEnd('/');
            if (decodedHref.Equals(requestUri, StringComparison.OrdinalIgnoreCase))
                continue;

            var propstat = response.Element(DavNs + "propstat");
            if (propstat is null) continue;
            var prop = propstat.Element(DavNs + "prop");
            if (prop is null) continue;

            var displayname = prop.Element(DavNs + "displayname")?.Value;
            var contentLengthStr = prop.Element(DavNs + "getcontentlength")?.Value;
            var lastModifiedStr = prop.Element(DavNs + "getlastmodified")?.Value;
            var resourceType = prop.Element(DavNs + "resourcetype");

            var isDir = resourceType?.Element(DavNs + "collection") is not null;
            var name = !string.IsNullOrEmpty(displayname) ? displayname : GetFileName(decodedHref);

            if (string.IsNullOrEmpty(name)) continue;
            if (!includeHidden && name.StartsWith('.')) continue;

            long.TryParse(contentLengthStr, out var size);
            var modified = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(lastModifiedStr) &&
                DateTime.TryParse(lastModifiedStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            {
                modified = dt.ToUniversalTime();
            }

            // Строим виртуальный путь / Build virtual path.
            var entryPath = BuildEntryPath(decodedHref, requestPath, name);

            result.Add(new FileEntry(
                fullPath: entryPath,
                isDirectory: isDir,
                exists: true,
                size: isDir ? 0 : size,
                attributes: System.IO.FileAttributes.Normal,
                lastWriteTimeUtc: modified));
        }

        result.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    /// <summary>
    /// Строит виртуальный путь элемента из href-ответа.
    /// Builds a virtual entry path from the response href.
    /// </summary>
    private string BuildEntryPath(string decodedHref, string requestPath, string name)
    {
        // Извлекаем относительную часть от _davBase / Extract relative part from _davBase.
        var davBasePath = new Uri(_davBase).AbsolutePath.TrimEnd('/');
        var entryHref = decodedHref.TrimEnd('/');

        var idx = entryHref.IndexOf(davBasePath, StringComparison.OrdinalIgnoreCase);
        string relativePath;
        if (idx >= 0)
        {
            relativePath = entryHref[(idx + davBasePath.Length)..];
            if (string.IsNullOrEmpty(relativePath))
                relativePath = "/" + name;
        }
        else
        {
            relativePath = "/" + name;
        }

        // Если есть _rootPath, убираем его из относительного пути / Strip rootPath from relative path.
        if (_rootPath != "/" && !string.IsNullOrWhiteSpace(_rootPath))
        {
            var rootTrimmed = _rootPath.TrimStart('/').TrimEnd('/');
            if (relativePath.StartsWith("/" + rootTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath[("/" + rootTrimmed).Length..];
                if (string.IsNullOrEmpty(relativePath))
                    relativePath = "/" + name;
            }
        }

        if (!relativePath.StartsWith('/'))
            relativePath = "/" + relativePath;

        return relativePath;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _http?.Dispose();
        _http = null;
    }
}
