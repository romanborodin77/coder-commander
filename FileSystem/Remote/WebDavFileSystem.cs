using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote;

/// <summary>
/// <see cref="IFileSystem"/> over WebDAV (RFC 4918).
///
/// App-side paths are <c>dav://host/dir/name</c>; the mapping to a real URL is this class's job and
/// nobody else's, so the rest of the app never sees an HTTP concept. The instance is bound to one
/// connection's base URL, which is why a path is only ever meaningful to the instance that produced
/// it.
///
/// <para><b><see cref="FileSystemCapabilities.NativePaths"/> is deliberately never declared</b>.
/// Everything gated on it - secure wipe, folder-size walking, packing, timestamp stamping, the
/// Recycle Bin, a FileSystemWatcher, git - would either be meaningless here or would reach around
/// this class to a local path that does not exist. Not declaring it makes every one of those
/// guards refuse correctly without a single new check being written. <see cref="Capabilities"/>
/// DOES declare <see cref="FileSystemCapabilities.Writable"/>/<see cref="FileSystemCapabilities.Deletable"/>
/// unconditionally - WebDAV genuinely supports PUT/DELETE/MKCOL, unlike a read-only archive
/// format, which is the actual distinction those two flags exist to draw.</para>
/// </summary>
public sealed class WebDavFileSystem : IFileSystem, IDisposable
{
    private static readonly HttpMethod Propfind = new("PROPFIND");
    private static readonly HttpMethod Mkcol = new("MKCOL");
    private static readonly HttpMethod Move = new("MOVE");
    private static readonly HttpMethod Copy = new("COPY");

    /// <summary>Only the properties actually used. <c>allprop</c> makes some servers return
    /// megabytes of extensions per entry, and the ones that matter here are these three.</summary>
    private const string PropfindBody =
        """<?xml version="1.0" encoding="utf-8"?><propfind xmlns="DAV:"><prop>""" +
        """<resourcetype/><getcontentlength/><getlastmodified/></prop></propfind>""";

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string _authority;

    /// <summary>Collections this connection has already created, so a copy does not recreate the
    /// same destination once per file. Guarded by its own lock: two panels can copy at once.</summary>
    private readonly HashSet<string> _knownCollections = new(StringComparer.Ordinal);

    public string Name => "WebDAV";

    /// <inheritdoc/>
    public FileSystemCapabilities Capabilities => FileSystemCapabilities.Writable | FileSystemCapabilities.Deletable;

    internal WebDavFileSystem(HttpClient http, Uri baseUri, string authority)
    {
        _http = http;
        _baseUri = baseUri;
        _authority = authority;
    }

    public string GetRootPath(string path) => RemotePath.Make("dav", _authority);

    // ── Path mapping ────────────────────────────────────────────────────────────────────────

    /// <summary>App path to request URL. Each segment is percent-encoded individually, so a name
    /// containing a space or a <c>#</c> survives, while the separators stay separators.</summary>
    private Uri ToUri(string path, bool asCollection = false)
    {
        var inner = RemotePath.PathOf(path);
        var encoded = inner.Length == 0
            ? ""
            : string.Join('/', inner.Split('/').Select(Uri.EscapeDataString));

        var baseText = _baseUri.AbsoluteUri.TrimEnd('/');
        var full = encoded.Length == 0 ? baseText : $"{baseText}/{encoded}";
        if (asCollection && !full.EndsWith('/')) full += "/";
        return new Uri(full);
    }

    private string ToAppPath(string relativeInner) => RemotePath.Make("dav", _authority, relativeInner);

    // ── Reading ─────────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var uri = ToUri(path, asCollection: true);
        using var request = new HttpRequestMessage(Propfind, uri)
        {
            Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml"),
        };
        // Depth: 1 is immediate children. Infinity is what a naive implementation reaches for and
        // is refused outright by most servers (RFC 4918 §9.1 allows them to).
        request.Headers.Add("Depth", "1");

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
        var xml = await ReadBoundedStringAsync(response, ct).ConfigureAwait(false);

        var inner = RemotePath.PathOf(path);

        // Decoded, because the parser compares this against decoded hrefs to recognise the
        // directory's own entry in its own Depth:1 listing. Uri.AbsolutePath keeps percent-encoding,
        // so passing it raw made "/dav/My%20Docs" never equal "/dav/My Docs" - and every directory
        // whose name contained a space or a non-ASCII character listed itself as its own child.
        var entries = WebDavPropfindParser.ParseListing(xml, Uri.UnescapeDataString(uri.AbsolutePath));

