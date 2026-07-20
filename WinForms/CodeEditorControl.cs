using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Composite code-editing control replacing RichTextBox: an owner-drawn <see cref="CodeEditorCanvas"/>
/// plus themed scrollbars. This is the public API surface <see cref="EditorTab"/>/<see cref="EditorForm"/>
/// talk to. Selection, clipboard, undo/redo, syntax highlighting, the line-number gutter, and the
/// find/replace bar are added incrementally in later milestones — see the editor rewrite plan.
/// </summary>
public sealed class CodeEditorControl : Panel
{
    private readonly TextBuffer _buffer = new();
    private readonly CodeEditorCanvas _canvas;
    private readonly CodeEditorGutter _gutter;
    private readonly FindReplaceBar _findBar;
    private readonly ThemedScrollBar _vScroll;
    private readonly ThemedScrollBar _hScroll;

    private bool _modified;
    /// <summary>Undo-stack state id at the last save/load - lets Undo back to that exact state clear
    /// the modified flag instead of leaving it stuck on just because *something* happened since.</summary>
    private long _cleanStateId;

    public new event EventHandler? TextChanged;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ModifiedChanged;

    public bool Modified
    {
        get => _modified;
        set
        {
            if (!value)
                _cleanStateId = _canvas.UndoStack.CurrentStateId;
            if (_modified == value) return;
            _modified = value;
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Stored but not yet applied to layout — word wrap reflow isn't implemented (out of scope for this rewrite pass).</summary>
    public bool WordWrap { get; set; }

    public bool ShowWhitespace
    {
        get => _canvas.ShowWhitespace;
        set { _canvas.ShowWhitespace = value; _canvas.Invalidate(); }
    }

    public LanguageId Language
    {
        get => _canvas.Language;
        set => _canvas.SetLanguage(value);
    }

    public new string Text
    {
        get => _buffer.GetText();
        set => LoadText(value);
    }

    /// <summary>O(document) — only meant for occasional status-bar display, not a hot path.</summary>
    public int TextLength => _buffer.GetText().Length;

    public CodeEditorControl()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        _canvas = new CodeEditorCanvas(_buffer) { Dock = DockStyle.Fill };
        _canvas.ContentChanged += OnCanvasContentChanged;
        _canvas.ScrollChanged += (_, _) => SyncScrollBars();
        _canvas.CaretMoved += (_, _) => SelectionChanged?.Invoke(this, EventArgs.Empty);
        _canvas.Resize += (_, _) => SyncScrollBars();

        _vScroll = new ThemedScrollBar { Orientation = Orientation.Vertical, Dock = DockStyle.Right, Width = 14, Minimum = 0 };
        _hScroll = new ThemedScrollBar { Orientation = Orientation.Horizontal, Dock = DockStyle.Bottom, Height = 14, Minimum = 0 };
        _vScroll.ValueChanged += (_, _) => _canvas.ScrollY = _vScroll.Value;
        _hScroll.ValueChanged += (_, _) => _canvas.ScrollX = _hScroll.Value;

        _gutter = new CodeEditorGutter(_canvas);
        _findBar = new FindReplaceBar(_canvas, _buffer, _canvas.UndoStack);

        // Fill must be added first (WinForms docking order), edge-docked controls on top.
        Controls.Add(_canvas);
        Controls.Add(_hScroll);
        Controls.Add(_vScroll);
        Controls.Add(_gutter);
        Controls.Add(_findBar);

        BackColor = ThemeService.Current.PanelBackground;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _findBar.Visible)
        {
            _findBar.CloseBar();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnCanvasContentChanged(object? sender, EventArgs e)
    {
        Modified = _canvas.UndoStack.CurrentStateId != _cleanStateId;
        TextChanged?.Invoke(this, EventArgs.Empty);
        SyncScrollBars();
    }

    public void LoadText(string text)
    {
        _buffer.LoadText(text);
        _canvas.ResetCaretToStart();
        Modified = false;
        SyncScrollBars();
    }

    /// <summary>1-based (line, column) — O(1), unlike the old RichTextBox-based implementation.</summary>
    public (int Line, int Column) GetCursorPosition() => (_canvas.Caret.Line + 1, _canvas.Caret.Column + 1);

    public void Cut() => _canvas.Cut();
    public void Copy() => _canvas.Copy();
    public void Paste() => _canvas.Paste();
    public void SelectAll() => _canvas.SelectAll();
    public void Undo() => _canvas.Undo();
    public void Redo() => _canvas.Redo();
    public bool CanUndo => _canvas.CanUndo;
    public bool CanRedo => _canvas.CanRedo;
    public void ShowFindBar(bool withReplace) => _findBar.ShowBar(withReplace);
    public void HideFindBar() => _findBar.CloseBar();

    public void GoToLine(int line) => _canvas.GoToLine(line);

    public void ApplyTheme()
    {
        BackColor = ThemeService.Current.PanelBackground;
        _canvas.ApplyTheme();
        _gutter.ApplyTheme();
        _findBar.ApplyTheme();
        SyncScrollBars();
    }

    private void SyncScrollBars()
    {
        var lineHeight = _canvas.LineHeight;
        if (lineHeight <= 0 || _canvas.ClientSize.Height <= 0) return;

        var docHeight = _buffer.LineCount * lineHeight;
        _vScroll.Maximum = Math.Max(docHeight, _canvas.ClientSize.Height);
        _vScroll.LargeChange = Math.Max(1, _canvas.ClientSize.Height);
        _vScroll.Value = _canvas.ScrollY;

        // Placeholder horizontal range until real per-line width tracking lands (word-wrap milestone).
        _hScroll.Maximum = Math.Max(2000, _canvas.ScrollX + _canvas.ClientSize.Width);
        _hScroll.LargeChange = Math.Max(1, _canvas.ClientSize.Width);
        _hScroll.Value = _canvas.ScrollX;
    }
}
