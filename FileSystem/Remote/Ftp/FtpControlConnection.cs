using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote.Ftp;

/// <summary>
/// One FTP control connection: the socket, the TLS state, and the command/reply conversation on it.
///
/// <para><b>Strictly one conversation at a time.</b> FTP has no request ids - a reply belongs to
/// whatever command was sent last - so two callers sharing a connection would read each other's
/// replies and stay one step out of phase for the rest of the session. Nothing here serialises
/// access; <see cref="FtpConnectionPool"/> does, by handing out a connection to one caller at a
/// time.</para>
///
/// <para><b>Passive mode only.</b> Active mode requires the client to listen for an inbound
/// connection, which fails behind any ordinary firewall or NAT and means accepting a connection
/// from whoever arrives first.</para>
///
/// <para><b>Written against RFC 959, with RFC 2228/4217 for TLS, RFC 2428 for EPSV and RFC 3659 for
/// MLSD.</b> <c>FtpWebRequest</c> is not used because it is obsolete in .NET (SYSLIB0014) and has
/// no TLS-with-certificate-pinning story at all.</para>
/// </summary>
internal sealed class FtpControlConnection : IDisposable
{
    /// <summary>Default when the server does not advertise UTF-8. Latin-1 maps every byte to a
    /// character and back unchanged, so a name in an unknown encoding survives the round trip
    /// instead of being replaced with question marks.</summary>
    private static readonly Encoding Latin1 = Encoding.Latin1;

    private readonly string _host;
    private readonly int _port;
    private readonly ConnectionProfile _profile;
    private readonly string? _password;
    private readonly bool _requireTls;
    private readonly RemoteCertificateValidationCallback _certificateValidator;

    private TcpClient? _client;
    private Stream? _stream;
    private Encoding _encoding = Latin1;
    private readonly HashSet<string> _features = new(StringComparer.OrdinalIgnoreCase);
    private bool _protectData;

    private readonly byte[] _readBuffer = new byte[4096];
    private int _readOffset;
    private int _readLength;

    public FtpControlConnection(string host, int port, ConnectionProfile profile, string? password, bool requireTls)
    {
        _host = host;
        _port = port;
        _profile = profile;
        _password = password;
        _requireTls = requireTls;
        _certificateValidator = RemoteTls.MakeCertificateValidator(profile, "FTP");
    }

    /// <summary>Whether the server advertised MLSD, whose output needs no shape-matching.</summary>
    public bool SupportsMlsd => _features.Contains("MLSD");

    /// <summary>Whether the server advertised MLST, the unambiguous "does this exist" command.</summary>
    public bool SupportsMlst => _features.Any(f => f.StartsWith("MLST", StringComparison.OrdinalIgnoreCase));

    /// <summary>A connection that has been torn down by an error is not put back in the pool.</summary>
    public bool IsUsable => _client is { Connected: true } && _stream is not null;

    /// <summary>When this connection last carried a command. The pool uses it to decide whether an
    /// idle connection is worth pinging before it is handed out again - see
    /// <see cref="FtpConnectionPool"/>.</summary>
    public DateTime LastUsedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Clears any buffered response data left from a cancelled read, so the next
    /// command doesn't read stale bytes from a previous reply. Called by the pool on Return.</summary>
    public void ResetReadBuffer()
    {
        _readOffset = 0;
        _readLength = 0;
    }

    // ── Session setup ───────────────────────────────────────────────────────────────────────

