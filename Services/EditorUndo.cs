namespace CoderCommander.Services;

/// <summary>A single edit within a group: OldText (removed, empty for a pure insert) replaced by NewText (inserted, empty for a pure delete) at Start.</summary>
public readonly struct TextEdit
{
    public TextPosition Start { get; init; }
    public string OldText { get; init; }
    public string NewText { get; init; }
}

/// <summary>
/// One undoable action. Usually a single TextEdit (typing, backspace, delete, paste); Replace All
/// records every replacement as one EditGroup so a single Ctrl+Z reverts the whole operation.
/// Edits are stored in the order they were originally applied (chronological), which for a batch
/// like Replace All means position-safe application order (last match to first) — Redo replays
/// them forward in that order, Undo reverses them back to front. See UndoStack.RecordBatch.
/// </summary>
public sealed class EditGroup
{
    public List<TextEdit> Edits { get; init; } = [];
    public TextPosition CaretBefore;
    public TextPosition CaretAfter;
    public DateTime LastEditUtc;
    /// <summary>Stamped by <see cref="UndoStack"/> with a fresh id whenever this group's content
    /// changes (first pushed, or extended by coalescing) — see <see cref="UndoStack.CurrentStateId"/>.</summary>
    internal long StateId;
}

/// <summary>
/// Undo/redo history for the code editor. Doesn't touch the buffer itself — callers apply the
/// EditGroup returned by Undo()/Redo().
/// </summary>
public sealed class UndoStack
{
    private const int MaxDepth = 500;
    private const int CoalesceTimeoutMs = 700;

    private readonly LinkedList<EditGroup> _undo = new();
    private readonly Stack<EditGroup> _redo = new();
    private long _nextStateId;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Identifies the document's current content within this editing session: 0 for the pristine
    /// (just loaded/cleared) state, otherwise the id stamped on the top-of-undo-stack group. Two
    /// reads are equal only when they refer to the exact same content — Undo/Redo hand back a
    /// previously-stamped id when they revisit a state, while any new edit (even one made after
    /// undoing) always gets a fresh id. Lets a caller remember "the id at save time" and compare
    /// later to know whether the document is back to that exact state, instead of just tracking
    /// "has anything happened since".
    /// </summary>
    public long CurrentStateId => _undo.Count > 0 ? _undo.Last!.Value.StateId : 0;

    /// <summary>
    /// Records a single-edit group (the common case: typing, backspace, delete, paste).
    /// coalesceWithPrevious requests a merge into the top-of-stack group when it's eligible
    /// (single-character run, same direction, within the coalesce window) — callers pass this
    /// for plain typing/backspace/delete, never for paste, cut, or an edit that replaced a selection.
    /// </summary>
    public void Record(TextPosition start, string oldText, string newText, TextPosition caretBefore, TextPosition caretAfter, bool coalesceWithPrevious)
    {
        _redo.Clear();

        if (coalesceWithPrevious && _undo.Count > 0 && TryCoalesce(_undo.Last!.Value, start, oldText, newText, caretAfter))
        {
            _undo.Last!.Value.StateId = ++_nextStateId;
            return;
        }

        Push(new EditGroup
        {
            Edits = [new TextEdit { Start = start, OldText = oldText, NewText = newText }],
            CaretBefore = caretBefore,
            CaretAfter = caretAfter,
            LastEditUtc = DateTime.UtcNow,
            StateId = ++_nextStateId
        });
    }

    /// <summary>Records a multi-edit atomic group (e.g. Replace All) — never coalesced with anything.</summary>
    public void RecordBatch(IReadOnlyList<TextEdit> edits, TextPosition caretBefore, TextPosition caretAfter)
    {
        if (edits.Count == 0) return;
        _redo.Clear();
        Push(new EditGroup { Edits = [..edits], CaretBefore = caretBefore, CaretAfter = caretAfter, LastEditUtc = DateTime.UtcNow, StateId = ++_nextStateId });
    }

    private void Push(EditGroup group)
    {
        _undo.AddLast(group);
        if (_undo.Count > MaxDepth)
            _undo.RemoveFirst();
    }

    private static bool TryCoalesce(EditGroup top, TextPosition start, string oldText, string newText, TextPosition caretAfter)
    {
        if (top.Edits.Count != 1) return false; // only coalesce simple single-edit groups
        var topEdit = top.Edits[0];
        var elapsedOk = (DateTime.UtcNow - top.LastEditUtc).TotalMilliseconds <= CoalesceTimeoutMs;
        if (!elapsedOk) return false;

        var topIsPureInsert = topEdit.OldText.Length == 0;
        var thisIsPureInsert = oldText.Length == 0;
        if (topIsPureInsert && thisIsPureInsert && !topEdit.NewText.Contains('\n') && !newText.Contains('\n'))
        {
            var topEnd = new TextPosition(topEdit.Start.Line, topEdit.Start.Column + topEdit.NewText.Length);
            if (topEnd == start)
            {
                top.Edits[0] = new TextEdit { Start = topEdit.Start, OldText = topEdit.OldText, NewText = topEdit.NewText + newText };
                top.CaretAfter = caretAfter;
                top.LastEditUtc = DateTime.UtcNow;
                return true;
            }
        }

        var topIsPureDelete = topEdit.NewText.Length == 0;
        var thisIsPureDelete = newText.Length == 0;
        if (topIsPureDelete && thisIsPureDelete && !topEdit.OldText.Contains('\n') && !oldText.Contains('\n'))
        {
            var thisEnd = new TextPosition(start.Line, start.Column + oldText.Length);
            if (thisEnd == topEdit.Start)
            {
                // Backspace direction: the new deletion happened immediately before the existing one.
                top.Edits[0] = new TextEdit { Start = start, OldText = oldText + topEdit.OldText, NewText = "" };
                top.CaretAfter = caretAfter;
                top.LastEditUtc = DateTime.UtcNow;
                return true;
            }
            if (start == topEdit.Start)
            {
                // Forward-delete direction: caret stayed put, extending the deleted run.
                top.Edits[0] = new TextEdit { Start = topEdit.Start, OldText = topEdit.OldText + oldText, NewText = "" };
                top.CaretAfter = caretAfter;
                top.LastEditUtc = DateTime.UtcNow;
                return true;
            }
        }

        return false;
    }

    public EditGroup? Undo()
    {
        if (_undo.Count == 0) return null;
        var group = _undo.Last!.Value;
        _undo.RemoveLast();
        _redo.Push(group);
        return group;
    }

    public EditGroup? Redo()
    {
        if (_redo.Count == 0) return null;
        var group = _redo.Pop();
        _undo.AddLast(group);
        return group;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
