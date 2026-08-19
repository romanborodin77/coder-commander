namespace CoderCommander.FileSystem;

#pragma warning disable CA1308 // Lowercase URL scheme per RFC 3986 normalization, stored in constructed remote paths
/// <summary>
/// Path arithmetic and safety checks for remote providers, whose paths look like
/// <c>dav://host/dir/name</c>.
///
/// This is the third path flavour in the app, after plain Windows paths and archive paths
/// (<c>C:\a\f.zip|inner/name</c>), and it has to coexist with both:
///
/// <list type="bullet">
/// <item><see cref="System.IO.Path"/> mangles it exactly as it mangles an archive path -
/// <c>Path.Combine</c> would insert backslashes, <c>GetPathRoot</c> would return nonsense - so
/// nothing here goes through it.</item>
/// <item><see cref="VfsPath.IsArchive"/> decides "this is an archive path" from a bare <c>|</c>
/// anywhere in the string. A remote path containing <c>|</c> would therefore be torn apart as an
/// archive path, so <c>|</c> is rejected outright rather than escaped. The same character has
/// already caused one real defect in this codebase, in unpacking.</item>
/// </list>
///
/// Everything a server sends is untrusted input, exactly like an archive entry name, and is
/// treated with the same suspicion: see <see cref="IsSafeEntryName"/> and
/// <see cref="EscapesRoot"/>, which mirror <see cref="Archives.ArchiveSafety"/> - including its
/// rule of failing closed when a check itself throws.
/// </summary>
public static class RemotePath
{
    /// <summary>Separator between scheme and the rest, e.g. the <c>://</c> of <c>dav://host/x</c>.</summary>
    public const string SchemeSeparator = "://";

    /// <summary>
    /// Upper bound on a whole remote path. A server is free to answer with megabytes of nested
    /// names; without a cap those flow straight into UI strings and dictionary keys. The value
    /// is deliberately far above any legitimate path and far below anything that hurts.
    /// </summary>
    public const int MaxPathLength = 8192;

    /// <summary>Upper bound on one path segment - roughly the strictest limit real servers impose,
    /// and well under Windows' own 255-character component limit for the local cache case.</summary>
    public const int MaxSegmentLength = 255;

    /// <summary><c>true</c> when <paramref name="path"/> is a remote path. Must be consulted
    /// **before** <see cref="VfsPath.IsArchive"/> when classifying a path, so a remote path is
    /// never mistaken for an archive one.</summary>
    public static bool IsRemote(string? path) =>
        !string.IsNullOrEmpty(path) && SchemeOf(path) is not null;

    /// <summary>Scheme of a remote path (<c>"dav"</c>), or <c>null</c> when it isn't one.
    /// Only ASCII letters and digits are accepted, so a Windows path such as <c>C:\x</c> can never
    /// be read as a scheme.</summary>
    public static string? SchemeOf(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var idx = path.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (idx <= 0) return null;

        for (var i = 0; i < idx; i++)
        {
            var c = path[i];
            // Deliberately narrower than RFC 3986 (which also allows '+', '-', '.'): the app owns
            // every scheme it registers, and a narrow rule can't accidentally match something else.
            if (!char.IsAsciiLetterOrDigit(c)) return null;
        }
        return path[..idx].ToLowerInvariant();
    }

    /// <summary>Everything after <c>scheme://</c>, i.e. <c>host/dir/name</c>. Empty for a
    /// non-remote path.</summary>
    public static string BodyOf(string path)
    {
        var idx = path.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        return idx < 0 ? "" : path[(idx + SchemeSeparator.Length)..];
    }

    /// <summary>Host component, i.e. the first segment of the body. Empty when absent.</summary>
    public static string HostOf(string path)
    {
        var body = BodyOf(path);
        var slash = body.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? body : body[..slash];
    }

    /// <summary>Slash-separated path below the host, with no leading or trailing slash.</summary>
    public static string PathOf(string path)
    {
        var body = BodyOf(path);
        var slash = body.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? "" : Normalize(body[(slash + 1)..]);
    }

