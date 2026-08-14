using CoderCommander.Services;
using CoderCommander.Viewers;

namespace CoderCommander.WinForms;

/// <summary>
/// Inline Ctrl+F find bar for <see cref="ViewerForm"/>. Searches whatever
/// <see cref="IViewerSearchTarget"/> is currently set via <see cref="SetTarget"/> - generalized
/// from a hard binding to a single <see cref="RichTextBox"/> so it can follow the active viewer
/// format (Text/ASCII/Binary/Hex today, a non-<c>RichTextBox</c> content in later phases) instead
/// of being tied to one control for the window's whole lifetime. Modeled on
/// <see cref="FindReplaceBar"/>'s UI shape (docked <see cref="Panel"/>, <c>Dock=Top</c>, shown/
/// hidden via <see cref="Visible"/>, a <see cref="FlowLayoutPanel"/> row of controls) but built
/// fresh - the viewer is read-only (no Replace row).
/// </summary>
internal sealed class ViewerFindBar : Panel
{
    private IViewerSearchTarget? _target;
    private readonly TextBox _findBox;
    private readonly Label _matchCountLabel;
    private readonly ThemedCheckBox _matchCaseCheck;

    private List<int> _matchStarts = new();
    private int _matchLength;
    private int _currentIndex = -1;

    public ViewerFindBar()
    {
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
        prevBtn.Click += (_, _) => GoTo(-1);

        var nextBtn = ThemedForm.CreateThemedButton("▼");
        nextBtn.Margin = new Padding(0, 0, 10, 0);
        nextBtn.Click += (_, _) => GoTo(1);

        _matchCaseCheck = UiHelpers.CreateCheckBox(L.GetString("View.FindBar.MatchCase"));
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
    }

    /// <summary>Applies the current theme to the bar background - same tagging trick as
    /// <see cref="FindReplaceBar"/>: the generic control-tree theming pass visits this
    /// <see cref="Panel"/> directly too, so it needs a role to keep re-applying on every switch.</summary>
    public void ApplyTheme()
    {
        BackColor = ThemeService.Current.HeaderBackground;
        Tag = ThemeRole.HeaderBackground;
    }

    /// <summary>Sets which content this bar searches, called by <c>ViewerForm</c> whenever the
    /// active viewer format changes. Passing <c>null</c> (a format with no
    /// <see cref="IViewerSearchTarget"/>, e.g. Image) closes the bar if it was open - a stale
    /// target from the previous format must never be searched.</summary>
    public void SetTarget(IViewerSearchTarget? target)
    {
        _target = target;
        if (target == null) CloseBar();
    }

    /// <summary>Shows the bar, focuses the search box, and re-runs the current pattern (if any)
    /// against whatever content is now loaded in <see cref="_target"/> - the target's text changes
    /// out from under this bar on every file navigation, so a stale match list must never be
    /// trusted across a reopen. A no-op when there is no searchable target.</summary>
    public void ShowBar()
    {
        if (_target == null) return;
        Visible = true;
        if (!Focused && !_findBox.Focused)
        {
            _findBox.Focus();
            _findBox.SelectAll();
        }
        Rescan();
    }

    /// <summary>Hides the bar and returns focus to the content view. Called on Escape (only after
    /// the bar itself has focus - the viewer's own Escape-closes-the-window handler backs off
    /// while this bar is visible, same precedence <see cref="FindReplaceBar"/>/editor use) and on
    /// every file navigation, since a match list for the previous file's text is meaningless once
    /// the content changes.</summary>
    public void CloseBar()
    {
        Visible = false;
        _matchStarts.Clear();
        _currentIndex = -1;
        _target?.FocusContent();
    }

    /// <summary>Rebuilds the match list from scratch against the target's current text. A plain
    /// linear <see cref="string.IndexOf(string, int, StringComparison)"/> scan is simple and fast
    /// enough at the sizes this bar ever sees (bounded by the loaders' own 16MB text / 1MB
    /// hex-dump caps) - no need for a real search index.</summary>
    private void Rescan()
    {
        _matchStarts.Clear();
        _currentIndex = -1;
        var pattern = _findBox.Text;
        _matchLength = pattern.Length;

        if (pattern.Length == 0 || _target == null)
        {
            UpdateLabel();
            return;
        }

        var text = _target.GetSearchText();
        var comparison = _matchCaseCheck.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var searchFrom = 0;
        while (searchFrom <= text.Length - pattern.Length)
        {
            var found = text.IndexOf(pattern, searchFrom, comparison);
            if (found < 0) break;
            _matchStarts.Add(found);
            searchFrom = found + pattern.Length;
        }

        if (_matchStarts.Count > 0)
        {
            // Land on the match at/after the current caret, not always the first - re-running a
            // search after moving the caret manually should continue from where the user is.
            var caret = _target.CurrentOffset;
            _currentIndex = _matchStarts.FindIndex(s => s >= caret);
            if (_currentIndex < 0) _currentIndex = 0;
            SelectCurrent();
        }
        UpdateLabel();
    }

    private void GoTo(int direction)
    {
        if (_target == null) return;

        if (_matchStarts.Count == 0)
        {
            Rescan();
            if (_matchStarts.Count == 0) return;
        }

        var forward = direction > 0;
        var wrapped = false;
        _currentIndex += direction;
        if (_currentIndex < 0)
        {
            _currentIndex = _matchStarts.Count - 1;
            wrapped = true;
        }
        else if (_currentIndex >= _matchStarts.Count)
        {
            _currentIndex = 0;
            wrapped = true;
        }

        SelectCurrent();
        UpdateLabel(wrapped, forward);
    }

    private void SelectCurrent()
    {
        if (_target == null) return;
        if (_currentIndex < 0 || _currentIndex >= _matchStarts.Count) return;
        _target.SelectRange(_matchStarts[_currentIndex], _matchLength);
    }

    private void UpdateLabel(bool wrapped = false, bool forward = true)
    {
        var L = LocalizationService.Current;
        if (_matchStarts.Count == 0)
        {
            _matchCountLabel.Text = string.IsNullOrEmpty(_findBox.Text) ? "" : L.GetString("View.FindBar.NotFound");
            return;
        }

        var text = L.GetString("View.FindBar.MatchCount", _currentIndex + 1, _matchStarts.Count);
        if (wrapped)
            text += "  •  " + L.GetString(forward ? "View.FindBar.WrappedToTop" : "View.FindBar.WrappedToBottom");
        _matchCountLabel.Text = text;
    }

    private void OnFindBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            GoTo(e.Shift ? -1 : 1);
            e.Handled = e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            CloseBar();
            e.Handled = e.SuppressKeyPress = true;
        }
    }
}
