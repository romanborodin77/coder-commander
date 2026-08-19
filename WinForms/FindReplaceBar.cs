using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Inline find/replace bar docked at the top of the editor — modeled on FilePanelUserControl's
/// breadcrumb bar (always present in the control tree, toggled via Visible, never a modal dialog).
/// </summary>
internal sealed class FindReplaceBar : Panel
{
    private readonly CodeEditorCanvas _canvas;
    private readonly TextBuffer _buffer;
    private readonly UndoStack _undoStack;
    private readonly FindController _find = new();

    /// <summary>Raised whenever Replace/Replace All actually changed the buffer - the bar mutates
    /// <see cref="TextBuffer"/>/<see cref="UndoStack"/> directly, bypassing
    /// <see cref="CodeEditorCanvas"/>'s own edit methods entirely, so its
    /// <see cref="CodeEditorCanvas.ContentChanged"/> event (the only thing
    /// <see cref="CodeEditorControl"/> listens to for recomputing its Modified flag) never fires
    /// for a replacement - without this, replacing text left the tab looking unmodified and able
    /// to be closed with no "save changes?" prompt, silently discarding the edit.</summary>
    public event EventHandler? ContentChanged;

    private readonly TextBox _findBox;
    private readonly TextBox _replaceBox;
    private readonly Label _matchCountLabel;
    private readonly ThemedCheckBox _matchCaseCheck;
    private readonly Panel _replaceRow;

    /// <summary>Set for the duration of our own Replace/Replace All calls, so
    /// <see cref="OnBufferChanged"/> doesn't react to the very edits it made itself - those
    /// already re-scan (or clear) the match list themselves afterward, in the right order.</summary>
    private bool _suppressInvalidation;

    /// <summary>
    /// Initializes a new instance of the <see cref="FindReplaceBar"/> class, wiring find/replace
    /// controls to the specified canvas and buffer.
    /// </summary>
    /// <param name="canvas">The editor canvas whose selection and highlights are updated on match navigation.</param>
    /// <param name="buffer">The text buffer to search within.</param>
    /// <param name="undoStack">The undo stack used for replace operations.</param>
    public FindReplaceBar(CodeEditorCanvas canvas, TextBuffer buffer, UndoStack undoStack)
    {
        _canvas = canvas;
        _buffer = buffer;
        _undoStack = undoStack;
        // Match positions are a snapshot taken by SetPattern - any buffer edit not driven by this
        // bar's own Replace/Replace All (typically the user typing directly into the canvas
        // between opening Find and clicking Replace All) leaves stale offsets in _find.Matches.
        // Acting on those would delete/insert at whatever text now happens to sit at the old
        // coordinates, potentially unrelated to what was actually matched.
        _buffer.Changed += OnBufferChanged;

        Dock = DockStyle.Top;
        Visible = false;
        AutoSize = true;
        Padding = new Padding(6, 4, 6, 4);

        var L = LocalizationService.Current;

        var findRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _findBox = UiHelpers.CreateTextBox();
        _findBox.Width = 220;
        _findBox.Margin = new Padding(0, 3, 6, 3);
        _findBox.TextChanged += (_, _) => OnPatternChanged();
        _findBox.KeyDown += OnFindBoxKeyDown;

        var prevBtn = ThemedForm.CreateThemedButton("▲");
        prevBtn.Margin = new Padding(0, 0, 4, 0);
        prevBtn.Click += (_, _) => GoToMatch(_find.FindPrevious(out var wrapped), wrapped, forward: false);

        var nextBtn = ThemedForm.CreateThemedButton("▼");
        nextBtn.Margin = new Padding(0, 0, 10, 0);
        nextBtn.Click += (_, _) => GoToMatch(_find.FindNext(out var wrapped), wrapped, forward: true);

        _matchCaseCheck = UiHelpers.CreateCheckBox(L.GetString("Edit.FindBar.MatchCase"));
        _matchCaseCheck.AutoSize = true;
        _matchCaseCheck.Margin = new Padding(0, 6, 10, 0);
        _matchCaseCheck.CheckedChanged += (_, _) =>
        {
            _find.CaseSensitive = _matchCaseCheck.Checked;
            OnPatternChanged();
        };

        var closeBtn = ThemedForm.CreateThemedButton("✕");
        closeBtn.Margin = new Padding(0, 0, 10, 0);
        closeBtn.Click += (_, _) => CloseBar();

        _matchCountLabel = UiHelpers.CreateLabel("");
        _matchCountLabel.Margin = new Padding(0, 8, 0, 0);

        findRow.Controls.Add(_findBox);
        findRow.Controls.Add(prevBtn);
        findRow.Controls.Add(nextBtn);
        findRow.Controls.Add(_matchCaseCheck);
        findRow.Controls.Add(closeBtn);
        findRow.Controls.Add(_matchCountLabel);

        var replaceFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _replaceBox = UiHelpers.CreateTextBox();
        _replaceBox.Width = 220;
        _replaceBox.Margin = new Padding(0, 3, 6, 3);

        var replaceOneBtn = ThemedForm.CreateThemedButton(L.GetString("Edit.Toolbar.Replace"));
        replaceOneBtn.Margin = new Padding(0, 0, 4, 0);
        replaceOneBtn.Click += (_, _) => ReplaceOne();

        var replaceAllBtn = ThemedForm.CreateThemedButton(L.GetString("Edit.FindBar.ReplaceAll"));
        replaceAllBtn.Click += (_, _) => ReplaceAll();

        replaceFlow.Controls.Add(_replaceBox);
        replaceFlow.Controls.Add(replaceOneBtn);
        replaceFlow.Controls.Add(replaceAllBtn);

        _replaceRow = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Visible = false,
            BackColor = ThemeService.Current.HeaderBackground,
            Tag = ThemeRole.HeaderBackground
        };
        _replaceRow.Controls.Add(replaceFlow);

