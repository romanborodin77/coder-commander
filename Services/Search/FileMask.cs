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

    /// <summary>Whether this mask accepts everything, in which case matching is skipped entirely.
    /// False for an invalid regex - see <see cref="IsValid"/> - since that matches nothing, not
    /// everything.</summary>
    public bool MatchesEverything => IsValid && _patterns.Length == 0;

    /// <summary>The text this mask was built from, for showing back to the user.</summary>
    public string Text { get; }

    /// <summary>False only when constructed with <c>useRegex: true</c> and <see cref="Text"/> failed
    /// to compile as a regex (unbalanced group, bad escape, etc.) - wildcard mode is always valid,
    /// since <see cref="Compile"/> builds its own regex from escaped literal text and cannot fail on
    /// arbitrary input. A caller driving a search UI should check this before running the search and
    /// show the compile error, rather than silently getting zero results from a mask that never
    /// matches anything (see <see cref="Matches"/>).</summary>
    public bool IsValid { get; } = true;

    public FileMask(string? masks) : this(masks, useRegex: false, caseSensitive: false) { }

    /// <param name="masks">Wildcard masks (<c>;</c>/<c>,</c>-separated) in the default mode, or a
    /// single regular expression when <paramref name="useRegex"/> is true - regex mode matches the
    /// whole <paramref name="masks"/> string as one pattern, never split on separators (a semicolon
    /// can be a meaningful regex literal, and combining multiple regexes with a separator has no
    /// single obvious meaning the way wildcard masks' implicit OR does).</param>
    /// <param name="useRegex">Interpret <paramref name="masks"/> as a regular expression instead of
    /// wildcards. Unlike wildcard mode (always case-insensitive, matching Windows file name
    /// semantics), regex mode honors <paramref name="caseSensitive"/> explicitly.</param>
    public FileMask(string? masks, bool useRegex, bool caseSensitive)
    {
        Text = masks?.Trim() ?? "";

        if (useRegex)
        {
            if (Text.Length == 0) { _patterns = []; return; }

            var opts = RegexOptions.Compiled | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            try
            {
                _patterns = [new Regex(Text, opts, MatchTimeout)];
            }
            catch (ArgumentException)
            {
                // Invalid regex, most often typed live and not finished yet - IsValid false lets
                // the caller surface this instead of a search that silently finds nothing.
                _patterns = [];
                IsValid = false;
            }
            return;
        }

        var parts = Text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Masks are a union, so one "everything" mask anywhere in the set makes the whole set accept
        // everything - and then there is nothing to compile or match against.
        _patterns = parts.Length == 0 || parts.Any(IsMatchEverything)
            ? []
            : parts.Select(Compile).ToArray();
    }

    /// <summary>Whether <paramref name="name"/> matches any mask in the set. Always false when
    /// <see cref="IsValid"/> is false (an invalid regex matches nothing rather than everything -
    /// the safer default for a mask feeding a selection or a delete filter).</summary>
    public bool Matches(string name)
    {
        if (!IsValid) return false;
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