    /// <summary>Root of a remote path: <c>scheme://host</c>. Returns the input unchanged if it is
    /// not a remote path.</summary>
    public static string GetRoot(string path)
    {
        var scheme = SchemeOf(path);
        return scheme is null ? path : $"{scheme}{SchemeSeparator}{HostOf(path)}";
    }

    /// <summary>Builds <c>scheme://host/path</c> from parts.</summary>
    public static string Make(string scheme, string host, string path = "")
    {
        var normalized = Normalize(path);
        var root = $"{scheme.ToLowerInvariant()}{SchemeSeparator}{host}";
        return normalized.Length == 0 ? root : $"{root}/{normalized}";
    }

    /// <summary>Collapses separators to a bare <c>a/b/c</c> form, accepting backslashes on input
    /// because users paste Windows-shaped paths into address fields.</summary>
    public static string Normalize(string? path) =>
        string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/').Trim('/');

    /// <summary>Appends a relative path, keeping the remote form.</summary>
    public static string Combine(string basePath, string relative)
    {
        var tail = Normalize(relative);
        if (tail.Length == 0) return basePath;

        var head = PathOf(basePath);
        var joined = head.Length == 0 ? tail : $"{head}/{tail}";
        return Make(SchemeOf(basePath) ?? "", HostOf(basePath), joined);
    }

    /// <summary>Parent directory, or the root when already at it.</summary>
    public static string GetParent(string path)
    {
        var inner = PathOf(path);
        if (inner.Length == 0) return GetRoot(path);

        var slash = inner.LastIndexOf('/');
        var parent = slash < 0 ? "" : inner[..slash];
        return Make(SchemeOf(path) ?? "", HostOf(path), parent);
    }

    /// <summary>Last segment - the file or directory name.</summary>
    public static string GetName(string path)
    {
        var inner = PathOf(path);
        if (inner.Length == 0) return HostOf(path);

        var slash = inner.LastIndexOf('/');
        return slash < 0 ? inner : inner[(slash + 1)..];
    }