        // Added in reverse visual order: later-added Top-docked controls claim the position
        // closer to the edge (same convention as EditorForm's Controls.SetChildIndex comment).
        Controls.Add(_replaceRow);
        Controls.Add(findRow);

        ApplyTheme();
    }

    /// <summary>Applies the current theme to the bar background.</summary>
    public void ApplyTheme()
    {
        var p = ThemeService.Current;
        BackColor = p.HeaderBackground;
        // FindReplaceBar is itself a Panel, so ThemedForm's generic control traversal also visits
        // it directly - tag it so that pass keeps re-applying HeaderBackground too, instead of
        // falling back to the plain Background it uses for untagged panels.
        Tag = ThemeRole.HeaderBackground;
    }

    /// <summary>
    /// Shows the find bar, optionally with the replace row visible. Focuses the find text box
    /// and re-runs the current pattern if one is already entered.
    /// </summary>
    /// <param name="withReplace">If <c>true</c>, the replace row is also displayed.</param>
    public void ShowBar(bool withReplace)
    {
        Visible = true;
        _replaceRow.Visible = withReplace;
        if (!Focused && !_findBox.Focused)
        {
            _findBox.Focus();
            _findBox.SelectAll();
        }
        if (!string.IsNullOrEmpty(_findBox.Text))
            OnPatternChanged();
    }

    /// <summary>Hides the bar, clears the find pattern, removes match highlights, and returns focus to the canvas.</summary>
    public void CloseBar()
    {
        Visible = false;
        _find.Clear();
        _canvas.SetFindHighlights(null, -1);
        _canvas.Focus();
    }

    private void OnPatternChanged()
    {
        _find.SetPattern(_buffer, _findBox.Text, _canvas.Caret);
        _canvas.SetFindHighlights(_find.Matches, _find.CurrentIndex);
        if (_find.Current is { } m)
            _canvas.SelectRange(m.Start, m.End);
        UpdateMatchCountLabel();
    }

    /// <summary>Re-scans on any buffer edit not driven by our own Replace/Replace All, keeping
    /// the match list (and the count Replace All's confirmation dialog shows) accurate instead of
    /// silently stale - the same re-scan that already runs on every keystroke typed into the find
    /// box itself, just triggered by document edits instead.</summary>
    private void OnBufferChanged(object? sender, TextChangeEventArgs e)
    {
        if (_suppressInvalidation) return;
        if (!string.IsNullOrEmpty(_findBox.Text))
            OnPatternChanged();
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            if (e.Shift) GoToMatch(_find.FindPrevious(out var wp), wp, forward: false);
            else GoToMatch(_find.FindNext(out var wn), wn, forward: true);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            CloseBar();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private void GoToMatch(FindMatch? match, bool wrapped, bool forward)
    {
        _canvas.SetFindHighlights(_find.Matches, _find.CurrentIndex);
        if (match == null)
        {
            UpdateMatchCountLabel();
            return;
        }
        _canvas.SelectRange(match.Value.Start, match.Value.End);
        UpdateMatchCountLabel(wrapped, forward);
    }

    private void UpdateMatchCountLabel(bool wrapped = false, bool forward = true)
    {
        var L = LocalizationService.Current;
        if (_find.Matches.Count == 0)
        {
            _matchCountLabel.Text = string.IsNullOrEmpty(_findBox.Text) ? "" : L.GetString("Edit.NotFound");
            return;
        }
        var text = L.GetString("Edit.FindBar.MatchCount", _find.CurrentIndex + 1, _find.Matches.Count);
        if (wrapped)
            text += "  •  " + L.GetString(forward ? "Edit.FindBar.WrappedToTop" : "Edit.FindBar.WrappedToBottom");
        _matchCountLabel.Text = text;
    }

    private void ReplaceOne()
    {
        bool replaced;
        TextPosition caretAfter;
        _suppressInvalidation = true;
        try
        {
            replaced = _find.ReplaceCurrent(_buffer, _undoStack, _replaceBox.Text, out caretAfter);
        }
        finally { _suppressInvalidation = false; }

        if (!replaced) return;
        _canvas.SelectRange(caretAfter, caretAfter);
        _find.SetPattern(_buffer, _findBox.Text, caretAfter);
        _canvas.SetFindHighlights(_find.Matches, _find.CurrentIndex);
        UpdateMatchCountLabel();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceAll()
    {
        if (_find.Matches.Count == 0) return;
        var L = LocalizationService.Current;
        var result = StyledMessageBox.Show(
            L.GetString("Edit.FindBar.ReplaceAllConfirm", _find.Matches.Count),
            L.GetString("Edit.FindBar.ReplaceAll"),
            MsgBoxButtons.YesNo, MsgBoxIcon.Question);
        if (result != MsgBoxResult.Yes) return;

        int count;
        TextPosition caretAfter;
        _suppressInvalidation = true;
        try
        {
            count = _find.ReplaceAll(_buffer, _undoStack, _replaceBox.Text, _canvas.Caret, out caretAfter);
        }
        finally { _suppressInvalidation = false; }

        _canvas.SelectRange(caretAfter, caretAfter);
        _canvas.SetFindHighlights(null, -1);
        _matchCountLabel.Text = L.GetString("Edit.FindBar.ReplacedCount", count);
        if (count > 0)
            ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer.Changed -= OnBufferChanged;
            _findBox?.Dispose();
            _replaceBox?.Dispose();
            _matchCaseCheck?.Dispose();
            _matchCountLabel?.Dispose();
            _replaceRow?.Dispose();
        }
        base.Dispose(disposing);
    }
}