        // FileEntry derives Name from FullPath, and Path.GetFileName handles a dav:// path
        // correctly because it splits on '/' as well as '\'. Its archive-path branch cannot fire
        // here: a remote path may not contain '|' (RemotePath rejects it), which is exactly the
        // coexistence rule that keeps these two path flavours apart.
        return entries.Select(e => new FileEntry(
            fullPath: ToAppPath(inner.Length == 0 ? e.Name : $"{inner}/{e.Name}"),
            isDirectory: e.IsDirectory,
            exists: true,
            size: e.Size,
            attributes: e.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            createdTimeUtc: default,
            lastWriteTimeUtc: e.LastWriteTimeUtc,
            lastAccessTimeUtc: default)).ToList();
    }

    /// <summary>
    /// Walks the subtree with repeated <c>Depth: 1</c> requests rather than one
    /// <c>Depth: infinity</c>.
    ///
    /// Servers are explicitly permitted to refuse infinite depth and most do; even where it works,
    /// one request that returns an entire tree cannot be cancelled halfway or reported on. This
    /// costs one round trip per directory and gives both.
    /// </summary>
    public async Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default)
    {
        var result = new List<FileEntry>();
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(path);
        visited.Add(path);

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count >= RemoteLimits.MaxEntriesPerDirectory) break;

            var current = queue.Dequeue();
            IReadOnlyList<FileEntry> children;
            try
            {
                children = await EnumerateAsync(current, includeHidden, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One unreadable subdirectory must not abandon the whole walk - the same rule the
                // local provider's enumeration failures follow.
                LogService.Warning($"WebDAV: cannot list {RemotePath.PathOf(current)}: {ex.GetType().Name}");
                continue;
            }

            foreach (var child in children)
            {
                result.Add(child);
                if (child.IsDirectory && visited.Add(child.FullPath))
                    queue.Enqueue(child.FullPath);
            }
        }

        return result;
    }

    public async Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        var parent = RemotePath.GetParent(path);
        var name = RemotePath.GetName(path);
        if (name.Length == 0) return null;

        var siblings = await EnumerateAsync(parent, includeHidden: true, ct).ConfigureAwait(false);
        return siblings.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Existence check by <c>PROPFIND</c> with <c>Depth: 0</c>, not <c>HEAD</c>.
    ///
    /// HEAD is the obvious choice and the wrong one: a collection is not a retrievable entity, and
    /// servers are within their rights to answer <c>405 Method Not Allowed</c> for one - which would
    /// make every directory on such a server look absent, and every attempt to navigate into it fail
    /// with "path does not exist". PROPFIND is the method WebDAV defines for asking about a resource
    /// and answers for collections and files alike, at the same cost of one round trip.
    /// </summary>
    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(Propfind, ToUri(path, asCollection: true))
        {
            Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "0");

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode || (int)response.StatusCode == 207;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ToUri(path));
        // ResponseHeadersRead so the body streams instead of being buffered whole - a 4 GB file
        // must not become a 4 GB byte[].
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        try
        {
            EnsureSuccess(response, "GET", path);
            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            // HttpClient.Timeout covers only up to headers with ResponseHeadersRead; a server
            // that trickles one byte per minute would hang the body read indefinitely. The
            // TimeoutStream applies RemoteLimits.RequestTimeout per individual read.
            // The response is disposed when the stream is closed, so the underlying connection
            // is returned to the pool — without this, HttpResponseMessage stays live until GC.
            return new TimeoutStream(stream, RemoteLimits.RequestTimeout, ct, response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    // ── Writing ─────────────────────────────────────────────────────────────────────────────

    public async Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, ToUri(destinationPath))
        {
            Content = new StreamContent(source, RemoteLimits.TransferBufferSize),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a collection and any missing ancestors.
    ///
    /// <para>MKCOL creates one collection and fails if the parent is missing (RFC 4918 §9.3), so
    /// ancestors are created top-down. An already-existing collection answers 405, which is the
    /// desired end state rather than an error.</para>
    ///
    /// <para><b>Collections already created are remembered.</b> A copy calls this once per file,
    /// always for the same destination - free on a local disk, a round trip per level per file over
    /// the network. Without the cache, copying a few hundred files spends more requests recreating
    /// an existing collection than transferring data.</para>
    /// </summary>
    public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        var inner = RemotePath.PathOf(path);
        if (inner.Length == 0) return;

        lock (_knownCollections)
        {
            if (_knownCollections.Contains(inner)) return;
        }

        var segments = inner.Split('/');
        var built = "";
        var created = new List<string>();
        foreach (var segment in segments)
        {
            ct.ThrowIfCancellationRequested();
            built = built.Length == 0 ? segment : $"{built}/{segment}";
            created.Add(built);

            using var request = new HttpRequestMessage(Mkcol, ToUri(ToAppPath(built), asCollection: true));
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.Conflict)
                continue;
            EnsureSuccess(response, "MKCOL", built);
        }

        lock (_knownCollections)
        {
            foreach (var collection in created) _knownCollections.Add(collection);
        }
    }

    public async Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        // DELETE on a collection is always recursive in WebDAV; there is no shallow form, so the
        // flag cannot be honoured and saying so beats pretending.
        using var request = new HttpRequestMessage(HttpMethod.Delete, ToUri(path));
        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        TransferAsync(Move, source, destination, overwrite, ct);

    public Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default) =>
        TransferAsync(Copy, source, destination, overwrite, ct);

    /// <summary>MOVE and COPY differ only in the verb: both take the target in a
    /// <c>Destination</c> header and an explicit <c>Overwrite</c> flag (RFC 4918 §9.8, §9.9).
    /// Overwrite defaults to T on the wire, so it is always sent rather than omitted - silently
    /// clobbering a file because a header was left out is not acceptable.</summary>
    private async Task TransferAsync(HttpMethod method, string source, string destination, bool overwrite, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, ToUri(source));
        request.Headers.Add("Destination", ToUri(destination).AbsoluteUri);
        request.Headers.Add("Overwrite", overwrite ? "T" : "F");

        using var response = await SendAsync(request, ct).ConfigureAwait(false);
    }

    /// <summary>WebDAV has no concept of filesystem attributes, so this is a deliberate no-op
    /// rather than an exception: callers set attributes opportunistically after a copy, and
    /// throwing would turn a successful transfer into a failed one.</summary>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default) =>
        Task.CompletedTask;

    /// <summary>Quota reporting (RFC 4331) is optional and widely unimplemented. Returning
    /// <c>(0, 0)</c> is the codebase's established "couldn't determine" answer - the
    /// decompression-bomb guard already treats it that way and skips its check rather than
    /// blocking on a number it doesn't have.</summary>
    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default) =>
        Task.FromResult((0L, 0L));

    // ── Plumbing ────────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        EnsureSuccess(response, request.Method.Method, request.RequestUri?.AbsolutePath ?? "");
        return response;
    }

    /// <summary>
    /// Turns a failing status into an exception carrying a message worth showing.
    ///
    /// The status is included but the response body never is: a server is free to put anything in
    /// it, and an error dialog is the last place a hostile string should be rendered verbatim.
    /// 207 is a success - it is how PROPFIND answers.
    /// </summary>
    private static void EnsureSuccess(HttpResponseMessage response, string verb, string path)
    {
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 207) return;

        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "authentication failed",
            HttpStatusCode.Forbidden => "access denied",
            HttpStatusCode.NotFound => "not found",
            HttpStatusCode.Conflict => "parent collection is missing",
            HttpStatusCode.PreconditionFailed => "destination exists and overwrite was refused",
            // Raised against a chunked PUT, which is what an upload from a source whose length is
            // not known in advance (an archive entry, another connection) has to use.
            HttpStatusCode.LengthRequired => "the server requires the file size in advance and cannot accept this upload",
            (HttpStatusCode)423 => "resource is locked",
            (HttpStatusCode)507 => "insufficient storage on the server",
            _ => response.StatusCode.ToString(),
        };
        throw new IOException($"WebDAV {verb} failed for \"{path}\": {(int)response.StatusCode} {reason}");
    }

    /// <summary>Reads a response body with a hard ceiling, so a server streaming without end
    /// cannot exhaust memory.</summary>
    private static async Task<string> ReadBoundedStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();

        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > RemoteLimits.MaxListingBytes)
            {
                LogService.Warning("WebDAV: listing response exceeded the size limit and was truncated");
                break;
            }
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    public void Dispose() => _http.Dispose();
}
