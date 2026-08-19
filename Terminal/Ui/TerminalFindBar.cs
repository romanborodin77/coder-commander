using CoderCommander.Services;
using CoderCommander.WinForms;

namespace CoderCommander.Terminal.Ui;

/// <summary>
/// Inline scrollback search bar - modeled on <see cref="WinForms.FindReplaceBar"/> (always present
/// in the control tree, toggled via Visible, never a modal dialog), searching plain substrings
/// across every combined-space line (scrollback + active screen) via
/// <see cref="TerminalCanvas.GetCombinedLineText"/> rather than a text buffer.
/// </summary>
internal sealed class TerminalFindBar : Panel
{
    private readonly TerminalCanvas _canvas;
    private readonly TextBox _findBox;
    private readonly Label _matchCountLabel;
    private readonly ThemedCheckBox _matchCaseCheck;

    private readonly List<(int Line, int Col, int Length)> _matches = new();
    private int _currentIndex = -1;

    public TerminalFindBar(TerminalCanvas canvas)
    {
        _canvas = canvas;

        Dock = DockStyle.Top;
        Visible = false;
        AutoSize = true;
        Padding = new Padding(6, 4, 6, 4);

        var L = LocalizationService.Current;

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _findBox = UiHelpers.CreateTextBox();
        _findBox.Width = 220;
        _findBox.Margin = new Padding(0, 3, 6, 3);
        _findBox.TextChanged += (_, _) => Rescan();
        _findBox.KeyDown += OnFindBoxKeyDown;

        var prevBtn = ThemedForm.CreateThemedButton("▲");
        prevBtn.Margin = new Padding(0, 0, 4, 0);
        prevBtn.Click += (_, _) => GoTo(_currentIndex - 1);

        var nextBtn = ThemedForm.CreateThemedButton("▼");
        nextBtn.Margin = new Padding(0, 0, 10, 0);
        nextBtn.Click += (_, _) => GoTo(_currentIndex + 1);

        _matchCaseCheck = UiHelpers.CreateCheckBox(L.GetString("Edit.FindBar.MatchCase"));
        _matchCaseCheck.AutoSize = true;
        _matchCaseCheck.Margin = new Padding(0, 6, 10, 0);
        _matchCaseCheck.CheckedChanged += (_, _) => Rescan();

        var closeBtn = ThemedForm.CreateThemedButton("✕");
        closeBtn.Margin = new Padding(0, 0, 10, 0);
        closeBtn.Click += (_, _) => CloseBar();

        _matchCountLabel = UiHelpers.CreateLabel("");
        _matchCountLabel.Margin = new Padding(0, 8, 0, 0);

        row.Controls.Add(_findBox);
        row.Controls.Add(prevBtn);
        row.Controls.Add(nextBtn);
        row.Controls.Add(_matchCaseCheck);
        row.Controls.Add(closeBtn);
        row.Controls.Add(_matchCountLabel);

        Controls.Add(row);

        ApplyTheme();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        if (IsDisposed) return;
        var p = ThemeService.Current;
        BackColor = p.HeaderBackground;
        // TerminalFindBar is itself a Panel, so ControlThemer's generic traversal also visits it
        // directly - tag it so that pass keeps re-applying HeaderBackground too, instead of
        // falling back to the plain Background it uses for untagged panels.
        Tag = ThemeRole.HeaderBackground;
        _matchCountLabel.ForeColor = p.DimForeground;
    }

    /// <summary>Shows the bar, focuses the search box, and re-runs the current pattern if one is
    /// already entered.</summary>
    public void ShowBar()
    {
        Visible = true;
        if (!_findBox.Focused)
        {
            _findBox.Focus();
            _findBox.SelectAll();
        }
        if (!string.IsNullOrEmpty(_findBox.Text))
            Rescan();
    }

    /// <summary>Hides the bar, clears match highlights, and returns focus to the canvas.</summary>
    public void CloseBar()
    {
        Visible = false;
        _matches.Clear();
        _currentIndex = -1;
        _canvas.SetFindHighlights(null, -1);
        _canvas.Focus();
    }

    private void Rescan()
    {
        _matches.Clear();
        _currentIndex = -1;

        var pattern = _findBox.Text;
        if (pattern.Length > 0)
        {
            var comparison = _matchCaseCheck.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var lineCount = _canvas.CombinedLineCount();
            for (var line = 0; line < lineCount; line++)
            {
                string text;
                try
                {
                    text = _canvas.GetCombinedLineText(line);
                }
                catch
                {
                    // The screen buffer was mutated (scrollback cleared, terminal resized)
                    // between CombinedLineCount() and this line access — the line index is
                    // no longer valid. Bail out of the scan with whatever matches we have.
                    break;
                }
                var searchFrom = 0;
                while (searchFrom <= text.Length - pattern.Length)
                {
                    var idx = text.IndexOf(pattern, searchFrom, comparison);
                    if (idx < 0) break;
                    _matches.Add((line, idx, pattern.Length));
                    searchFrom = idx + Math.Max(1, pattern.Length);
                }
            }
        }

        if (_matches.Count > 0)
            _currentIndex = 0;

        _canvas.SetFindHighlights(_matches, _currentIndex);
        if (_currentIndex >= 0)
            _canvas.ScrollToCombinedLine(_matches[_currentIndex].Line);
        UpdateMatchCountLabel();
    }

    private void GoTo(int index)
    {
        if (_matches.Count == 0) return;
        _currentIndex = ((index % _matches.Count) + _matches.Count) % _matches.Count;

        _canvas.SetFindHighlights(_matches, _currentIndex);
        _canvas.ScrollToCombinedLine(_matches[_currentIndex].Line);
        UpdateMatchCountLabel();
    }

    private void UpdateMatchCountLabel()
    {
        var L = LocalizationService.Current;
        if (_matches.Count == 0)
        {
            _matchCountLabel.Text = string.IsNullOrEmpty(_findBox.Text) ? "" : L.GetString("Edit.NotFound");
            return;
        }
        _matchCountLabel.Text = L.GetString("Edit.FindBar.MatchCount", _currentIndex + 1, _matches.Count);
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            GoTo(e.Shift ? _currentIndex - 1 : _currentIndex + 1);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            CloseBar();
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeService.ThemeChanged -= OnThemeChanged;
            _findBox?.Dispose();
            _matchCaseCheck?.Dispose();
            _matchCountLabel?.Dispose();
        }
        base.Dispose(disposing);
    }
}
