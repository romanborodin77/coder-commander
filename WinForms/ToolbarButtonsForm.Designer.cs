using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class ToolbarButtonsForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Panel _availableGroup = null!;
    private ListBox _available = null!;
    private Label _availableLabel = null!;
    private FlowLayoutPanel _middleColumn = null!;
    private RoundedButton _addBtn = null!;
    private RoundedButton _removeBtn = null!;
    private RoundedButton _addSeparatorBtn = null!;
    private Panel _currentGroup = null!;
    private ListBox _current = null!;
    private Label _currentLabel = null!;
    private FlowLayoutPanel _rightColumn = null!;
    private RoundedButton _upBtn = null!;
    private RoundedButton _downBtn = null!;
    private Panel _buttonBar = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _closeBtn = null!;
    private RoundedButton _saveBtn = null!;
    private FlowLayoutPanel _leftGroup = null!;
    private RoundedButton _resetBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _available?.Dispose();
            _current?.Dispose();
            _availableLabel?.Dispose();
            _currentLabel?.Dispose();
            _addBtn?.Dispose();
            _removeBtn?.Dispose();
            _addSeparatorBtn?.Dispose();
            _upBtn?.Dispose();
            _downBtn?.Dispose();
            _resetBtn?.Dispose();
            _saveBtn?.Dispose();
            _closeBtn?.Dispose();
            _availableGroup?.Dispose();
            _currentGroup?.Dispose();
            _middleColumn?.Dispose();
            _rightColumn?.Dispose();
            _leftGroup?.Dispose();
            _rightGroup?.Dispose();
            _buttonBar?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The two labelled groups came from a shared <c>LabeledGroup</c> factory
    /// and are written out here; both list boxes are filled at runtime from
    /// <c>ToolbarButtonCatalog</c> and the saved settings.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _layout = new TableLayoutPanel();
        _availableGroup = new Panel();
        _available = new ListBox();
        _availableLabel = new Label();
        _middleColumn = new FlowLayoutPanel();
        _addBtn = new RoundedButton();
        _removeBtn = new RoundedButton();
        _addSeparatorBtn = new RoundedButton();
        _currentGroup = new Panel();
        _current = new ListBox();
        _currentLabel = new Label();
        _rightColumn = new FlowLayoutPanel();
        _upBtn = new RoundedButton();
        _downBtn = new RoundedButton();
        _buttonBar = new Panel();
        _rightGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        _saveBtn = new RoundedButton();
        _leftGroup = new FlowLayoutPanel();
        _resetBtn = new RoundedButton();
        _layout.SuspendLayout();
        _availableGroup.SuspendLayout();
        _middleColumn.SuspendLayout();
        _currentGroup.SuspendLayout();
        _rightColumn.SuspendLayout();
        _buttonBar.SuspendLayout();
        _rightGroup.SuspendLayout();
        _leftGroup.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 4;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _layout.Controls.Add(_availableGroup, 0, 0);
        _layout.Controls.Add(_middleColumn, 1, 0);
        _layout.Controls.Add(_currentGroup, 2, 0);
        _layout.Controls.Add(_rightColumn, 3, 0);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(12);
        _layout.RowCount = 1;
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _availableGroup
        //
        // Fill added before Top: WinForms docks from the highest Controls index down, so the
        // last-added caption lands above the list rather than over it.
        _availableGroup.Controls.Add(_available);
        _availableGroup.Controls.Add(_availableLabel);
        _availableGroup.Dock = DockStyle.Fill;
        _availableGroup.Name = "_availableGroup";
        //
        // _available
        //
        _available.Dock = DockStyle.Fill;
        _available.IntegralHeight = false;
        _available.Name = "_available";
        //
        // _availableLabel
        //
        _availableLabel.Dock = DockStyle.Top;
        _availableLabel.Name = "_availableLabel";
        _availableLabel.Size = new Size(100, 22);
        _availableLabel.Text = "Available";
        _uiMetadata.SetLocalizationKey(_availableLabel, "Settings.Toolbar.Available");
        _uiMetadata.SetThemeRole(_availableLabel, ThemeRole.Section);
        //
        // _middleColumn
        //
        _middleColumn.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        _middleColumn.AutoSize = true;
        _middleColumn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _middleColumn.Controls.Add(_addBtn);
        _middleColumn.Controls.Add(_removeBtn);
        _middleColumn.Controls.Add(_addSeparatorBtn);
        _middleColumn.Dock = DockStyle.Fill;
        _middleColumn.FlowDirection = FlowDirection.TopDown;
        _middleColumn.Name = "_middleColumn";
        _middleColumn.WrapContents = false;
        //
        // The three transfer buttons carry no fixed Width. "Add Separator" localizes to "Добавить
        // разделитель", which needs ~140px at the 9pt UI font - at the flat 96px this column used
        // to hardcode, a third of that caption was simply cut off. AutoSize sizes each to its own
        // caption; ToolbarButtonsForm's constructor then raises all three MinimumSize to the widest
        // preferred width so the column still reads as one aligned stack rather than a ragged edge.
        //
        // _addBtn
        //
        _addBtn.AutoSize = true;
        _addBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _addBtn.Margin = new Padding(4, 8, 4, 0);
        _addBtn.MinimumSize = new Size(96, 32);
        _addBtn.Name = "_addBtn";
        _addBtn.Padding = new Padding(12, 0, 12, 0);
        _addBtn.Role = ThemeRole.SecondaryButton;
        _addBtn.Text = "Add →";
        _uiMetadata.SetLocalizationKey(_addBtn, "Settings.Toolbar.Add");
        //
        // _removeBtn
        //
        _removeBtn.AutoSize = true;
        _removeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _removeBtn.Margin = new Padding(4, 8, 4, 0);
        _removeBtn.MinimumSize = new Size(96, 32);
        _removeBtn.Name = "_removeBtn";
        _removeBtn.Padding = new Padding(12, 0, 12, 0);
        _removeBtn.Role = ThemeRole.SecondaryButton;
        _removeBtn.Text = "← Remove";
        _uiMetadata.SetLocalizationKey(_removeBtn, "Settings.Toolbar.Remove");
        //
        // _addSeparatorBtn
        //
        // Hidden for the function bar, which has no separators - the constructor decides.
        _addSeparatorBtn.AutoSize = true;
        _addSeparatorBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _addSeparatorBtn.Margin = new Padding(4, 8, 4, 0);
        _addSeparatorBtn.MinimumSize = new Size(96, 32);
        _addSeparatorBtn.Name = "_addSeparatorBtn";
        _addSeparatorBtn.Padding = new Padding(12, 0, 12, 0);
        _addSeparatorBtn.Role = ThemeRole.SecondaryButton;
        _addSeparatorBtn.Text = "Separator";
        _uiMetadata.SetLocalizationKey(_addSeparatorBtn, "Settings.Toolbar.AddSeparator");
        //
        // _currentGroup
        //
        _currentGroup.Controls.Add(_current);
        _currentGroup.Controls.Add(_currentLabel);
        _currentGroup.Dock = DockStyle.Fill;
        _currentGroup.Name = "_currentGroup";
        //
        // _current
        //
        _current.Dock = DockStyle.Fill;
        _current.IntegralHeight = false;
        _current.Name = "_current";
        //
        // _currentLabel
        //
        _currentLabel.Dock = DockStyle.Top;
        _currentLabel.Name = "_currentLabel";
        _currentLabel.Size = new Size(100, 22);
        _currentLabel.Text = "Current";
        _uiMetadata.SetLocalizationKey(_currentLabel, "Settings.Toolbar.Current");
        _uiMetadata.SetThemeRole(_currentLabel, ThemeRole.Section);
        //
        // _rightColumn
        //
        _rightColumn.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        _rightColumn.AutoSize = true;
        _rightColumn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightColumn.Controls.Add(_upBtn);
        _rightColumn.Controls.Add(_downBtn);
        _rightColumn.Dock = DockStyle.Fill;
        _rightColumn.FlowDirection = FlowDirection.TopDown;
        _rightColumn.Name = "_rightColumn";
        _rightColumn.WrapContents = false;
        //
        // _upBtn
        //
        // Glyphs, not words - no localization key.
        _upBtn.Margin = new Padding(4, 8, 4, 0);
        _upBtn.Name = "_upBtn";
        _upBtn.Role = ThemeRole.SecondaryButton;
        _upBtn.Size = new Size(40, 32);
        _upBtn.Text = "▲";
        //
        // _downBtn
        //
        _downBtn.Margin = new Padding(4, 8, 4, 0);
        _downBtn.Name = "_downBtn";
        _downBtn.Role = ThemeRole.SecondaryButton;
        _downBtn.Size = new Size(40, 32);
        _downBtn.Text = "▼";
        //
        // _buttonBar
        //
        // Right group added before Left: both are edge-docked and the last-added claims its edge
        // first.
        _buttonBar.Controls.Add(_rightGroup);
        _buttonBar.Controls.Add(_leftGroup);
        _buttonBar.Dock = DockStyle.Bottom;
        _buttonBar.Name = "_buttonBar";
        _buttonBar.Padding = new Padding(16, 10, 16, 10);
        _buttonBar.Size = new Size(640, 56);
        _uiMetadata.SetThemeRole(_buttonBar, ThemeRole.HeaderBackground);
        //
        // _rightGroup
        //
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_closeBtn);
        _rightGroup.Controls.Add(_saveBtn);
        _rightGroup.Dock = DockStyle.Right;
        _rightGroup.FlowDirection = FlowDirection.LeftToRight;
        _rightGroup.Name = "_rightGroup";
        _rightGroup.WrapContents = false;
        //
        // _closeBtn
        //
        _closeBtn.AutoSize = true;
        _closeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _closeBtn.MinimumSize = new Size(100, 32);
        _closeBtn.Name = "_closeBtn";
        _closeBtn.Padding = new Padding(20, 0, 20, 0);
        _closeBtn.Role = ThemeRole.SecondaryButton;
        _closeBtn.Text = "Cancel";
        _uiMetadata.SetLocalizationKey(_closeBtn, "Common.Cancel");
        //
        // _saveBtn
        //
        _saveBtn.AutoSize = true;
        _saveBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _saveBtn.Margin = new Padding(0, 0, 8, 0);
        _saveBtn.MinimumSize = new Size(100, 32);
        _saveBtn.Name = "_saveBtn";
        _saveBtn.Padding = new Padding(20, 0, 20, 0);
        _saveBtn.Role = ThemeRole.PrimaryButton;
        _saveBtn.Text = "Save";
        _uiMetadata.SetLocalizationKey(_saveBtn, "Common.Save");
        //
        // _leftGroup
        //
        _leftGroup.AutoSize = true;
        _leftGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _leftGroup.BackColor = Color.Transparent;
        _leftGroup.Controls.Add(_resetBtn);
        _leftGroup.Dock = DockStyle.Left;
        _leftGroup.FlowDirection = FlowDirection.LeftToRight;
        _leftGroup.Name = "_leftGroup";
        _leftGroup.WrapContents = false;
        //
        // _resetBtn
        //
        _resetBtn.AutoSize = true;
        _resetBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _resetBtn.MinimumSize = new Size(100, 32);
        _resetBtn.Name = "_resetBtn";
        _resetBtn.Padding = new Padding(20, 0, 20, 0);
        _resetBtn.Role = ThemeRole.SecondaryButton;
        _resetBtn.Text = "Reset to default";
        _uiMetadata.SetLocalizationKey(_resetBtn, "Settings.Toolbar.ResetDefault");
        //
        // ToolbarButtonsForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(640, 420);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_layout);
        Controls.Add(_buttonBar);
        MinimumSize = new Size(520, 320);
        Name = "ToolbarButtonsForm";
        Text = "Customize toolbar";
        _layout.ResumeLayout(false);
        _availableGroup.ResumeLayout(false);
        _middleColumn.ResumeLayout(false);
        _currentGroup.ResumeLayout(false);
        _rightColumn.ResumeLayout(false);
        _buttonBar.ResumeLayout(false);
        _buttonBar.PerformLayout();
        _rightGroup.ResumeLayout(false);
        _leftGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
