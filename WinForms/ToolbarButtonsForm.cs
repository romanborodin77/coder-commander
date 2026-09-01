using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.WinForms;

/// <summary>
/// F5.2's toolbar customization editor - one instance edits either the main toolbar or the
/// function (F-key) bar, picked by <paramref name="isFunctionBar"/> at construction. Two list
/// boxes (every offerable command on the left, the current ordered layout on the right) with
/// Add/Remove/Move Up/Move Down, plus Add Separator for the main toolbar only (the function bar
/// has never had one). Persists on Save (<see cref="SettingsService.SaveToolbarLayout"/>) - the
/// caller (<c>SettingsForm</c>) is expected to prompt the user to restart, the same way every
/// other toolbar-affecting setting already requires (this app builds its toolbars once, in
/// <c>MainForm</c>'s constructor, not on every settings change).
/// </summary>
public sealed partial class ToolbarButtonsForm : ThemedForm
{
    /// <summary>One row in either list box - <see cref="ToString"/> is what the ListBox actually
    /// displays (no separate DisplayMember wiring needed).</summary>
    private sealed record Entry(string CommandId, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private readonly bool _isFunctionBar;

    public ToolbarButtonsForm(bool isFunctionBar)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _isFunctionBar = isFunctionBar;

        // The window title is one of two keys depending on which bar is being edited, so it cannot
        // travel as a single fixed LocalizationKey.
        Text = LocalizationService.Current.GetString(
            isFunctionBar ? "Settings.Toolbar.EditFunctionBar" : "Settings.Toolbar.EditToolbar");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        // The function bar has no separators.
        _addSeparatorBtn.Visible = !isFunctionBar;

        // Only after ApplyLocalization has put the real captions in place. The three buttons
        // AutoSize to their own text, which alone would leave a ragged column - "Добавить
        // разделитель" is roughly half again as wide as "Добавить →". Raising every MinimumSize to
        // the widest preferred width aligns them without capping any of them: AutoSize can no
        // longer shrink a button below the widest caption, and none of them wants to grow past it.
        var widest = 0;
        foreach (var btn in new[] { _addBtn, _removeBtn, _addSeparatorBtn })
            widest = Math.Max(widest, btn.PreferredSize.Width);
        foreach (var btn in new[] { _addBtn, _removeBtn, _addSeparatorBtn })
            btn.MinimumSize = new Size(widest, btn.MinimumSize.Height);

        _available.SelectedIndexChanged += (_, _) => UpdateButtonState();
        _current.SelectedIndexChanged += (_, _) => UpdateButtonState();
        _available.DoubleClick += (_, _) => AddSelected();
        _current.DoubleClick += (_, _) => RemoveSelected();

        _addBtn.Click += (_, _) => AddSelected();
        _removeBtn.Click += (_, _) => RemoveSelected();
        _addSeparatorBtn.Click += (_, _) => AddSeparator();
        _upBtn.Click += (_, _) => MoveSelected(-1);
        _downBtn.Click += (_, _) => MoveSelected(1);
        _resetBtn.Click += (_, _) => ResetToDefault();
        _saveBtn.Click += (_, _) => SaveAndClose();
        _closeBtn.Click += (_, _) => Close();

        Load += (_, _) => LoadLayout();
    }

    private IReadOnlyList<ToolbarButtonSpec> Catalog =>
        _isFunctionBar ? ToolbarButtonCatalog.FunctionBarCommands : ToolbarButtonCatalog.ToolbarCommands;

    private void LoadLayout()
    {
        var s = SettingsService.Load();
        var saved = _isFunctionBar ? s.FunctionBarButtons : s.ToolbarButtons;
        var layout = saved.Count > 0
            ? saved
            : (_isFunctionBar ? ToolbarButtonCatalog.DefaultFunctionBarLayout : ToolbarButtonCatalog.DefaultToolbarLayout);
        PopulateFrom(layout);
    }

    private void PopulateFrom(IReadOnlyList<string> layout)
    {
        var L = LocalizationService.Current;

        _available.Items.Clear();
        foreach (var spec in Catalog)
            _available.Items.Add(new Entry(spec.CommandId, L.GetString(spec.LabelKey)));

        _current.Items.Clear();
        foreach (var entry in layout)
        {
            if (entry == ToolbarButtonCatalog.Separator)
            {
                _current.Items.Add(new Entry(ToolbarButtonCatalog.Separator, L.GetString("Settings.Toolbar.Separator")));
                continue;
            }
            var spec = _isFunctionBar
                ? ToolbarButtonCatalog.FindFunctionBarCommand(entry)
                : ToolbarButtonCatalog.FindToolbarCommand(entry);
            if (spec is { } s)
                _current.Items.Add(new Entry(s.CommandId, L.GetString(s.LabelKey)));
        }

        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        _addBtn.Enabled = _available.SelectedItem != null;
        _removeBtn.Enabled = _current.SelectedItem != null;
        _upBtn.Enabled = _current.SelectedIndex > 0;
        _downBtn.Enabled = _current.SelectedIndex >= 0 && _current.SelectedIndex < _current.Items.Count - 1;
    }

    private void AddSelected()
    {
        if (_available.SelectedItem is not Entry entry) return;
        var index = _current.SelectedIndex;
        var insertAt = index >= 0 ? index + 1 : _current.Items.Count;
        _current.Items.Insert(insertAt, entry);
        _current.SelectedIndex = insertAt;
    }

    private void AddSeparator()
    {
        var L = LocalizationService.Current;
        var index = _current.SelectedIndex;
        var insertAt = index >= 0 ? index + 1 : _current.Items.Count;
        _current.Items.Insert(insertAt, new Entry(ToolbarButtonCatalog.Separator, L.GetString("Settings.Toolbar.Separator")));
        _current.SelectedIndex = insertAt;
    }

    private void RemoveSelected()
    {
        var index = _current.SelectedIndex;
        if (index < 0) return;
        _current.Items.RemoveAt(index);
        _current.SelectedIndex = Math.Min(index, _current.Items.Count - 1);
    }

    private void MoveSelected(int direction)
    {
        var index = _current.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _current.Items.Count) return;

        var item = _current.Items[index];
        _current.Items.RemoveAt(index);
        _current.Items.Insert(target, item);
        _current.SelectedIndex = target;
    }

    private void ResetToDefault() =>
        PopulateFrom(_isFunctionBar ? ToolbarButtonCatalog.DefaultFunctionBarLayout : ToolbarButtonCatalog.DefaultToolbarLayout);

    private void SaveAndClose()
    {
        var layout = _current.Items.Cast<Entry>().Select(e => e.CommandId).ToList();
        SettingsService.SaveToolbarLayout(_isFunctionBar, layout);

        var L = LocalizationService.Current;
        StyledMessageBox.Show(L.GetString("Settings.Toolbar.RestartNotice"),
            Text, MsgBoxButtons.OK, MsgBoxIcon.Information, this);
        DialogResult = DialogResult.OK;
        Close();
    }

}
