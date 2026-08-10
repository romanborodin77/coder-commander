using System.Globalization;
using CoderCommander.Services;

namespace CoderCommander.FileSystem.Remote.Ftp;

/// <summary>One entry read out of an FTP directory listing.</summary>
public sealed record FtpListEntry(string Name, bool IsDirectory, long Size, DateTime LastWriteTimeUtc);

/// <summary>
/// Turns an FTP directory listing into entries.
///
/// <para><b>Why two parsers.</b> <c>LIST</c> has no defined format at all - RFC 959 says the output
/// is for humans - so every server invented its own, and a client is left matching shapes. RFC 3659
/// fixed this years later with <c>MLSD</c>, whose output is machine-readable and unambiguous. This
/// class prefers MLSD wherever the server offers it and falls back to shape-matching only when it
/// does not.</para>
///
/// <para><b>Every name is untrusted.</b> A listing is attacker-influenced data whose names go on to
/// build local paths during a download, so each one goes through
/// <see cref="RemotePath.IsSafeEntryName"/> - the same check archive entry names get, and for the
/// same reason.</para>
///
/// <para>Kept free of sockets so it can be tested against transcripts from real servers, which is
/// the only way to have any confidence in a format defined by precedent rather than by a document.</para>
/// </summary>
public static class FtpListParser
{
    /// <summary>
    /// Parses <c>MLSD</c> output: <c>fact=value;fact=value; name</c> per line (RFC 3659 §7).
    ///
    /// Fact names are case-insensitive per the RFC, which servers exploit freely - <c>Type</c>,
    /// <c>type</c> and <c>TYPE</c> all occur in the wild.
    /// </summary>
    public static IReadOnlyList<FtpListEntry> ParseMlsd(IEnumerable<string> lines)
    {
        var entries = new List<FtpListEntry>();

        foreach (var line in lines)
        {
            if (entries.Count >= RemoteLimits.MaxEntriesPerDirectory)
            {
                LogService.Warning($"FTP: listing truncated at {RemoteLimits.MaxEntriesPerDirectory} entries");
                break;
            }

            // The name is everything after the first space that follows the facts. A name may itself
            // contain spaces, so the split is on the first one only.
            var split = line.IndexOf(' ');
            if (split < 0) continue;

            var name = line[(split + 1)..];
            if (name is "." or "..") continue;
            if (!RemotePath.IsSafeEntryName(name))
            {
                LogService.Warning("FTP: rejected a listing entry with an unsafe name");
                continue;
            }

            string? type = null, sizeText = null, modifyText = null;
            foreach (var fact in line[..split].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = fact.IndexOf('=');
                if (eq <= 0) continue;

                var key = fact[..eq];
                var value = fact[(eq + 1)..];
                if (key.Equals("type", StringComparison.OrdinalIgnoreCase)) type = value;
                else if (key.Equals("size", StringComparison.OrdinalIgnoreCase)) sizeText = value;
                else if (key.Equals("modify", StringComparison.OrdinalIgnoreCase)) modifyText = value;
            }

            // "cdir" and "pdir" are the directory itself and its parent - present in MLSD output and
            // not children of it.
            if (type is not null &&
                (type.Equals("cdir", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("pdir", StringComparison.OrdinalIgnoreCase)))
                continue;

            var isDirectory = type is not null && type.Equals("dir", StringComparison.OrdinalIgnoreCase);

            entries.Add(new FtpListEntry(
                name,
                isDirectory,
                isDirectory ? 0 : ParseSize(sizeText),
                ParseMlsdTimestamp(modifyText)));
        }

        return entries;
    }

    /// <summary>
    /// Parses <c>LIST</c> output in either of the two shapes that between them cover essentially
    /// every server: the Unix <c>ls -l</c> form and the DOS/IIS form. Anything else yields nothing
    /// for that line rather than a guess.
    /// </summary>
    public static IReadOnlyList<FtpListEntry> ParseList(IEnumerable<string> lines)
    {
        var entries = new List<FtpListEntry>();

        foreach (var line in lines)
        {
            if (entries.Count >= RemoteLimits.MaxEntriesPerDirectory)
            {
                LogService.Warning($"FTP: listing truncated at {RemoteLimits.MaxEntriesPerDirectory} entries");
                break;
            }

            if (line.Length == 0) continue;
            // "total 42" heads Unix listings and is not an entry.
            if (line.StartsWith("total ", StringComparison.OrdinalIgnoreCase)) continue;

            var entry = ParseUnixLine(line) ?? ParseDosLine(line);
            if (entry is null)
            {
                LogService.Debug($"FTP: unrecognised listing line, skipped ({line.Length} chars)");
                continue;
            }

            if (entry.Name is "." or "..") continue;
            if (!RemotePath.IsSafeEntryName(entry.Name))
            {
                LogService.Warning("FTP: rejected a listing entry with an unsafe name");
                continue;
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// <c>drwxr-xr-x   2 owner group     4096 Jan 15 09:31 name with spaces</c>
    ///
    /// <para>Fields are whitespace-separated but the count varies - some servers omit the group,
    /// some add one - so the name is found by locating the date instead of by counting: the date is
    /// three fields, and everything after them is the name however many spaces it contains.</para>
    /// </summary>
    private static FtpListEntry? ParseUnixLine(string line)
    {
        var first = line[0];
        // The permission block is the only reliable marker. 'd' directory, '-' file, 'l' symlink.
        if (first is not ('d' or '-' or 'l')) return null;
        if (line.Length < 10) return null;

        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < 8) return null;

        // Walk forward to the month name; the two fields after it are the day and the
        // year-or-time, and the name starts after those.
        var monthIndex = -1;
        for (var i = 3; i < fields.Length - 2 && i < 8; i++)
        {
            if (MonthNumber(fields[i]) > 0) { monthIndex = i; break; }
        }
        if (monthIndex < 0) return null;

        var nameFieldIndex = monthIndex + 3;
        if (nameFieldIndex >= fields.Length) return null;

        // Recover the name from the original line rather than by re-joining fields, so runs of
        // spaces inside it survive.
        var name = SkipFields(line, nameFieldIndex);
        if (name.Length == 0) return null;

        // A symlink lists as "link -> target"; the name is the part before the arrow.
        if (first == 'l')
        {
            var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow > 0) name = name[..arrow];
        }

        var size = ParseSize(fields[monthIndex - 1]);
        var timestamp = ParseUnixTimestamp(fields[monthIndex], fields[monthIndex + 1], fields[monthIndex + 2]);

        // A symlink's target may be a directory, and LIST does not say. Treating it as a file is
        // the safer wrong answer: the panel then offers to download it rather than to descend into
        // something that may not be a directory at all.
        return new FtpListEntry(name, first == 'd', first == 'd' ? 0 : size, timestamp);
    }

    /// <summary>
    /// <c>01-15-24  09:31AM       &lt;DIR&gt;          name</c> or
    /// <c>01-15-24  09:31AM              1234 name</c> - the IIS/DOS form.
    /// </summary>
    private static FtpListEntry? ParseDosLine(string line)
    {
        var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (fields.Length < 4) return null;

        if (!DateTime.TryParseExact(
                $"{fields[0]} {fields[1]}",
                ["MM-dd-yy hh:mmtt", "MM-dd-yyyy hh:mmtt", "MM/dd/yy hh:mmtt", "MM/dd/yyyy hh:mmtt"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            return null;

        var isDirectory = fields[2].Equals("<DIR>", StringComparison.OrdinalIgnoreCase);
        var name = SkipFields(line, 3);
        if (name.Length == 0) return null;

        // The DOS listing carries no time zone. Treating it as UTC would shift every timestamp by
        // the server's offset; treating it as unspecified keeps it as the server displayed it.
        return new FtpListEntry(
            name,
            isDirectory,
            isDirectory ? 0 : ParseSize(fields[2]),
            DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified));
    }

    /// <summary>Everything in <paramref name="line"/> after the first <paramref name="count"/>
    /// whitespace-separated fields, with the original spacing of the remainder intact.</summary>
    private static string SkipFields(string line, int count)
    {
        var i = 0;
        for (var field = 0; field < count; field++)
        {
            while (i < line.Length && line[i] == ' ') i++;
            while (i < line.Length && line[i] != ' ') i++;
        }
        while (i < line.Length && line[i] == ' ') i++;
        return i < line.Length ? line[i..] : "";
    }

    private static long ParseSize(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size >= 0
            ? size
            : 0;

    /// <summary><c>YYYYMMDDHHMMSS</c>, optionally with fractional seconds, always UTC (RFC 3659 §2.3).
    /// This is the one timestamp in FTP that needs no guessing.</summary>
    private static DateTime ParseMlsdTimestamp(string? text)
    {
        if (string.IsNullOrEmpty(text)) return default;

        var trimmed = text.Length > 14 ? text[..14] : text;
        return DateTime.TryParseExact(trimmed, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : default;
    }

    /// <summary>
    /// The Unix listing's date, which is the format's worst feature: recent entries show a time and
    /// no year, older ones show a year and no time.
    ///
    /// <para>A missing year is taken as the most recent one that does not put the date in the
    /// future - the same rule <c>ls</c> itself uses. Assuming the current year instead makes every
    /// file from last December appear to be from this December, eleven months out.</para>
    ///
    /// <para>No time zone is stated anywhere, so the result is deliberately
    /// <see cref="DateTimeKind.Unspecified"/> rather than a UTC value that would be wrong by the
    /// server's offset.</para>
    /// </summary>
    private static DateTime ParseUnixTimestamp(string month, string day, string yearOrTime)
    {
        var monthNumber = MonthNumber(month);
        if (monthNumber == 0) return default;
        if (!int.TryParse(day, out var dayNumber) || dayNumber is < 1 or > 31) return default;

        try
        {
            if (yearOrTime.Contains(':'))
            {
                var parts = yearOrTime.Split(':');
                if (parts.Length < 2 || !int.TryParse(parts[0], out var hour) || !int.TryParse(parts[1], out var minute))
                    return default;

                var now = DateTime.Now;
                var candidate = new DateTime(now.Year, monthNumber, dayNumber, hour, minute, 0, DateTimeKind.Unspecified);
                // Allow a day of slack: the server's clock and this one need not agree exactly, and
                // rolling a today-dated entry back a whole year over a few minutes' skew is worse
                // than showing a date a few hours in the future.
                return candidate > now.AddDays(1) ? candidate.AddYears(-1) : candidate;
            }

            return int.TryParse(yearOrTime, out var year) && year is >= 1970 and <= 9999
                ? new DateTime(year, monthNumber, dayNumber, 0, 0, 0, DateTimeKind.Unspecified)
                : default;
        }
        catch (ArgumentOutOfRangeException)
        {
            // 31 February and friends. A nonsense date is "unknown", not a crash.
            return default;
        }
    }

    /// <summary>1-12 for an English month abbreviation, 0 for anything else. Month names are matched
    /// against the invariant culture on purpose: a server localised to the client's culture is not a
    /// thing, and using the current culture would break the parser on a Russian Windows.</summary>
    private static int MonthNumber(string text)
    {
        if (text.Length != 3) return 0;
        var months = CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedMonthNames;
        for (var i = 0; i < 12; i++)
        {
            if (string.Equals(months[i], text, StringComparison.OrdinalIgnoreCase)) return i + 1;
        }
        return 0;
    }
}
