using CoderCommander.Services;

namespace CoderCommander.UiTests;

/// <summary>
/// Regression tests for the out-of-bounds fix in <see cref="SyntaxHighlighter"/>'s string-literal
/// scanning (TokenizeCLike, TokenizePython, TokenizeJson): an unterminated string literal whose
/// very last character is an escaping backslash pushed the scan index one past the end of the
/// text, and the following text[start..i] slice threw ArgumentOutOfRangeException. Both call
/// sites in CodeEditorCanvas.OnHighlightTimerTick catch this, so it didn't crash the app - but
/// highlighting broke permanently for that file, and every subsequent keystroke re-threw the
/// same exception. Realistic trigger: typing a Windows path literal (e.g. "C:\) and pausing mid-edit.
/// </summary>
public class SyntaxHighlighterUnterminatedStringTests
{
    [Test]
    public void Tokenize_CSharp_UnterminatedStringEndingInBackslash_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => SyntaxHighlighter.Tokenize("var path = \"C:\\", LanguageId.CSharp));
    }

    [Test]
    public void Tokenize_Python_UnterminatedStringEndingInBackslash_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => SyntaxHighlighter.Tokenize("path = \"C:\\", LanguageId.Python));
    }

    [Test]
    public void Tokenize_Json_UnterminatedStringEndingInBackslash_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => SyntaxHighlighter.Tokenize("{\"key\": \"C:\\", LanguageId.Json));
    }

    [Test]
    public void Tokenize_CSharp_NormalEscapedString_StillTokenizesCorrectly()
    {
        var tokens = SyntaxHighlighter.Tokenize("var s = \"a\\\\b\";", LanguageId.CSharp);
        Assert.That(tokens.Any(t => t.Type == TokenType.String && t.Text == "\"a\\\\b\""), Is.True);
    }
}
