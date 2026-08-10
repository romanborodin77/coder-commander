namespace CoderCommander.FileSystem.Remote;

/// <summary>
/// Every bound a remote provider is held to, in one file - the same arrangement as
/// <c>Terminal/Vt/VtLimits.cs</c>, and for the same reason: a server is on the other side of a
/// trust boundary, so "how much of this will we accept" must be answerable by reading one screen
/// rather than by auditing call sites.
///
/// The threat model is that everything a server sends may be hostile or simply broken: a listing
/// with a million entries, a response that never ends, a redirect loop, a connection that accepts
/// bytes and then goes silent forever. None of those may hang the app or exhaust its memory.
/// </summary>
public static class RemoteLimits
{
    /// <summary>How long a single request may take end to end. Deliberately not
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>, which is what
    /// <c>HttpClient.Timeout</c> effectively becomes once a caller passes a cancellation token and
    /// forgets to cancel it.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Budget for establishing a connection during auto-connect at startup. Shorter than
    /// <see cref="RequestTimeout"/> on purpose: a dead server must not make the app look frozen
    /// while it launches.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Cap on a directory-listing response body. A listing is XML, so a malicious or
    /// runaway server could otherwise stream indefinitely into memory.</summary>
    public const int MaxListingBytes = 16 * 1024 * 1024;

    /// <summary>Cap on entries taken from one listing. Past this the listing is truncated and the
    /// fact is logged - showing part of a directory beats freezing the panel while a hundred
    /// thousand rows are built.</summary>
    public const int MaxEntriesPerDirectory = 50_000;

    /// <summary>Redirects followed before giving up, guarding against a redirect loop.</summary>
    public const int MaxRedirects = 5;

    /// <summary>Buffer used for streaming file bodies. Matches the middle tier of
    /// <c>Utils/BufferSizing</c> - large enough that per-read overhead disappears on a network
    /// stream, small enough to be irrelevant to memory.</summary>
    public const int TransferBufferSize = 1024 * 1024;

    // ── FTP control channel ─────────────────────────────────────────────────────────────────

    /// <summary>Cap on one line of an FTP reply. The protocol is line-based with no length field,
    /// so a server that never sends a newline would otherwise grow a buffer without limit.</summary>
    public const int MaxControlLineLength = 8 * 1024;

    /// <summary>Cap on the lines of one multi-line reply. <c>FEAT</c> legitimately answers with a
    /// few dozen; a server answering with a million is not one worth talking to.</summary>
    public const int MaxControlReplyLines = 1024;

    /// <summary>
    /// Control connections opened per FTP filesystem.
    ///
    /// An FTP control channel is strictly one conversation at a time - a command may not be sent
    /// while a transfer is running on its data connection - so a single connection would make a
    /// panel refresh wait for a download to finish. A handful covers both panels plus a transfer
    /// without turning a file manager into something a server would see as a flood.
    /// </summary>
    public const int MaxFtpControlConnections = 4;
}
