using System.Globalization;

namespace CoderCommander.Terminal.Vt;

/// <summary>
/// Terminal cell width (0, 1, or 2) for a Unicode code point - the "wcwidth" problem. 0 for
/// combining marks and zero-width joiners/formatting characters (they attach to the previous
/// cell instead of occupying their own); 2 for East Asian Wide/Fullwidth characters and emoji;
/// 1 otherwise. Ambiguous-width (UAX #11 category A) characters are treated as 1, matching
/// Windows Terminal's default.
/// </summary>
internal static class CharWidth
{
    public static int Of(int rune)
    {
        if (rune < 0x20) return 0; // controls never reach here as "print", but stay safe if they do
        if (rune == 0x00AD) return 1; // soft hyphen - Format category, but visually occupies a cell

        if (rune is >= 0x200B and <= 0x200F) return 0; // zero-width space, LRM/RLM, etc.
        if (rune == 0x200D) return 0; // zero-width joiner
        if (rune is >= 0x2060 and <= 0x2064) return 0; // word joiner and friends
        if (rune is >= 0xFE00 and <= 0xFE0F) return 0; // variation selectors (incl. VS16)
        if (rune is >= 0xE0100 and <= 0xE01EF) return 0; // variation selectors supplement

        var category = rune <= 0xFFFF
            ? CharUnicodeInfo.GetUnicodeCategory((char)rune)
            : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(rune), 0);

        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.Format)
            return 0;

        return IsWide(rune) ? 2 : 1;
    }

    // UAX #11 East Asian Wide (W) / Fullwidth (F) ranges plus the common emoji-presentation
    // blocks, matching what mainstream terminal emulators use. Sorted, searched by binary search.
    private static readonly (int Start, int End)[] WideRanges =
    [
        (0x1100, 0x115F),   // Hangul Jamo
        (0x2329, 0x232A),   // Angle brackets
        (0x2E80, 0x303E),   // CJK Radicals .. CJK Symbols and Punctuation
        (0x3041, 0x33FF),   // Hiragana .. CJK Compatibility
        (0x3400, 0x4DBF),   // CJK Unified Ideographs Extension A
        (0x4E00, 0x9FFF),   // CJK Unified Ideographs
        (0xA000, 0xA4CF),   // Yi Syllables
        (0xAC00, 0xD7A3),   // Hangul Syllables
        (0xF900, 0xFAFF),   // CJK Compatibility Ideographs
        (0xFE30, 0xFE4F),   // CJK Compatibility Forms
        (0xFF00, 0xFF60),   // Fullwidth Forms
        (0xFFE0, 0xFFE6),   // Fullwidth Signs
        (0x16FE0, 0x16FE4),
        (0x17000, 0x18AFF), // Tangut
        (0x1AFF0, 0x1B2FF), // Kana Extended / Nushu
        (0x1F004, 0x1F004),
        (0x1F0CF, 0x1F0CF),
        (0x1F18E, 0x1F18E),
        (0x1F191, 0x1F19A),
        (0x1F200, 0x1F320),
        (0x1F300, 0x1F64F), // Misc Symbols/Pictographs, Emoticons
        (0x1F680, 0x1F6FF), // Transport and Map
        (0x1F900, 0x1F9FF), // Supplemental Symbols and Pictographs
        (0x1FA70, 0x1FAFF), // Symbols and Pictographs Extended-A
        (0x20000, 0x2FFFD), // CJK Unified Ideographs Extension B..
        (0x30000, 0x3FFFD),
    ];

    private static bool IsWide(int rune)
    {
        int lo = 0, hi = WideRanges.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var (start, end) = WideRanges[mid];
            if (rune < start) hi = mid - 1;
            else if (rune > end) lo = mid + 1;
            else return true;
        }
        return false;
    }
}
