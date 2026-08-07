using System.Diagnostics;
using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression test for the O(n^2) inline-scan fix in
/// <see cref="SyntaxHighlighter"/>'s Markdown tokenizer: the bold/italic/code/link scan re-ran 4
/// regexes over the full remaining suffix on every match found, so a single line built from many
/// short matches took quadratic time. <see cref="CodeEditorCanvas"/> tokenizes synchronously on
/// the UI thread for files under its sync-tokenize character threshold, so a pathological line
/// near that limit would freeze the editor.
/// </summary>
public class SyntaxHighlighterMarkdownPerfTests
{
    [Test]
    public void Tokenize_Markdown_PathologicalRepeatingLine_CompletesQuickly()
    {
        // A single line, well beyond the inline-scan cap, made of a pattern that matches the
        // italic regex on every 3 characters - the worst case for the old O(n^2) scan.
        var line = string.Concat(Enumerable.Repeat("*a*", 50_000)); // 150,000 chars

        var sw = Stopwatch.StartNew();
        var tokens = SyntaxHighlighter.Tokenize(line, LanguageId.Markdown);
        sw.Stop();

        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
            $"A single pathological Markdown line must not make Tokenize quadratic (took {sw.Elapsed})");
        Assert.That(tokens, Is.Not.Empty);
    }

    [Test]
    public void Tokenize_Markdown_OrdinaryShortLine_StillHighlightsInline()
    {
        var tokens = SyntaxHighlighter.Tokenize("This is **bold** and *italic* text.", LanguageId.Markdown);

        Assert.That(tokens.Any(t => t.Type == TokenType.MarkdownBold), Is.True,
            "Short lines under the cap must still get real inline formatting, not just Plain");
        Assert.That(tokens.Any(t => t.Type == TokenType.MarkdownItalic), Is.True);
    }
}
