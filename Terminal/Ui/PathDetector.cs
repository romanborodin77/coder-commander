using System.Text.RegularExpressions;

namespace CoderCommander.Terminal.Ui;

/// <summary>
/// Heuristic detection of Windows filesystem paths in plain terminal text, for the right-click
/// "Show in panel" action. Deliberately conservative - only drive-letter-absolute
/// (<c>C:\Foo\Bar</c>) and UNC (<c>\\server\share\...</c>) forms, never bare relative paths, which
/// are indistinguishable from ordinary words without knowing the shell's actual cwd at the time
/// that line was printed. A false negative here just means the action doesn't offer itself; a
/// false positive would offer to navigate to something that isn't really a path.
/// </summary>
internal static class PathDetector
{
    // Stops at whitespace and a conservative set of characters that terminate a path in practice
    // (shell quoting, redirection, list separators) without needing to know which shell is
    // running.
    private static readonly Regex PathPattern =
        new(@"(?:[A-Za-z]:[\\/]|\\\\[^\s\\]+\\[^\s\\]+)[^\s""'<>|]*", RegexOptions.Compiled);

    /// <summary>Finds the path span (if any) covering column <paramref name="col"/> of
    /// <paramref name="lineText"/>.</summary>
    public static bool TryFindPathAt(string lineText, int col, out string path, out int start, out int length)
    {
        path = "";
        start = length = 0;
        if (string.IsNullOrEmpty(lineText) || col < 0 || col >= lineText.Length)
            return false;

        foreach (Match m in PathPattern.Matches(lineText))
        {
            if (col < m.Index || col >= m.Index + m.Length) continue;

            // Trim trailing punctuation that's almost always sentence/list punctuation, not part
            // of the path itself (e.g. "see C:\Work\Foo." or "(C:\Work\Foo)").
            var text = m.Value.TrimEnd('.', ',', ')', ']', ':', ';');
            if (text.Length == 0 || col >= m.Index + text.Length) return false;

            path = text;
            start = m.Index;
            length = text.Length;
            return true;
        }
        return false;
    }
}
