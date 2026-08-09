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

    /// <summary>Every registered format, in registration order - for reporting what this build
    /// actually supports (the About dialog) rather than for lookup, which the By*/From* members
    /// below do more precisely.</summary>
    public static IEnumerable<IArchiveFormat> Registered => _formats;

    /// <summary>Returns all registered formats that support archive creation (<see cref="ArchiveCapabilities.Create"/>).</summary>
    public static IEnumerable<IArchiveFormat> Creatable =>
        _formats.Where(f => f.Capabilities.HasFlag(ArchiveCapabilities.Create));

    /// <summary>Registers an <see cref="IArchiveFormat"/> so it can be detected by extension, signature, or both.</summary>
    public static void Register(IArchiveFormat format) => _formats.Add(format);

    /// <summary>Finds a registered format by its <see cref="IArchiveFormat.Id"/> (case-insensitive), or <c>null</c> if none match.</summary>
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

    /// <summary>
    /// Reads up to 512 bytes from <paramref name="path"/> and matches them against each registered
    /// format's signature. Returns <c>null</c> on I/O errors or when no format recognizes the header.
    /// </summary>
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

    /// <summary>Returns <c>true</c> if <see cref="Detect"/> can identify an archive format for <paramref name="path"/>.</summary>
    public static bool IsSupportedArchiveFile(string path) => Detect(path) is not null;

    /// <summary>
    /// Creates an <see cref="IFileSystem"/> backed by the detected format for <paramref name="archivePath"/>,
    /// or <c>null</c> if the format is not recognized.
    /// </summary>
    public static IFileSystem? CreateFileSystem(string archivePath) =>
        Detect(archivePath)?.CreateFileSystem(archivePath);
}
