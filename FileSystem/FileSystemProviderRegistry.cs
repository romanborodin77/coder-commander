namespace CoderCommander.FileSystem;

/// <summary>
/// Process-wide registry of remote filesystem providers, populated once at startup from
/// <c>Program.cs</c> - the same shape and lifetime as
/// <see cref="Archives.ArchiveFormatRegistry"/>, so there is one way to answer "what can serve
/// this?" in the codebase rather than two competing ones.
///
/// Lookup is by scheme only. There is no signature sniffing here, unlike the archive registry:
/// an archive has to be recognised from bytes on disk, whereas a remote path always carries its
/// scheme, and guessing would mean contacting a server to find out what it is.
/// </summary>
public static class FileSystemProviderRegistry
{
    private static readonly List<IFileSystemProvider> Providers = new();

    /// <summary>Everything registered, in registration order - for the connection editor's type
    /// list and for reporting what this build supports.</summary>
    public static IEnumerable<IFileSystemProvider> Registered => Providers;

    /// <summary>Registers a provider. Called from <c>Program.cs</c> at startup; a duplicate scheme
    /// replaces the earlier registration rather than shadowing it, so a test can substitute one
    /// without the original silently winning.</summary>
    public static void Register(IFileSystemProvider provider)
    {
        Providers.RemoveAll(p => string.Equals(p.Scheme, provider.Scheme, StringComparison.OrdinalIgnoreCase));
        Providers.Add(provider);
    }

    /// <summary>Provider for a scheme, or <c>null</c> when nothing serves it.</summary>
    public static IFileSystemProvider? ByScheme(string? scheme) =>
        string.IsNullOrEmpty(scheme)
            ? null
            : Providers.FirstOrDefault(p => string.Equals(p.Scheme, scheme, StringComparison.OrdinalIgnoreCase));

    /// <summary>Provider that serves <paramref name="path"/>, or <c>null</c> when the path isn't
    /// remote or its scheme is unknown.</summary>
    public static IFileSystemProvider? ForPath(string? path) => ByScheme(RemotePath.SchemeOf(path));

    /// <summary><c>true</c> when <paramref name="path"/> is a remote path this build can actually
    /// serve. Checked before <see cref="VfsPath.IsArchive"/> when classifying a path, so a remote
    /// path is never handed to the archive machinery.</summary>
    public static bool IsSupportedRemotePath(string? path) => ForPath(path) is not null;

    /// <summary>Drops every registration. Exists for tests, which must not inherit providers
    /// registered by another test - the archive registry's lack of this is a known nuisance.</summary>
    internal static void ResetForTests() => Providers.Clear();
}
