using System.Text;

namespace CoderCommander.Terminal.Vt;

/// <summary>
/// Sanitizes OSC 0/1/2 title text before it's shown anywhere in the UI (the terminal TAB's
/// label only - never <c>MainForm.Text</c>, and title *reporting* back to the shell is never
/// implemented at all - see the terminal rewrite plan's security section for why: a
/// report-what-you-were-told-to-display round trip is the classic OSC-title RCE pattern).
/// <para>
/// Every byte in an OSC payload can be attacker-controlled (a crafted filename, a git branch
/// name, a malicious shell prompt), so this strips C0/C1 control characters, Trojan-Source-style
/// bidi override/isolate characters, and zero-width characters, collapses runs of whitespace,
/// and truncates to a bounded length.
/// </para>
/// </summary>
internal static class OscSanitizer
{
    // Numeric bounds instead of char literals for the invisible/format ranges below -
    // deliberately, so nothing here depends on an editor/tool round-tripping an invisible
    // Unicode character correctly.
    private const char C1Start = (char)0x0080;
    private const char C1End = (char)0x009F;
    private const char BidiEmbedStart = (char)0x202A; // LRE
    private const char BidiEmbedEnd = (char)0x202E;   // RLO
    private const char BidiIsolateStart = (char)0x2066; // LRI
    private const char BidiIsolateEnd = (char)0x2069;   // PDI
    private const char ZeroWidthStart = (char)0x200B; // ZWSP
    private const char ZeroWidthEnd = (char)0x200F;   // RLM

    public static string SanitizeTitle(ReadOnlySpan<char> raw)
    {
        var sb = new StringBuilder(Math.Min(raw.Length, VtLimits.MaxTitleLength));
        var lastWasSpace = false;

        foreach (var c in raw)
        {
            if (IsStripped(c))
                continue;

            var isSpace = c is ' ' or '\t';
            if (isSpace)
            {
                if (lastWasSpace) continue;
                sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }

            if (sb.Length >= VtLimits.MaxTitleLength)
            {
                // The truncation point can land right after a lone high surrogate when its
                // matching low surrogate would have been the next char - char.ConvertFromUtf32
                // elsewhere assumes well-formed pairs, so an unpaired one left dangling at the
                // end renders as a broken glyph. Drop it rather than keep a half-written pair.
                if (sb.Length > 0 && char.IsHighSurrogate(sb[^1]))
                    sb.Length--;
                break;
            }
        }

        return sb.ToString().Trim();
    }

    private static bool IsStripped(char c)
    {
        // C0 + DEL, EXCEPT tab: tab is whitespace, handled by SanitizeTitle's own collapse-to-
        // single-space logic below, not by outright removal - stripping it here would skip that
        // logic entirely (via the `continue` a stripped char takes) and delete it silently
        // instead of collapsing it.
        if ((c <= '\x1F' && c != '\t') || c == '\x7F') return true;
        if (c >= C1Start && c <= C1End) return true;                    // C1
        if (c >= BidiEmbedStart && c <= BidiEmbedEnd) return true;       // bidi embedding/override
        if (c >= BidiIsolateStart && c <= BidiIsolateEnd) return true;   // bidi isolates
        if (c >= ZeroWidthStart && c <= ZeroWidthEnd) return true;       // zero-width space, ZWNJ, ZWJ, LRM, RLM
        return false;
    }
}
