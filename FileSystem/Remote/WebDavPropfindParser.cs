using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote;

/// <summary>One entry read out of a PROPFIND multistatus response.</summary>
/// <param name="Name">Decoded last path segment - the file or directory name.</param>
/// <param name="HrefPath">Absolute, still-encoded path from the response's <c>href</c>, used to
/// address the resource on subsequent requests without re-encoding it ourselves.</param>
public sealed record WebDavEntry(
    string Name,
    string HrefPath,
    bool IsDirectory,
    long Size,
    DateTime LastWriteTimeUtc);

/// <summary>
/// Parses a WebDAV <c>PROPFIND</c> multistatus response (RFC 4918 §9.1, §14.16).
///
/// Deliberately separate from <see cref="WebDavFileSystem"/> and free of any HTTP: this is the
/// part where a hostile or merely eccentric server does damage, and keeping it pure means the
/// whole protocol surface can be tested against real-world response shapes without a network, a
/// server, or a mock HTTP stack.
///
/// <para><b>Robustness rules applied throughout.</b> Real servers disagree about almost
/// everything: namespace prefixes (<c>D:</c>, <c>d:</c>, none), absolute vs path-only hrefs,
/// trailing slashes on collections, whether <c>getcontentlength</c> appears for directories, and
/// which date format <c>getlastmodified</c> uses. A missing or unparseable property is treated as
/// "unknown" and the entry is still returned - dropping a file because its timestamp was odd would
/// hide real data.</para>
///
/// <para><b>XML is parsed with DTD processing disabled.</b> The document comes from an untrusted
/// server; leaving DTDs enabled invites entity-expansion denial of service ("billion laughs") and
/// external-entity disclosure. This is the one setting that must not be relaxed for
/// compatibility.</para>
/// </summary>
public static class WebDavPropfindParser
{
    private static readonly XNamespace Dav = "DAV:";

    /// <summary>
    /// Reads the entries of a directory listing.
    ///
    /// <paramref name="requestPath"/> is the decoded absolute path that was asked about; the
    /// response's own entry for it is skipped, so the result contains children only. Servers vary
    /// on whether that self-entry carries a trailing slash, so the comparison normalises both
    /// sides rather than trusting either.
    /// </summary>
    public static IReadOnlyList<WebDavEntry> ParseListing(string xml, string requestPath)
    {
        var entries = new List<WebDavEntry>();
        XDocument doc;

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                // Non-negotiable: the document is attacker-influenced.
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = true,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
            });
            doc = XDocument.Load(reader);
        }
        catch (Exception ex)
        {
            // A malformed body is a server problem, not a reason to take the panel down. The
            // caller sees an empty directory and the reason lands in the log.
            LogService.Warning($"WebDAV: unparseable PROPFIND response ({ex.GetType().Name})");
            return entries;
        }

        var selfKey = NormalizeForComparison(requestPath);

        foreach (var response in doc.Descendants(Dav + "response"))
        {
            if (entries.Count >= RemoteLimits.MaxEntriesPerDirectory)
            {
                LogService.Warning(
                    $"WebDAV: listing truncated at {RemoteLimits.MaxEntriesPerDirectory} entries");
                break;
            }

            var href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href)) continue;

            var hrefPath = ExtractPath(href);
            if (hrefPath.Length == 0) continue;

            // Skip the entry describing the directory itself. With Depth: 1 the server includes
            // it, and without this the panel would show a child that is really its own parent.
            if (NormalizeForComparison(Uri.UnescapeDataString(hrefPath)) == selfKey) continue;

            var prop = FindProp(response);
            var isDirectory = prop?.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null;

            var name = ExtractName(hrefPath);
            if (name.Length == 0) continue;

            // The server names the entry; that name goes on to build local paths during a
            // download, so it is checked exactly like an archive entry name.
            if (!RemotePath.IsSafeEntryName(name))
            {
                LogService.Warning("WebDAV: rejected a listing entry with an unsafe name");
                continue;
            }

            entries.Add(new WebDavEntry(
                name,
                hrefPath,
                isDirectory,
                isDirectory ? 0 : ParseLong(prop?.Element(Dav + "getcontentlength")?.Value),
                ParseDate(prop?.Element(Dav + "getlastmodified")?.Value)));
        }

        return entries;
    }

    /// <summary>
    /// The <c>prop</c> element of the first <c>propstat</c> whose status is 2xx.
    ///
    /// A response may carry several propstat blocks - typically one 200 with the properties that
    /// exist and one 404 with those that don't. Reading properties out of the 404 block yields
    /// empty values that look like real answers, which is how a file ends up displayed with size
    /// zero.
    /// </summary>
    private static XElement? FindProp(XElement response)
    {
        XElement? fallback = null;

        foreach (var propstat in response.Elements(Dav + "propstat"))
        {
            var prop = propstat.Element(Dav + "prop");
            if (prop is null) continue;

            fallback ??= prop;
            var status = propstat.Element(Dav + "status")?.Value;
            if (status is not null && IsSuccessStatus(status))
                return prop;
        }

        // Some servers omit propstat entirely and put prop directly under response.
        return fallback ?? response.Element(Dav + "prop");
    }

    /// <summary>Status lines look like <c>HTTP/1.1 200 OK</c>; only the code matters.</summary>
    private static bool IsSuccessStatus(string status)
    {
        foreach (var token in status.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                return code is >= 200 and < 300;
        }
        return false;
    }

    /// <summary>Path portion of an href, which may be absolute (<c>https://host/a/b</c>) or
    /// path-only (<c>/a/b</c>) - RFC 4918 permits both and real servers use both.</summary>
    private static string ExtractPath(string href)
    {
        var trimmed = href.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
            return absolute.AbsolutePath;

        var query = trimmed.IndexOf('?');
        if (query >= 0) trimmed = trimmed[..query];
        return trimmed;
    }

    /// <summary>Last segment of an href path, percent-decoded. Collections usually carry a
    /// trailing slash, which must come off before the name is taken.</summary>
    private static string ExtractName(string hrefPath)
    {
        var trimmed = hrefPath.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var raw = slash < 0 ? trimmed : trimmed[(slash + 1)..];

        try
        {
            return Uri.UnescapeDataString(raw);
        }
        catch (UriFormatException)
        {
            // Malformed percent-encoding: keep the raw form rather than dropping the entry, and
            // let IsSafeEntryName decide whether it is usable.
            return raw;
        }
    }

    /// <summary>Trailing slash and case are not meaningful when deciding whether two hrefs name
    /// the same resource.</summary>
    private static string NormalizeForComparison(string path) =>
        path.TrimEnd('/').ToLowerInvariant();

    private static long ParseLong(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : 0;

    /// <summary>
    /// <c>getlastmodified</c> is defined as an HTTP-date (RFC 1123), but servers emit ISO 8601 and
    /// other shapes too. Both are accepted; anything else yields <c>default</c>, which the UI
    /// renders as "unknown" rather than as 1 January 0001.
    /// </summary>
    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return default;

        if (DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var rfc1123))
            return rfc1123.UtcDateTime;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var general))
            return general.UtcDateTime;

        return default;
    }
}
