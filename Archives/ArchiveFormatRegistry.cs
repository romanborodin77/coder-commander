using CoderCommander.FileSystem;

namespace CoderCommander.Archives;

/// <summary>
/// Process-wide registry of known archive formats, populated once at startup (see
/// <c>Program.cs</c>). Everything that needs to go from a file path to "what kind of archive is
/// this and what can I do with it" goes through here instead of checking extensions/signatures
/// itself.
/// </summary>
public static class ArchiveFormatRegistry
{
    private const int SignatureProbeSize = 512;

    private static readonly List<IArchiveFormat> _formats = new();

    public static IEnumerable<IArchiveFormat> Creatable =>
        _formats.Where(f => f.Capabilities.HasFlag(ArchiveCapabilities.Create));

    public static void Register(IArchiveFormat format) => _formats.Add(format);

    public static IArchiveFormat? ById(string id) =>
        _formats.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Matches by longest registered extension suffix, so ".tar.gz" wins over ".gz".</summary>
    public static IArchiveFormat? FromExtension(string path)
    {
        IArchiveFormat? best = null;
        var bestLength = 0;

        foreach (var format in _formats)
        {
            foreach (var extension in format.Extensions)
            {
                if (extension.Length > bestLength && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    best = format;
                    bestLength = extension.Length;
                }
            }
        }

        return best;
    }

    public static IArchiveFormat? FromSignature(string path)
    {
        Span<byte> header = stackalloc byte[SignatureProbeSize];

        int read;
        try
        {
            using var stream = File.OpenRead(path);
            read = stream.Read(header);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var probe = header[..read];
        foreach (var format in _formats)
        {
            if (format.MatchesSignature(probe))
                return format;
        }

        return null;
    }

    /// <summary>Extension first, signature as fallback for files whose extension isn't recognized
    /// (or has none) - preserves the existing behavior of opening e.g. a signature-matched .docx
    /// as ZIP when Enter is pressed on it.</summary>
    public static IArchiveFormat? Detect(string path) =>
        FromExtension(path) ?? FromSignature(path);

    public static bool IsSupportedArchiveFile(string path) => Detect(path) is not null;

    public static IFileSystem? CreateFileSystem(string archivePath) =>
        Detect(archivePath)?.CreateFileSystem(archivePath);
}
