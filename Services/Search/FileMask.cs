using System.Text;
using System.Text.RegularExpressions;

namespace CoderCommander.Services.Search;

/// <summary>
/// A set of wildcard masks - <c>*.cs;*.md</c> - compiled once and matched against file names.
///
/// <para><b>Compiled once on purpose.</b> The obvious implementation builds a regex inside the match
/// method, which is fine for selecting within one visible directory and ruinous for a search that
/// walks a hundred thousand files: the pattern is parsed and a new <see cref="Regex"/> object built
/// per file. This type is constructed once per search and then only matched against.</para>
///
/// <para><b><c>*.*</c> means "everything", not "something with a dot".</b> That is what it has meant
/// in every file manager since DOS, and the literal reading - which a naive wildcard-to-regex
/// translation produces - would silently drop every extensionless file from the results.</para>
/// </summary>
public sealed class FileMask
{
    /// <summary>Separators between masks. Semicolon is the convention; comma is accepted because
    /// people type it, and neither is legal in a Windows file name so neither is ambiguous.</summary>
    private static readonly char[] Separators = [';', ','];

    /// <summary>Backstop against catastrophic backtracking: a mask with several <c>*</c>/<c>?</c>
    /// wildcards (e.g. <c>*a*a*a*a*a*a*a*ab</c>) compiles to a sequence of <c>.*</c>/<c>.</c> groups
    /// that is polynomial-worst-case for the backtracking regex engine against a long non-matching
    /// name - the pattern is user-typed text, not developer-reviewed, so a pathological mask must
    /// time out rather than hang the search/selection thread indefinitely.</summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly Regex[] _patterns;

    /// <summary>Whether this mask accepts everything, in which case matching is skipped entirely.</summary>
    public bool MatchesEverything => _patterns.Length == 0;

    /// <summary>The text this mask was built from, for showing back to the user.</summary>
    public string Text { get; }

    public FileMask(string? masks)
    {
        Text = masks?.Trim() ?? "";

        var parts = Text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Masks are a union, so one "everything" mask anywhere in the set makes the whole set accept
        // everything - and then there is nothing to compile or match against.
        _patterns = parts.Length == 0 || parts.Any(IsMatchEverything)
            ? []
            : parts.Select(Compile).ToArray();
    }

    /// <summary>Whether <paramref name="name"/> matches any mask in the set.</summary>
    public bool Matches(string name)
    {
        if (_patterns.Length == 0) return true;

        foreach (var pattern in _patterns)
        {
            try
            {
                if (pattern.IsMatch(name)) return true;
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological mask against this particular name - treat as "does not match" rather
                // than propagate and abort the whole search/selection over one bad comparison.
            }
        }
        return false;
    }

    /// <summary>The two spellings of "everything". <c>*.*</c> is included for the reason in the class
    /// remarks: read literally it would exclude files without an extension.</summary>
    private static bool IsMatchEverything(string mask) => mask is "*" or "*.*";

    /// <summary>
    /// Wildcard to regex, anchored at both ends.
    ///
    /// <para>Built by escaping first and substituting the two wildcards afterwards, so a mask
    /// containing regex metacharacters - <c>report(final).txt</c>, <c>a+b.cs</c> - matches the
    /// literal name rather than being interpreted as a pattern. That is a correctness point, not a
    /// stylistic one: without it, a mask the user typed as text quietly becomes a regex.</para>
    /// </summary>
    private static Regex Compile(string mask)
    {
        var escaped = new StringBuilder("^");
        foreach (var c in mask)
        {
            escaped.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }
        escaped.Append('$');

        // IgnoreCase because Windows file names are case-insensitive, and a mask that matched
        // "readme.TXT" but not "readme.txt" would be surprising even on a case-sensitive server.
        // CultureInvariant so the meaning does not shift with the machine's locale - the Turkish
        // dotless i is the standard way this bites.
        return new Regex(escaped.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, MatchTimeout);
    }
}
