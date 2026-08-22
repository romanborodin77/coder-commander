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
public sealed class ToolbarButtonsForm : ThemedForm
{
    /// <summary>One row in either list box - <see cref="ToString"/> is what the ListBox actually
    /// displays (no separate DisplayMember wiring needed).</summary>
    private sealed record Entry(string CommandId, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private readonly bool _isFunctionBar;
    private readonly ListBox _available;
    private readonly ListBox _current;
    private readonly Button _addBtn;
    private readonly Button _removeBtn;
    private readonly Button _addSeparatorBtn;
    private readonly Button _upBtn;
    private readonly Button _downBtn;
    private readonly Button _resetBtn;

    public ToolbarButtonsForm(bool isFunctionBar)
    {
        _isFunctionBar = isFunctionBar;
        var L = LocalizationService.Current;

        Text = L.GetString(isFunctionBar ? "Settings.Toolbar.EditFunctionBar" : "Settings.Toolbar.EditToolbar");
        ClientSize = new Size(640, 420);
        Resizable = true;
        MinimumSize = new Size(520, 320);

        _available = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _current = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        _available.SelectedIndexChanged += (_, _) => UpdateButtonState();
        _current.SelectedIndexChanged += (_, _) => UpdateButtonState();
        _available.DoubleClick += (_, _) => AddSelected();
        _current.DoubleClick += (_, _) => RemoveSelected();

        var availableGroup = LabeledGroup(L.GetString("Settings.Toolbar.Available"), _available);
        var currentGroup = LabeledGroup(L.GetString("Settings.Toolbar.Current"), _current);

        _addBtn = CreateThemedButton(L.GetString("Settings.Toolbar.Add"));
        _addBtn.Click += (_, _) => AddSelected();
        _removeBtn = CreateThemedButton(L.GetString("Settings.Toolbar.Remove"));
        _removeBtn.Click += (_, _) => RemoveSelected();
        _addSeparatorBtn = CreateThemedButton(L.GetString("Settings.Toolbar.AddSeparator"));
        _addSeparatorBtn.Click += (_, _) => AddSeparator();
        _addSeparatorBtn.Visible = !isFunctionBar;
        _upBtn = CreateThemedButton("▲");
        _upBtn.Click += (_, _) => MoveSelected(-1);
        _downBtn = CreateThemedButton("▼");
        _downBtn.Click += (_, _) => MoveSelected(1);

        var middleColumn = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
        };
        foreach (var btn in new[] { _addBtn, _removeBtn, _addSeparatorBtn })
        {
            btn.Width = 96;
            btn.Margin = new Padding(4, 8, 4, 0);
            middleColumn.Controls.Add(btn);
        }

        var rightColumn = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom,
        };
        foreach (var btn in new[] { _upBtn, _downBtn })
        {
            btn.Width = 40;
            btn.Margin = new Padding(4, 8, 4, 0);
            rightColumn.Controls.Add(btn);
        }

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 4,
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(availableGroup, 0, 0);
        layout.Controls.Add(middleColumn, 1, 0);
        layout.Controls.Add(currentGroup, 2, 0);
        layout.Controls.Add(rightColumn, 3, 0);

        _resetBtn = CreateThemedButton(L.GetString("Settings.Toolbar.ResetDefault"));
        _resetBtn.Click += (_, _) => ResetToDefault();

        var saveBtn = CreateThemedButton(L.GetString("Common.Save"), accent: true);
        saveBtn.Click += (_, _) => SaveAndClose();

        var closeBtn = CreateThemedButton(L.GetString("Common.Cancel"));
        closeBtn.Click += (_, _) => Close();

        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        saveBtn.Margin = new Padding(0, 0, 8, 0);
        rightGroup.Controls.Add(closeBtn);
        rightGroup.Controls.Add(saveBtn);

        var leftGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        leftGroup.Controls.Add(_resetBtn);

        var buttonBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(16, 10, 16, 10),
        };
        buttonBar.SetRole(ThemeRole.HeaderBackground);
        buttonBar.Controls.Add(rightGroup);
        buttonBar.Controls.Add(leftGroup);

        Controls.Add(layout);
        Controls.Add(buttonBar);

        CancelButton = closeBtn;
        Load += (_, _) => LoadLayout();
    }

    private static Panel LabeledGroup(string title, Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var label = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = title,
        };
        label.SetRole(ThemeRole.Section);
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(label);
        return panel;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _available?.Dispose();
            _current?.Dispose();
            _addBtn?.Dispose();
            _removeBtn?.Dispose();
            _addSeparatorBtn?.Dispose();
            _upBtn?.Dispose();
            _downBtn?.Dispose();
            _resetBtn?.Dispose();
        }
        base.Dispose(disposing);
    }
}
