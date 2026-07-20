namespace CoderCommander.Services;

public readonly struct FindMatch
{
    public TextPosition Start { get; init; }
    public TextPosition End { get; init; }
}

/// <summary>
/// Plain substring search over a <see cref="TextBuffer"/> (line by line — good enough at this
/// app's file-manager scale; no need for the incremental-search machinery a huge-document editor
/// would want). Owns the match list and "current" cursor into it so the find bar and the canvas's
/// match-highlight painting share one source of truth.
/// </summary>
public sealed class FindController
{
    private List<FindMatch> _matches = [];
    private int _currentIndex = -1;

    public IReadOnlyList<FindMatch> Matches => _matches;
    public int CurrentIndex => _currentIndex;
    public bool CaseSensitive { get; set; }

    public FindMatch? Current => _currentIndex >= 0 && _currentIndex < _matches.Count ? _matches[_currentIndex] : null;

    /// <summary>Recomputes all matches for pattern and points CurrentIndex at the first match at or after caret (wrapping to the first match otherwise).</summary>
    public void SetPattern(TextBuffer buffer, string pattern, TextPosition caret)
    {
        _matches = [];

        if (!string.IsNullOrEmpty(pattern))
        {
            var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            for (var line = 0; line < buffer.LineCount; line++)
            {
                var text = buffer.GetLine(line);
                var col = 0;
                while (col <= text.Length)
                {
                    var idx = text.IndexOf(pattern, col, comparison);
                    if (idx < 0) break;
                    _matches.Add(new FindMatch { Start = new TextPosition(line, idx), End = new TextPosition(line, idx + pattern.Length) });
                    col = idx + Math.Max(1, pattern.Length);
                }
            }
        }

        _currentIndex = _matches.Count == 0 ? -1 : FindClosestIndexAtOrAfter(caret);
    }

    private int FindClosestIndexAtOrAfter(TextPosition caret)
    {
        for (var i = 0; i < _matches.Count; i++)
            if (_matches[i].Start >= caret) return i;
        return 0;
    }

    public FindMatch? FindNext(out bool wrapped)
    {
        wrapped = false;
        if (_matches.Count == 0) return null;
        var next = _currentIndex + 1;
        if (next >= _matches.Count) { next = 0; wrapped = true; }
        _currentIndex = next;
        return _matches[_currentIndex];
    }

    public FindMatch? FindPrevious(out bool wrapped)
    {
        wrapped = false;
        if (_matches.Count == 0) return null;
        var prev = _currentIndex - 1;
        if (prev < 0) { prev = _matches.Count - 1; wrapped = true; }
        _currentIndex = prev;
        return _matches[_currentIndex];
    }

    /// <summary>
    /// Replaces the current match and advances to whatever now occupies its place. Callers should
    /// re-run SetPattern afterward — a single replacement can shift every later match's position
    /// if replacement.Length != the matched text's length, and re-deriving that here would just
    /// duplicate SetPattern's scan.
    /// </summary>
    public bool ReplaceCurrent(TextBuffer buffer, UndoStack undo, string replacement, out TextPosition caretAfter)
    {
        caretAfter = default;
        if (_currentIndex < 0 || _currentIndex >= _matches.Count) return false;

        var m = _matches[_currentIndex];
        var oldText = buffer.GetTextInRange(m.Start, m.End);
        buffer.DeleteRange(m.Start, m.End);
        buffer.InsertText(m.Start, replacement);
        caretAfter = TextBuffer.ComputeEndPosition(m.Start, replacement);
        undo.Record(m.Start, oldText, replacement, m.Start, caretAfter, coalesceWithPrevious: false);
        return true;
    }

    /// <summary>Replaces every current match as one atomic undo group. Returns the number replaced.</summary>
    public int ReplaceAll(TextBuffer buffer, UndoStack undo, string replacement, TextPosition caretBefore, out TextPosition caretAfter)
    {
        if (_matches.Count == 0)
        {
            caretAfter = caretBefore;
            return 0;
        }

        // Apply from the last match to the first so earlier matches' positions stay valid as we go.
        var edits = new List<TextEdit>(_matches.Count);
        for (var i = _matches.Count - 1; i >= 0; i--)
        {
            var m = _matches[i];
            var oldText = buffer.GetTextInRange(m.Start, m.End);
            buffer.DeleteRange(m.Start, m.End);
            buffer.InsertText(m.Start, replacement);
            edits.Add(new TextEdit { Start = m.Start, OldText = oldText, NewText = replacement });
        }

        caretAfter = TextBuffer.ComputeEndPosition(_matches[0].Start, replacement);
        undo.RecordBatch(edits, caretBefore, caretAfter);

        var count = _matches.Count;
        _matches = [];
        _currentIndex = -1;
        return count;
    }

    public void Clear()
    {
        _matches = [];
        _currentIndex = -1;
    }
}
