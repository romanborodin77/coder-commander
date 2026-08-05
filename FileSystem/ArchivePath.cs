namespace CoderCommander.FileSystem;

/// <summary>
/// Format-neutral virtual-path arithmetic for paths of the form
/// <c>C:\dir\file.zip|inner/dir/name</c>. Previously lived on <see cref="ZipArchiveFileSystem"/>
/// as ZIP-specific statics; the "<c>|</c>-separated host-path + inner-path" convention itself
/// isn't ZIP-specific, so it moved here where any archive format's file system can use it.
/// <see cref="ZipArchiveFileSystem"/> keeps <c>[Obsolete]</c> one-line forwarders to these for
/// source compatibility.
/// </summary>
public static class ArchivePath
{
    /// <summary>Separator character between host path and inner path in virtual archive paths.</summary>
    public const char Separator = '|';

    /// <summary>Combines an archive path and inner path into a single virtual path.</summary>
    public static string MakePath(string archivePath, string innerPath)
    {
        var normalized = innerPath.Replace('\\', '/');
        return $"{archivePath}{Separator}{normalized}";
    }

    /// <summary>Splits a virtual path into its archive path and inner path components.</summary>
    public static (string archivePath, string innerPath) SplitPath(string fullPath)
    {
        var idx = fullPath.IndexOf(Separator);
        return idx < 0 ? (fullPath, "") : (fullPath[..idx], fullPath[(idx + 1)..]);
    }

    /// <summary>Returns true when <paramref name="path"/> is a virtual archive path (contains the <see cref="Separator"/>).</summary>
    public static bool IsArchivePath(string path) => path.Contains(Separator);
}