    public async Task OpenAsync(CancellationToken ct)
    {
        _client = new TcpClient { NoDelay = true };

        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(RemoteLimits.ConnectTimeout);
            try
            {
                await _client.ConnectAsync(_host, _port, connectCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new IOException($"FTP: {_host}:{_port} did not answer within {RemoteLimits.ConnectTimeout.TotalSeconds:0} s");
            }
        }

        // Bounds the one write this class performs synchronously - the courtesy QUIT in Dispose.
        // Every other exchange is async and bounded by its own linked token instead, because
        // Socket.SendTimeout/ReceiveTimeout are ignored by the asynchronous methods.
        _client.SendTimeout = (int)RemoteLimits.ConnectTimeout.TotalMilliseconds;

        _stream = _client.GetStream();

        var greeting = await WithTimeoutAsync(ReadReplyAsync, ct).ConfigureAwait(false);
        if (greeting.Code != 220)
            throw new IOException($"FTP: unexpected greeting: {greeting}");

        await LoadFeaturesAsync(ct).ConfigureAwait(false);
        await NegotiateTlsAsync(ct).ConfigureAwait(false);
        await NegotiateEncodingAsync(ct).ConfigureAwait(false);
        await LoginAsync(ct).ConfigureAwait(false);

        // Binary. The default is ASCII, which rewrites line endings mid-transfer and corrupts every
        // file that is not text - silently, because the transfer still reports success.
        var type = await SendAsync("TYPE I", ct).ConfigureAwait(false);
        if (!type.IsSuccess)
            throw new IOException($"FTP: server refused binary mode: {type}");
    }

    private async Task LoadFeaturesAsync(CancellationToken ct)
    {
        var reply = await SendAsync("FEAT", ct).ConfigureAwait(false);
        if (reply.Code != 211) return;   // pre-RFC-2389 server: no features, everything falls back

        foreach (var line in reply.Text.Split('\n'))
        {
            var feature = line.Trim();
            if (feature.Length == 0) continue;
            // The first and last lines are the human-readable frame ("Extensions supported:").
            if (feature.StartsWith("Extensions", StringComparison.OrdinalIgnoreCase)) continue;
            if (feature.StartsWith("End", StringComparison.OrdinalIgnoreCase)) continue;
            _features.Add(feature);
        }
    }

