using System.Xml.Linq;
using CoderCommander.Archives;
using CoderCommander.Archives.Zip;
using CoderCommander.Viewers.Xml;

namespace CoderCommander.Viewers.Office;

/// <summary>
/// Safe read access to a <c>.docx</c>/<c>.xlsx</c>/<c>.pptx</c>/<c>.odt</c>/<c>.ods</c>/<c>.odp</c>
/// package - every one of these is just a ZIP container (Open Packaging Conventions for OOXML,
/// the ODF package format for OpenDocument), so this wraps the existing
/// <see cref="ZipArchiveFormat"/>/<see cref="IArchiveReader"/> rather than adding any new ZIP code.
/// <see cref="OpenAsync"/> is the one gate every converter goes through before touching a single
/// part - it enforces every <see cref="OfficeLimits"/> ceiling from the central directory alone,
/// before anything is decompressed, and rejects any entry name <see cref="ArchiveSafety.EscapesTarget"/>
/// would refuse to extract.
/// </summary>
internal sealed class OfficePackage : IDisposable
{
    private readonly IArchiveReader _reader;
    private readonly Dictionary<string, ArchiveEntryRecord> _entriesByName;

    private OfficePackage(IArchiveReader reader, Dictionary<string, ArchiveEntryRecord> entriesByName)
    {
        _reader = reader;
        _entriesByName = entriesByName;
    }

    /// <summary><paramref name="localPath"/> must already be a real, on-disk path - callers on a
    /// non-local <see cref="ViewerSource"/> materialize first (see each Office format's loader),
    /// the same requirement <c>ZipArchiveReader</c> itself already has.</summary>
    public static async Task<OfficePackage> OpenAsync(string localPath, CancellationToken ct)
    {
        var reader = ZipArchiveFormat.Instance.OpenRead(localPath);
        try
        {
            var dir = await reader.ReadDirectoryAsync(ct).ConfigureAwait(false);
            if (!dir.IsValid)
                throw new InvalidDataException("Not a valid OOXML/ODF package.");
            if (dir.Entries.Count > OfficeLimits.MaxEntries)
                throw new InvalidDataException("Package has too many parts.");

            var entriesByName = new Dictionary<string, ArchiveEntryRecord>(StringComparer.Ordinal);
            long totalUncompressed = 0;
            foreach (var entry in dir.Entries)
            {
                if (entry.IsDirectory) continue;

                var name = NormalizeName(entry.FullName);
                if (ArchiveSafety.EscapesTarget(name))
                    throw new InvalidDataException("Package contains an unsafe entry name.");

                if (entry.PackedSize > 0 && entry.Size / (double)entry.PackedSize > OfficeLimits.MaxCompressionRatio)
                    throw new InvalidDataException("Package failed a compression-ratio safety check.");

                totalUncompressed += entry.Size;
                if (totalUncompressed > OfficeLimits.MaxTotalUncompressedBytes)
                    throw new InvalidDataException("Package is too large to open.");

                entriesByName[name] = entry;
            }

            return new OfficePackage(reader, entriesByName);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private static string NormalizeName(string fullName) => fullName.Replace('\\', '/').TrimStart('/');

    public bool HasEntry(string name) => _entriesByName.ContainsKey(NormalizeName(name));

    /// <summary>Reads and parses one XML part through <see cref="SafeXml"/>. Returns null if the
    /// part doesn't exist (a legitimately optional part, e.g. no <c>numbering.xml</c> when a
    /// document has no numbered lists) - callers treat that as "nothing to contribute", not an
    /// error.</summary>
    public XDocument? ReadXml(string entryName)
    {
        var name = NormalizeName(entryName);
        if (!_entriesByName.TryGetValue(name, out var entry)) return null;
        if (entry.Size > OfficeLimits.MaxPartBytes)
            throw new InvalidDataException($"Part '{name}' exceeds the size limit.");

        using var stream = _reader.OpenEntry(entry);
        return SafeXml.LoadSafe(stream);
    }

    /// <summary>Reads one part's raw bytes (embedded images), capped at
    /// <paramref name="maxBytes"/> - returns null both when the part is missing and when it's over
    /// budget, since both cases mean "the caller gets no image here", not two different errors.</summary>
    public async Task<byte[]?> ReadBytesAsync(string entryName, long maxBytes, CancellationToken ct)
    {
        var name = NormalizeName(entryName);
        if (!_entriesByName.TryGetValue(name, out var entry)) return null;
        if (entry.Size > maxBytes) return null;

        using var stream = _reader.OpenEntry(entry);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>
    /// Resolves an OPC relationship <c>Target</c> (from a <c>_rels/*.rels</c> part) against the
    /// directory containing the part that referenced it - per the Open Packaging Conventions spec,
    /// a relative target is relative to that directory, not to the <c>_rels</c> folder itself or
    /// the package root. Walks <c>..</c>/<c>.</c> segments by hand rather than via
    /// <see cref="Path"/> (which would use the OS path separator and OS traversal rules for what
    /// is a "/"-only, in-archive path) and returns null the moment a target would resolve above the
    /// package root or reference an external (http/https) location - the same "fail closed, don't
    /// throw a raw exception into the caller" shape <see cref="ArchiveSafety.EscapesRoot"/> uses.
    /// </summary>
    public static string? ResolveRelationshipTarget(string referencingPartName, string target)
    {
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return null;

        var baseDir = NormalizeName(referencingPartName);
        var slash = baseDir.LastIndexOf('/');
        baseDir = slash >= 0 ? baseDir[..slash] : "";

        var startSegments = target.StartsWith('/')
            ? Array.Empty<string>()
            : (baseDir.Length == 0 ? Array.Empty<string>() : baseDir.Split('/'));

        var segments = new List<string>(startSegments);
        foreach (var seg in target.TrimStart('/').Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..")
            {
                if (segments.Count == 0) return null; // would escape the package root
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                if (seg.Contains(':', StringComparison.Ordinal)) return null;
                segments.Add(seg);
            }
        }

        return string.Join('/', segments);
    }

    public void Dispose() => _reader.Dispose();
}
