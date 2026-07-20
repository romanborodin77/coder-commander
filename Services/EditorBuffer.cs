namespace CoderCommander.Services;

/// <summary>Zero-based line/column position within a <see cref="TextBuffer"/>.</summary>
public struct TextPosition : IEquatable<TextPosition>, IComparable<TextPosition>
{
    public int Line;
    public int Column;

    public TextPosition(int line, int column)
    {
        Line = line;
        Column = column;
    }

    public int CompareTo(TextPosition other)
    {
        var lineCompare = Line.CompareTo(other.Line);
        return lineCompare != 0 ? lineCompare : Column.CompareTo(other.Column);
    }

    public bool Equals(TextPosition other) => Line == other.Line && Column == other.Column;
    public override bool Equals(object? obj) => obj is TextPosition p && Equals(p);
    public override int GetHashCode() => HashCode.Combine(Line, Column);
    public static bool operator ==(TextPosition a, TextPosition b) => a.Equals(b);
    public static bool operator !=(TextPosition a, TextPosition b) => !a.Equals(b);
    public static bool operator <(TextPosition a, TextPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(TextPosition a, TextPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(TextPosition a, TextPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TextPosition a, TextPosition b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"({Line},{Column})";
}

/// <summary>Describes which lines were affected by an edit, for callers that cache per-line data.</summary>
public sealed class TextChangeEventArgs : EventArgs
{
    public int StartLine { get; }
    public int OldEndLine { get; }
    public int NewEndLine { get; }

    public TextChangeEventArgs(int startLine, int oldEndLine, int newEndLine)
    {
        StartLine = startLine;
        OldEndLine = oldEndLine;
        NewEndLine = newEndLine;
    }
}

/// <summary>
/// Document model for the code editor: a plain list of lines (no rope/piece-table — see the
/// editor rewrite plan for why that's the right tradeoff at this app's scale). Positions are
/// always (line, column), never absolute offsets, so caret/selection math stays O(1).
/// </summary>
public sealed class TextBuffer
{
    private readonly List<string> _lines = new() { "" };

    /// <summary>Line ending used by GetText()/SaveFile — detected from the loaded text.</summary>
    public string LineEnding { get; private set; } = "\r\n";

    /// <summary>Bumped on every mutation; cheap way for callers to invalidate their own caches.</summary>
    public int Version { get; private set; }

    public int LineCount => _lines.Count;

    public event EventHandler<TextChangeEventArgs>? Changed;

    public string GetLine(int index) => _lines[index];

    public int LineLength(int index) => _lines[index].Length;

    public TextPosition ClampPosition(TextPosition pos)
    {
        var line = Math.Clamp(pos.Line, 0, LineCount - 1);
        var column = Math.Clamp(pos.Column, 0, _lines[line].Length);
        return new TextPosition(line, column);
    }

    /// <summary>Loads text, replacing the whole buffer. Detects the dominant line ending.</summary>
    public void LoadText(string text)
    {
        LineEnding = DetectLineEnding(text);
        _lines.Clear();
        _lines.AddRange(SplitLines(text));
        if (_lines.Count == 0)
            _lines.Add("");
        Version++;
        Changed?.Invoke(this, new TextChangeEventArgs(0, 0, LineCount - 1));
    }

    public string GetText() => string.Join(LineEnding, _lines);

    /// <summary>
    /// Always joined with a single '\n' regardless of LineEnding — used when calling the syntax
    /// tokenizer so its absolute token offsets can be mapped back to (line, column) with a known,
    /// constant 1-character separator width instead of having to special-case "\r\n".
    /// </summary>
    public string GetTextForTokenizing() => string.Join('\n', _lines);

    public string GetTextInRange(TextPosition start, TextPosition end)
    {
        (start, end) = OrderPositions(start, end);
        start = ClampPosition(start);
        end = ClampPosition(end);
        if (start.Line == end.Line)
            return _lines[start.Line][start.Column..end.Column];

        var sb = new System.Text.StringBuilder();
        sb.Append(_lines[start.Line].AsSpan(start.Column));
        for (var i = start.Line + 1; i < end.Line; i++)
        {
            sb.Append('\n');
            sb.Append(_lines[i]);
        }
        sb.Append('\n');
        sb.Append(_lines[end.Line].AsSpan(0, end.Column));
        return sb.ToString();
    }

    /// <summary>Inserts text (which may itself contain newlines, e.g. a paste) at pos. Returns the position right after the inserted text.</summary>
    public TextPosition InsertText(TextPosition pos, string text)
    {
        pos = ClampPosition(pos);
        if (text.Length == 0) return pos;

        var incoming = SplitLines(text);
        var line = _lines[pos.Line];
        var before = line[..pos.Column];
        var after = line[pos.Column..];

        TextPosition endPos;
        if (incoming.Count == 1)
        {
            _lines[pos.Line] = before + incoming[0] + after;
            endPos = new TextPosition(pos.Line, before.Length + incoming[0].Length);
        }
        else
        {
            _lines[pos.Line] = before + incoming[0];
            var toInsert = new List<string>(incoming.Count - 1);
            for (var i = 1; i < incoming.Count - 1; i++)
                toInsert.Add(incoming[i]);
            toInsert.Add(incoming[^1] + after);
            _lines.InsertRange(pos.Line + 1, toInsert);
            endPos = new TextPosition(pos.Line + incoming.Count - 1, incoming[^1].Length);
        }

        Version++;
        Changed?.Invoke(this, new TextChangeEventArgs(pos.Line, pos.Line, endPos.Line));
        return endPos;
    }

    /// <summary>Deletes [start, end) and returns the deleted text (for undo).</summary>
    public string DeleteRange(TextPosition start, TextPosition end)
    {
        (start, end) = OrderPositions(start, end);
        start = ClampPosition(start);
        end = ClampPosition(end);
        if (start == end) return "";

        var deleted = GetTextInRange(start, end);

        if (start.Line == end.Line)
        {
            _lines[start.Line] = _lines[start.Line][..start.Column] + _lines[start.Line][end.Column..];
        }
        else
        {
            var merged = _lines[start.Line][..start.Column] + _lines[end.Line][end.Column..];
            _lines.RemoveRange(start.Line + 1, end.Line - start.Line);
            _lines[start.Line] = merged;
        }

        Version++;
        Changed?.Invoke(this, new TextChangeEventArgs(start.Line, end.Line, start.Line));
        return deleted;
    }

    private static (TextPosition, TextPosition) OrderPositions(TextPosition a, TextPosition b) => a <= b ? (a, b) : (b, a);

    /// <summary>
    /// Where <paramref name="text"/> would end if inserted at <paramref name="start"/> — a pure
    /// string computation independent of any buffer state, used by undo/redo to know the span of
    /// text it needs to delete without re-deriving it from the (possibly since-changed) buffer.
    /// </summary>
    public static TextPosition ComputeEndPosition(TextPosition start, string text)
    {
        // Must split on the same separators as SplitLines (including a bare '\r') - counting only
        // '\n' here would under-count old Mac-style line endings and hand back a position short of
        // where InsertText actually lands, corrupting undo of such an insert.
        var lines = SplitLines(text);
        if (lines.Count == 1)
            return new TextPosition(start.Line, start.Column + text.Length);

        return new TextPosition(start.Line + lines.Count - 1, lines[^1].Length);
    }

    private static string DetectLineEnding(string text)
    {
        var crlf = text.IndexOf("\r\n", StringComparison.Ordinal);
        if (crlf >= 0) return "\r\n";
        if (text.Contains('\n')) return "\n";
        if (text.Contains('\r')) return "\r";
        return "\r\n";
    }

    private static List<string> SplitLines(string text) =>
        text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None).ToList();
}
