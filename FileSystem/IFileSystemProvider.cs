using CoderCommander.Models;

namespace CoderCommander.FileSystem;

/// <summary>
/// A kind of remote filesystem the app can connect to - WebDAV, FTP, SFTP.
///
/// Deliberately narrow: a provider knows how to turn a <see cref="ConnectionProfile"/> into a live
/// <see cref="IFileSystem"/>, and nothing else. Everything around that - profiles, stored
/// passwords, auto-connect, the places bar, connection state - is protocol-independent and lives
/// outside, so adding the second and third protocol costs one file each rather than another pass
/// over the surrounding machinery.
/// </summary>
public interface IFileSystemProvider
{
    /// <summary>Scheme this provider serves, lowercase, matching
    /// <see cref="RemotePath.SchemeOf"/> - e.g. <c>"dav"</c>.</summary>
    string Scheme { get; }

    /// <summary>Name shown in the connection editor's type list.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Opens a connection.
    ///
    /// <paramref name="password"/> is passed in rather than looked up by the provider, so that the
    /// credential store stays the single place that decides how a secret is obtained (saved, or
    /// prompted for) and providers never touch it. <c>null</c> means anonymous.
    ///
    /// Must honour <paramref name="ct"/>: a server that accepts the TCP connection and then says
    /// nothing would otherwise hang the caller indefinitely, and startup auto-connect runs this
    /// for every configured profile.
    ///
    /// Throws on failure - the caller records it as a failed connection with the message. It must
    /// not return a half-built <see cref="IFileSystem"/>, because everything downstream assumes a
    /// returned provider is usable.
    /// </summary>
    Task<IFileSystem> ConnectAsync(ConnectionProfile profile, string? password, CancellationToken ct);
}
