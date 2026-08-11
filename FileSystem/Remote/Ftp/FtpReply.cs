namespace CoderCommander.FileSystem.Remote.Ftp;

/// <summary>
/// One reply from an FTP control connection: a three-digit code and the text that came with it
/// (RFC 959 §4.2).
/// </summary>
/// <param name="Code">The numeric code, or 0 when the server said something unparseable.</param>
/// <param name="Text">Every line of the reply, newline-joined, with the code prefixes removed.</param>
public readonly record struct FtpReply(int Code, string Text)
{
    /// <summary>2xx and 3xx mean "done" and "send more" respectively; both are the server agreeing.
    /// 1xx is a preliminary reply and is never the final word, so it is not success on its own.</summary>
    public bool IsSuccess => Code is >= 200 and < 400;

    /// <summary>4xx is a transient failure (busy, file locked) and 5xx a permanent one. Worth
    /// distinguishing only where a retry could plausibly help; everywhere else both are failures.</summary>
    public bool IsTransientFailure => Code is >= 400 and < 500;

    public override string ToString() => $"{Code} {Text}";
}

/// <summary>
/// Line-level parsing of the control channel, kept apart from the socket so the awkward shapes -
/// multi-line replies, a code that appears again inside the text, a hyphen where a space belongs -
/// can be tested against real server transcripts without a network.
/// </summary>
public static class FtpReplyParser
{
    /// <summary>
    /// Whether <paramref name="line"/> ends a reply.
    ///
    /// <para>The rule from RFC 959 §4.2 is exact and easy to get subtly wrong: a reply ends with
    /// <c>NNN&lt;space&gt;</c>, while <c>NNN-</c> opens a multi-line reply and every line in between
    /// may be anything at all - including a line that starts with a different three-digit number.
    /// The naive "starts with three digits" test therefore ends the reply early on servers whose
    /// banner contains a version number, and the connection then reads the rest of the banner as
    /// the answer to the next command, one step out of phase from there on.</para>
    /// </summary>
    /// <param name="expectedCode">The code that opened a multi-line reply, or 0 when the reply has
    /// not started yet. A terminating line must repeat it - otherwise a quoted <c>"226 "</c> in the
    /// middle of a message would end the reply.</param>
    public static bool IsTerminalLine(string line, int expectedCode, out int code)
    {
        code = 0;
        if (line.Length < 4) return false;
        if (!char.IsAsciiDigit(line[0]) || !char.IsAsciiDigit(line[1]) || !char.IsAsciiDigit(line[2])) return false;
        if (line[3] != ' ') return false;

        code = (line[0] - '0') * 100 + (line[1] - '0') * 10 + (line[2] - '0');
        return expectedCode == 0 || code == expectedCode;
    }

    /// <summary>The code opening a multi-line reply (<c>NNN-</c>), or 0 when this is not one.</summary>
    public static int MultilineOpeningCode(string line)
    {
        if (line.Length < 4) return 0;
        if (!char.IsAsciiDigit(line[0]) || !char.IsAsciiDigit(line[1]) || !char.IsAsciiDigit(line[2])) return 0;
        if (line[3] != '-') return 0;
        return (line[0] - '0') * 100 + (line[1] - '0') * 10 + (line[2] - '0');
    }

    /// <summary>Strips the leading code and its separator from a reply line, leaving the message.</summary>
    public static string StripCode(string line) =>
        line.Length >= 4 && char.IsAsciiDigit(line[0]) && char.IsAsciiDigit(line[1]) &&
        char.IsAsciiDigit(line[2]) && line[3] is ' ' or '-'
            ? line[4..]
            : line;

    /// <summary>
    /// The port from a <c>227 Entering Passive Mode (h1,h2,h3,h4,p1,p2)</c> reply.
    ///
    /// <para><b>The address in the reply is deliberately not returned.</b> It is whatever the server
    /// believes its own address to be, which behind NAT is routinely a private address the client
    /// cannot reach - and, worse, a server that named a third host would have the client open a
    /// connection to it, which is the classic FTP bounce. The data connection always goes to the
    /// host the control connection is already talking to.</para>
    /// </summary>
    public static int ParsePasvPort(string text)
    {
        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        var body = open >= 0 && close > open ? text[(open + 1)..close] : text;

        var parts = body.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 6) return 0;

        // The last two fields are the port, high byte first - taken from the end so a reply with
        // extra leading text still parses.
        if (!int.TryParse(parts[^2], out var high) || !int.TryParse(parts[^1], out var low)) return 0;
        if (high is < 0 or > 255 || low is < 0 or > 255) return 0;

        return (high << 8) | low;
    }

    /// <summary>
    /// The port from a <c>229 Entering Extended Passive Mode (|||port|)</c> reply (RFC 2428).
    /// The delimiter is whatever character follows the opening parenthesis, not necessarily
    /// <c>|</c> - the RFC lets the server choose any ASCII character in 33-126.
    /// </summary>
    public static int ParseEpsvPort(string text)
    {
        var open = text.IndexOf('(', StringComparison.Ordinal);
        var close = text.IndexOf(')', open + 1);
        if (open < 0 || close < open + 2) return 0;

        var body = text[(open + 1)..close];
        var delimiter = body[0];
        var fields = body.Split(delimiter);

        // "|||port|" splits to ["", "", "", "port", ""] - the port is the last non-empty field.
        for (var i = fields.Length - 1; i >= 0; i--)
        {
            if (fields[i].Length == 0) continue;
            return int.TryParse(fields[i], out var port) && port is > 0 and <= 65535 ? port : 0;
        }
        return 0;
    }

    /// <summary>
    /// The path out of a <c>257 "/some/dir" is current directory</c> reply.
    /// Inside the quoted string a literal quote is doubled (RFC 959 §4.2), which is the part
    /// hand-rolled parsers miss.
    /// </summary>
    public static string ParseQuotedPath(string text)
    {
        var start = text.IndexOf('"', StringComparison.Ordinal);
        if (start < 0) return text.Trim();

        var result = new System.Text.StringBuilder();
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] != '"') { result.Append(text[i]); continue; }
            if (i + 1 < text.Length && text[i + 1] == '"') { result.Append('"'); i++; continue; }
            break;
        }
        return result.ToString();
    }
}