    // ── Safety ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Whether one name supplied by a server may be used as a path segment.
    ///
    /// The threat is the same one <see cref="Archives.ArchiveSafety"/> addresses for archive
    /// entries: a listing is attacker-influenced data, and a name from it ends up concatenated
    /// into a path that something later writes to. Rejected, in order of how they bite:
    ///
    /// <list type="bullet">
    /// <item><c>..</c> and <c>.</c> - traversal out of the current directory.</item>
    /// <item><c>/</c> and <c>\</c> - a "name" that is really a path, smuggling extra levels.</item>
    /// <item><c>|</c> - would make <see cref="VfsPath.IsArchive"/> read the whole path as an
    /// archive path and split it in the wrong place.</item>
    /// <item><c>:</c> - an NTFS alternate data stream (<c>readme.txt:payload.exe</c>) once the name
    /// reaches a local path during a download; the same guard exists in ArchiveSafety and for the
    /// same reason - nothing else catches it, because Windows treats it as an ordinary relative
    /// name.</item>
    /// <item>Control characters, including CR and LF, which corrupt protocol framing and log lines.</item>
    /// <item>Bidi overrides/isolates and zero-width characters - Trojan-Source style display
    /// spoofing, where a name renders as something other than what it is. Same ranges
    /// <c>Terminal/Vt/OscSanitizer.cs</c> strips, but rejected rather than stripped: a name is an
    /// identity, and quietly altering it would make two different entries look like one.</item>
    /// <item>Trailing dot or space - Windows silently strips those when creating a file, so
    /// <c>evil.exe.</c> and <c>evil.exe</c> would collide after a download.</item>
    /// <item>Reserved DOS device names (<c>CON</c>, <c>PRN</c>, <c>AUX</c>, <c>NUL</c>,
    /// <c>COM1</c>-<c>COM9</c>, <c>LPT1</c>-<c>LPT9</c>), with or without an extension - Windows
    /// treats <c>CON</c> and <c>CON.txt</c> alike as the device, not a file, so a name from a
    /// listing that reaches local disk under one of these can silently fail to create or read back
    /// as something else entirely.</item>
    /// </list>
    /// </summary>
    public static bool IsSafeEntryName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Length > MaxSegmentLength) return false;
        if (name is "." or "..") return false;

        var last = name[^1];
        if (last is '.' or ' ') return false;

        foreach (var c in name)
        {
            if (c is '/' or '\\' or '|' or ':') return false;
            if (char.IsControl(c)) return false;
            if (IsDisplaySpoofing(c)) return false;
        }

        return !IsReservedDeviceName(name);
    }

    /// <summary>Reserved Windows device names, checked by their stem (before the first <c>.</c>) so
    /// both <c>CON</c> and <c>CON.txt</c> are caught the way <c>CreateFile</c> treats them.</summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static bool IsReservedDeviceName(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? name : name[..dot];
        return ReservedDeviceNames.Contains(stem);
    }

    /// <summary>Bidi and zero-width code points that let a name render as something it isn't.
    /// Written as numeric ranges on purpose, so the source stays readable and no tool can mangle
    /// an invisible literal - the same decision <c>OscSanitizer</c> documents.</summary>
    private static bool IsDisplaySpoofing(char c) =>
        c is >= '\u202A' and <= '\u202E' ||   // LRE, RLE, PDF, LRO, RLO
        c is >= '\u2066' and <= '\u2069' ||   // LRI, RLI, FSI, PDI
        c is >= '\u200B' and <= '\u200F';     // ZWSP, ZWNJ, ZWJ, LRM, RLM

    /// <summary>
    /// Whether <paramref name="relative"/>, resolved against <paramref name="root"/>, would land
    /// outside it. The second layer behind <see cref="IsSafeEntryName"/>, for whole relative paths
    /// rather than single names.
    ///
    /// **Fails closed**: any malformed input that makes the comparison itself throw is reported as
    /// escaping, matching <see cref="Archives.ArchiveSafety"/>. A guard that answers "safe" when it
    /// doesn't understand the question is not a guard.
    /// </summary>
    public static bool EscapesRoot(string root, string relative)
    {
        try
        {
            var normalizedRelative = Normalize(relative);
            if (normalizedRelative.Length == 0) return false;
            if (normalizedRelative.Length > MaxPathLength) return true;

            foreach (var segment in normalizedRelative.Split('/'))
            {
                // An empty segment comes from "a//b" - harmless in itself, but it means the caller
                // built the path by concatenation without normalizing, so the rest is not trusted.
                if (segment.Length == 0) return true;
                if (!IsSafeEntryName(segment)) return true;
            }

            // IsSafeEntryName already rejects ".." per segment, so a relative path that passes
            // the loop above cannot escape the root. No further prefix check is needed — a
            // string constructed as root + "/" + relative always starts with root by definition.
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Whether a whole remote path is well-formed and safe to act on: a known shape, a
    /// host, a length within bounds, and every segment acceptable.</summary>
    public static bool IsWellFormed(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.Length > MaxPathLength) return false;
        if (SchemeOf(path) is null) return false;

        var host = HostOf(path);
        if (host.Length == 0) return false;
        foreach (var c in host)
        {
            if (char.IsControl(c) || c is '/' or '\\' or '|') return false;

            // '@' is rejected because it introduces the userinfo component: dav://user:pass@host
            // is a syntactically valid URL that would put a password into every path string, log
            // line and tooltip in the app. Credentials live in the protected profile store and
            // are never part of a path. ':' is deliberately still allowed - it separates the port.
            if (c is '@') return false;
        }

        var inner = PathOf(path);
        if (inner.Length == 0) return true;

        foreach (var segment in inner.Split('/'))
        {
            if (!IsSafeEntryName(segment)) return false;
        }
        return true;
    }
}
