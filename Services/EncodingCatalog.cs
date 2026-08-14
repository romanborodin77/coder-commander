using System.Text;

namespace CoderCommander.Services;

/// <summary>
/// The fixed list of encodings the Text viewer format's manual override dropdown offers, for the
/// case <see cref="TextEncodingDetector"/>'s autodetection guessed wrong - a legacy Cyrillic file
/// in particular can decode as any of three mutually-plausible single-byte encodings depending on
/// which one actually produced it, and no purely statistical heuristic resolves that reliably
/// without reference corpora this project doesn't have; a manual override is the honest fix.
/// </summary>
public static class EncodingCatalog
{
    public sealed record Entry(string Id, string DisplayNameKey, Func<Encoding> Factory);

    public static IReadOnlyList<Entry> Entries { get; } =
    [
        new("utf-8", "View.Encoding.Utf8", () => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
        new("utf-8-bom", "View.Encoding.Utf8Bom", () => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
        new("utf-16le", "View.Encoding.Utf16Le", () => Encoding.Unicode),
        new("utf-16be", "View.Encoding.Utf16Be", () => Encoding.BigEndianUnicode),
        new("windows-1251", "View.Encoding.Windows1251", () => Encoding.GetEncoding(1251)),
        new("koi8-r", "View.Encoding.Koi8R", () => Encoding.GetEncoding("koi8-r")),
        new("cp866", "View.Encoding.Cp866", () => Encoding.GetEncoding(866)),
        new("windows-1252", "View.Encoding.Windows1252", () => Encoding.GetEncoding(1252)),
        new("iso-8859-1", "View.Encoding.Latin1", () => Encoding.Latin1),
        new("ascii", "View.Encoding.Ascii", () => Encoding.ASCII),
    ];

    /// <summary>Resolves an <see cref="Entry.Id"/> to its <see cref="Encoding"/>, or null when the
    /// id is empty/unknown/unavailable on this system (e.g. <c>CodePagesEncodingProvider</c> not
    /// registered) - callers treat null the same as "no override, autodetect".</summary>
    public static Encoding? TryResolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        foreach (var entry in Entries)
        {
            if (!string.Equals(entry.Id, id, StringComparison.Ordinal)) continue;
            try { return entry.Factory(); }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException) { return null; }
        }
        return null;
    }
}