    /// <summary>
    /// Explicit FTPS (RFC 4217): upgrade the existing plaintext connection with <c>AUTH TLS</c>.
    ///
    /// <para>Explicit rather than implicit, because implicit FTPS was never standardised and its
    /// conventional port 990 is not what a server configured today listens on.</para>
    ///
    /// <para>TLS is always attempted. When the server has none, an <c>ftps://</c> profile fails and
    /// an <c>ftp://</c> one continues in the clear with a warning - the same shape as WebDAV over
    /// plain HTTP. Downgrading silently would be worse than either.</para>
    /// </summary>
    private async Task NegotiateTlsAsync(CancellationToken ct)
    {
        var offersTls = _features.Contains("AUTH TLS") || _features.Contains("AUTH SSL") || _features.Contains("AUTH");

        if (!offersTls)
        {
            if (_requireTls)
                throw new IOException($"FTP: {_host} does not offer TLS, and this connection requires it");

            LogService.Warning($"FTP: {_host} offers no TLS; the password and every file are sent unencrypted");
            return;
        }

        var auth = await SendAsync("AUTH TLS", ct).ConfigureAwait(false);
        if (!auth.IsSuccess)
        {
            if (_requireTls)
                throw new IOException($"FTP: server refused AUTH TLS: {auth}");

            LogService.Warning($"FTP: {_host} refused AUTH TLS; continuing unencrypted");
            return;
        }

        var ssl = new SslStream(_stream!, leaveInnerStreamOpen: false, _certificateValidator);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = _host,
            RemoteCertificateValidationCallback = _certificateValidator,
        }, ct).ConfigureAwait(false);
        _stream = ssl;

        // PBSZ must precede PROT and, for TLS, its only legal argument is 0 (RFC 4217 §9).
        await SendAsync("PBSZ 0", ct).ConfigureAwait(false);

        var prot = await SendAsync("PROT P", ct).ConfigureAwait(false);
        if (prot.IsSuccess)
        {
            _protectData = true;
        }
        else if (_requireTls)
        {
            throw new IOException($"FTP: server refused to protect the data channel: {prot}");
        }
        else
        {
            // The control channel - and therefore the password - is still encrypted; only file
            // contents are not. Worth saying out loud rather than leaving to be discovered.
            LogService.Warning($"FTP: {_host} refused PROT P; file contents are sent unencrypted");
        }
    }

    /// <summary>RFC 2640: a server advertising UTF8 gets UTF-8, everything else keeps Latin-1.
    /// Guessing UTF-8 for a server that does not speak it turns every non-ASCII name into
    /// replacement characters, and those names then fail to address the file they came from.</summary>
    private async Task NegotiateEncodingAsync(CancellationToken ct)
    {
        if (!_features.Contains("UTF8")) return;

        var reply = await SendAsync("OPTS UTF8 ON", ct).ConfigureAwait(false);
        if (reply.IsSuccess) _encoding = new UTF8Encoding(false);
    }

    private async Task LoginAsync(CancellationToken ct)
    {
        var anonymous = string.IsNullOrEmpty(_profile.UserName);
        var user = anonymous ? "anonymous" : _profile.UserName;

        var userReply = await SendAsync($"USER {user}", ct).ConfigureAwait(false);

        // 230 means no password was needed; 331 means send one.
        if (userReply.Code == 230) return;
        if (userReply.Code != 331)
            throw new IOException($"FTP: login refused: {userReply}");

        // An anonymous login conventionally sends an e-mail address as the password. A named account
        // with no stored password sends an empty one, which the server refuses with a message that
        // says so - better than sending "anonymous@" on its behalf and getting a refusal that looks
        // like the account is wrong.
        var secret = _password ?? (anonymous ? "anonymous@" : "");

        // The password is the one string that must never reach the log, so PASS is sent through
        // the path that does not log its own command text.
        var passReply = await SendCommandAsync($"PASS {secret}", logAs: "PASS ****", ct)
            .ConfigureAwait(false);

        if (passReply.Code == 332)
            throw new IOException("FTP: the server wants an account name (ACCT), which is not supported");
        if (!passReply.IsSuccess)
            throw new IOException($"FTP: login refused: {passReply}");
    }

    // ── Commands ────────────────────────────────────────────────────────────────────────────

    public Task<FtpReply> SendAsync(string command, CancellationToken ct) =>
        SendCommandAsync(command, command, ct);

    private async Task<FtpReply> SendCommandAsync(string command, string logAs, CancellationToken ct)
    {
        // The single most important check in this file. FTP commands are newline-delimited with no
        // escaping, so a path holding a CR or LF - which a server is perfectly able to put in a
        // listing - would end the command early and inject whatever follows as a new one. The
        // listing parser already rejects such names; this is the second layer, on the path that
        // every command without exception goes through.
        if (command.Contains('\r', StringComparison.Ordinal) || command.Contains('\n', StringComparison.Ordinal))
            throw new ArgumentException("FTP command may not contain a line break", nameof(command));

        LogService.Debug($"FTP > {logAs}");
        LastUsedUtc = DateTime.UtcNow;

        return await WithTimeoutAsync(async token =>
        {
            var bytes = _encoding.GetBytes(command + "\r\n");
            await _stream!.WriteAsync(bytes, token).ConfigureAwait(false);
            await _stream.FlushAsync(token).ConfigureAwait(false);

            return await ReadReplyAsync(token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Bounds one exchange with <see cref="RemoteLimits.RequestTimeout"/>.
    ///
    /// <para>Without this a server that accepts the connection and then goes silent - which is what
    /// a half-closed socket, an overloaded server or a firewall dropping the flow all look like -
    /// blocks the caller forever. There is no equivalent of <c>HttpClient.Timeout</c> here to fall
    /// back on: sockets have <c>ReceiveTimeout</c>, but it is documented as having no effect on the
    /// asynchronous methods, so the bound has to come from a token.</para>
    ///
    /// <para>A timeout is reported as an <see cref="IOException"/> rather than as cancellation,
    /// because the caller's own token was not cancelled and treating it as "the user cancelled"
    /// would make the operation disappear without a word.</para>
    /// </summary>
    private static async Task<T> WithTimeoutAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(RemoteLimits.RequestTimeout);

        try
        {
            return await body(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new IOException($"FTP: the server stopped responding (no reply within {RemoteLimits.RequestTimeout.TotalSeconds:0} s)");
        }
    }

    /// <summary>
    /// Reads one complete reply, following the multi-line rules of RFC 959 §4.2.
    ///
    /// A reply is either one <c>NNN&lt;space&gt;text</c> line, or <c>NNN-</c> followed by anything
    /// at all until a line repeating that same code with a space. "Anything at all" includes lines
    /// that themselves begin with three digits, which is why the closing code has to match.
    /// </summary>
    private async Task<FtpReply> ReadReplyAsync(CancellationToken ct)
    {
        var text = new StringBuilder();
        var first = await ReadLineAsync(ct).ConfigureAwait(false);

        if (FtpReplyParser.IsTerminalLine(first, 0, out var singleCode))
            return new FtpReply(singleCode, FtpReplyParser.StripCode(first));

        var openingCode = FtpReplyParser.MultilineOpeningCode(first);
        if (openingCode == 0)
            return new FtpReply(0, first);   // unparseable: hand it back rather than guess

        text.Append(FtpReplyParser.StripCode(first));

        for (var lines = 0; lines < RemoteLimits.MaxControlReplyLines; lines++)
        {
            var line = await ReadLineAsync(ct).ConfigureAwait(false);
            if (FtpReplyParser.IsTerminalLine(line, openingCode, out var code))
            {
                text.Append('\n').Append(FtpReplyParser.StripCode(line));
                return new FtpReply(code, text.ToString());
            }
            text.Append('\n').Append(line);
        }

        throw new IOException($"FTP: reply did not end within {RemoteLimits.MaxControlReplyLines} lines");
    }

    /// <summary>One CRLF-terminated line, decoded with the negotiated encoding. Bounded, because the
    /// protocol carries no length and a server that never sends a newline must not grow a buffer
    /// without limit.</summary>
    private async Task<string> ReadLineAsync(CancellationToken ct)
    {
        var line = new List<byte>(128);

        while (true)
        {
            if (_readOffset >= _readLength)
            {
                _readLength = await _stream!.ReadAsync(_readBuffer, ct).ConfigureAwait(false);
                _readOffset = 0;
                if (_readLength <= 0)
                    throw new IOException("FTP: the server closed the control connection");
            }

            var b = _readBuffer[_readOffset++];
            if (b == (byte)'\n')
            {
                if (line.Count > 0 && line[^1] == (byte)'\r') line.RemoveAt(line.Count - 1);
                return _encoding.GetString(line.ToArray());
            }

            line.Add(b);
            if (line.Count > RemoteLimits.MaxControlLineLength)
                throw new IOException($"FTP: reply line exceeded {RemoteLimits.MaxControlLineLength} bytes");
        }
    }

    // ── Data connections ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a passive data connection and starts <paramref name="command"/> on it.
    ///
    /// The order matters and is not the obvious one: the data connection is opened <i>before</i> the
    /// transfer command is sent, because in passive mode the server is already listening and expects
    /// the client to arrive. Sending the command first leaves a window in which the server may
    /// report the transfer complete before the client has connected.
    /// </summary>
    public async Task<Stream> OpenDataStreamAsync(string command, CancellationToken ct)
    {
        var (dataClient, dataStream) = await ConnectDataAsync(ct).ConfigureAwait(false);

        try
        {
            var reply = await SendAsync(command, ct).ConfigureAwait(false);

            // 1xx is the preliminary "here it comes"; 2xx means the whole transfer already finished
            // (an empty directory, typically) and no final reply will follow.
            if (reply.Code is < 100 or >= 300)
                throw new IOException($"FTP: {command.Split(' ')[0]} refused: {reply}");

            // The data channel's TLS handshake happens only now: the server starts its side after it
            // has accepted the transfer command, so handshaking earlier deadlocks against a peer that
            // is not yet listening for one.
            if (_protectData)
                dataStream = await AuthenticateDataAsync(dataStream, ct).ConfigureAwait(false);

            return new FtpDataStream(this, dataClient, dataStream, expectFinalReply: reply.Code < 200);
        }
        catch
        {
            dataStream.Dispose();
            dataClient.Dispose();
            throw;
        }
    }

    private async Task<(TcpClient, Stream)> ConnectDataAsync(CancellationToken ct)
    {
        var port = await NegotiatePassivePortAsync(ct).ConfigureAwait(false);

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(RemoteLimits.ConnectTimeout);

            // Always _host - never the address the server named in its own reply. See
            // FtpReplyParser.ParsePasvPort for why.
            await client.ConnectAsync(_host, port, connectCts.Token).ConfigureAwait(false);
            return (client, client.GetStream());
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>EPSV first (RFC 2428): it carries no address at all, so it works unchanged behind
    /// NAT and over IPv6. PASV is the fallback for servers that predate it.</summary>
    private async Task<int> NegotiatePassivePortAsync(CancellationToken ct)
    {
        var epsv = await SendAsync("EPSV", ct).ConfigureAwait(false);
        if (epsv.Code == 229)
        {
            var port = FtpReplyParser.ParseEpsvPort(epsv.Text);
            if (port > 0) return port;
        }

        var pasv = await SendAsync("PASV", ct).ConfigureAwait(false);
        if (pasv.Code == 227)
        {
            var port = FtpReplyParser.ParsePasvPort(pasv.Text);
            if (port > 0) return port;
        }

        throw new IOException($"FTP: could not open a data connection (EPSV: {epsv.Code}, PASV: {pasv.Code})");
    }

    private async Task<Stream> AuthenticateDataAsync(Stream dataStream, CancellationToken ct)
    {
        var ssl = new SslStream(dataStream, leaveInnerStreamOpen: false, _certificateValidator);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _host,
                RemoteCertificateValidationCallback = _certificateValidator,
            }, ct).ConfigureAwait(false);
            return ssl;
        }
        catch (Exception ex)
        {
            ssl.Dispose();
            // The common cause is a server configured to require the data connection to resume the
            // control connection's TLS session. .NET exposes no way to do that - SslStream has no
            // session-reuse API - so this is a real limitation and saying so beats a bare handshake
            // error the user cannot act on.
            throw new IOException(
                "FTP: the TLS handshake on the data connection failed. Some servers require it to " +
                "reuse the control connection's TLS session, which this client cannot do.", ex);
        }
    }

    /// <summary>
    /// Reads the reply that follows a completed transfer (226/250). Called by
    /// <see cref="FtpDataStream"/> once the data connection is closed - and only then, because the
    /// server does not send it until it sees the end of the data.
    ///
    /// <para>Bounded like every other exchange. This one matters most: it is awaited from a
    /// <c>Dispose</c>, so a server that never sends its verdict would hang the thread closing the
    /// stream - typically the one running the copy - with no token anywhere to cancel it.</para>
    /// </summary>
    internal Task<FtpReply> ReadTransferResultAsync(CancellationToken ct) =>
        WithTimeoutAsync(ReadReplyAsync, ct);

    /// <summary>Reads a whole data connection as lines - used for listings, which are small and
    /// have to be parsed as a unit anyway.</summary>
    public async Task<IReadOnlyList<string>> ReadLinesAsync(string command, CancellationToken ct)
    {
        var lines = new List<string>();
        var data = (FtpDataStream)await OpenDataStreamAsync(command, ct).ConfigureAwait(false);

        await using (data.ConfigureAwait(false))
        {
            using var reader = new StreamReader(data, _encoding, detectEncodingFromByteOrderMarks: false);

            var total = 0L;
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                total += line.Length;
                if (total > RemoteLimits.MaxListingBytes)
                {
                    LogService.Warning("FTP: listing exceeded the size limit and was truncated");

                    // Walking away from a half-read data connection makes the server answer 426
                    // rather than 226. That is the expected outcome of our own decision to stop, so
                    // it must not be raised as a failure - the caller asked for a bounded listing
                    // and is getting one.
                    data.AbortExpected = true;
                    break;
                }
                if (line.Length > 0) lines.Add(line);
            }
        }

        return lines;
    }

    public void Dispose()
    {
        try
        {
            // Best-effort courtesy: a server that is told QUIT frees the session immediately instead
            // of waiting for its idle timeout. Failure here is uninteresting by definition.
            if (IsUsable)
            {
                var bytes = _encoding.GetBytes("QUIT\r\n");
                _stream!.Write(bytes, 0, bytes.Length);
                _stream.Flush();
            }
        }
        catch
        {
            // The connection is going away regardless.
        }

        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }
}
