namespace CoderCommander.Models;

/// <summary>
/// A saved remote connection, as the user configured it.
///
/// Shaped for WebDAV from the outset - base URL, user name, authentication choice, TLS trust
/// decision, auto-connect - so the protocol step slots in without reworking the model or migrating
/// anyone's settings. FTP and SFTP need the same fields.
///
/// **There is deliberately no password property.** Profiles are serialised into
/// <c>settings.json</c> in the clear; a secret must never end up there, no matter how the file is
/// later copied, backed up or attached to a bug report. Passwords live in
/// <see cref="Services.CredentialStore"/>, keyed by <see cref="Id"/>.
/// </summary>
public sealed class ConnectionProfile
{
    /// <summary>Stable identity, and the key to this profile's password. A GUID rather than the
    /// name because the name is expected to change, and a renamed profile must not lose - or worse,
    /// silently inherit - a stored secret.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Caption shown in the places bar.</summary>
    public string Name { get; set; } = "";

    /// <summary>Provider scheme, e.g. <c>"dav"</c>. Matched against
    /// <c>IFileSystemProvider.Scheme</c>.</summary>
    public string Scheme { get; set; } = "";

    /// <summary>Base address including the root path, e.g. <c>https://example.com/remote.php/dav</c>.
    /// Stored as the user typed it (an ordinary URL), not as an app-internal
    /// <see cref="FileSystem.RemotePath"/> string - the two serve different purposes and conflating
    /// them is how a display form ends up being parsed as a path.</summary>
    public string Url { get; set; } = "";

    /// <summary>Empty means anonymous.</summary>
    public string UserName { get; set; } = "";

    /// <summary>Whether the password may be written to the protected store at all. Default is
    /// <c>false</c>: storing a secret is an explicit choice, and the alternative (asking at connect
    /// time) is always available.</summary>
    public bool SavePassword { get; set; }

    /// <summary>Connect on startup, without asking. Only meaningful together with
    /// <see cref="SavePassword"/> or an anonymous connection - otherwise there is nothing to
    /// connect with and the attempt would just pop a prompt at launch.</summary>
    public bool AutoConnect { get; set; }

    /// <summary>
    /// SHA-256 thumbprint of a self-signed or otherwise untrusted certificate the user explicitly
    /// accepted for this host, or empty to require a normally-trusted chain.
    ///
    /// Pinning one specific certificate is the whole point: the alternative people reach for -
    /// a validation callback that returns <c>true</c> - disables the check for every host and every
    /// certificate, permanently, and is indistinguishable from having no TLS at all.
    /// </summary>
    public string AcceptedCertificateThumbprint { get; set; } = "";

    /// <summary>Deep copy, so an edit dialog can work on a scratch instance and discard it on
    /// Cancel without the caller having to re-read from disk.</summary>
    public ConnectionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Scheme = Scheme,
        Url = Url,
        UserName = UserName,
        SavePassword = SavePassword,
        AutoConnect = AutoConnect,
        AcceptedCertificateThumbprint = AcceptedCertificateThumbprint,
    };

    /// <summary>Caption for the places bar: the name when set, otherwise something recognisable
    /// rather than an empty button.</summary>
    public string DisplayName => Name.Length > 0 ? Name : Url;

    public override string ToString() => $"ConnectionProfile({Scheme}, {DisplayName})";
}
