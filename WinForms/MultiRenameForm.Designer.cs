using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class MultiRenameForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Label _lblPattern = null!;
    private TextBox _patternBox = null!;
    private Label _lblExt = null!;
    private TextBox _extBox = null!;
    private Label _lblStart = null!;
    private FlowLayoutPanel _counterPanel = null!;
    private NumericUpDown _startIndex = null!;
    private Label _lblStep = null!;
    private NumericUpDown _stepIndex = null!;
    private Label _lblFind = null!;
    private TextBox _findBox = null!;
    private Label _lblReplace = null!;
    private TableLayoutPanel _replacePanel = null!;
    private TextBox _replaceBox = null!;
    private ThemedCheckBox _regexCheck = null!;
    private Label _hintLabel = null!;
    private Panel _spacer = null!;
    private ListView _previewList = null!;
    private ColumnHeader _colOldName = null!;
    private ColumnHeader _colNewName = null!;
    private ColumnHeader _colStatus = null!;
    private Panel _bottomPanel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _okBtn = null!;
    private RoundedButton _cancelBtn = null!;
    private RoundedButton _resetBtn = null!;
    private FlowLayoutPanel _leftGroup = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _patternBox?.Dispose();
            _extBox?.Dispose();
            _startIndex?.Dispose();
            _stepIndex?.Dispose();
            _findBox?.Dispose();
            _replaceBox?.Dispose();
            _regexCheck?.Dispose();
            _previewList?.Dispose();
            _lblPattern?.Dispose();
            _lblExt?.Dispose();
            _lblStart?.Dispose();
            _lblStep?.Dispose();
            _lblFind?.Dispose();
            _lblReplace?.Dispose();
            _hintLabel?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _resetBtn?.Dispose();
            _leftGroup?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _spacer?.Dispose();
            _replacePanel?.Dispose();
            _counterPanel?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Column captions are localized in the constructor - a
    /// <see cref="ColumnHeader"/> is not a <see cref="Control"/> and cannot carry a
    /// LocalizationKey.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _layout = new TableLayoutPanel();
        _lblPattern = new Label();
        _patternBox = new TextBox();
        _lblExt = new Label();
        _extBox = new TextBox();
        _lblStart = new Label();
        _counterPanel = new FlowLayoutPanel();
        _startIndex = new NumericUpDown();
        _lblStep = new Label();
        _stepIndex = new NumericUpDown();
        _lblFind = new Label();
        _findBox = new TextBox();
        _lblReplace = new Label();
        _replacePanel = new TableLayoutPanel();
        _replaceBox = new TextBox();
        _regexCheck = new ThemedCheckBox();
        _hintLabel = new Label();
        _spacer = new Panel();
        _previewList = new ListView();
        _colOldName = new ColumnHeader();
        _colNewName = new ColumnHeader();
        _colStatus = new ColumnHeader();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _okBtn = new RoundedButton();
        _cancelBtn = new RoundedButton();
        _resetBtn = new RoundedButton();
        _leftGroup = new FlowLayoutPanel();
        ((System.ComponentModel.ISupportInitialize)_startIndex).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_stepIndex).BeginInit();
        _layout.SuspendLayout();
        _counterPanel.SuspendLayout();
        _replacePanel.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _leftGroup.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 2;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_lblPattern, 0, 0);
        _layout.Controls.Add(_patternBox, 1, 0);
        _layout.Controls.Add(_lblExt, 0, 1);
        _layout.Controls.Add(_extBox, 1, 1);
        _layout.Controls.Add(_lblStart, 0, 2);
        _layout.Controls.Add(_counterPanel, 1, 2);
        _layout.Controls.Add(_lblFind, 0, 3);
        _layout.Controls.Add(_findBox, 1, 3);
        _layout.Controls.Add(_lblReplace, 0, 4);
        _layout.Controls.Add(_replacePanel, 1, 4);
        _layout.Controls.Add(_hintLabel, 0, 5);
        _layout.Controls.Add(_spacer, 0, 6);
        _layout.Controls.Add(_previewList, 0, 7);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(16, 16, 16, 8);
        _layout.RowCount = 8;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _layout.SetColumnSpan(_hintLabel, 2);
        _layout.SetColumnSpan(_spacer, 2);
        _layout.SetColumnSpan(_previewList, 2);
        _uiMetadata.SetThemeRole(_layout, ThemeRole.Background);
        //
        // _lblPattern
        //
        _lblPattern.AutoSize = true;
        _lblPattern.Dock = DockStyle.Fill;
        _lblPattern.Name = "_lblPattern";
        _lblPattern.Text = "Name pattern";
        _lblPattern.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_lblPattern, "MultiRename.Pattern");
        _uiMetadata.SetThemeRole(_lblPattern, ThemeRole.Emphasis);
        //
        // _patternBox
        //
        _patternBox.BorderStyle = BorderStyle.FixedSingle;
        _patternBox.Dock = DockStyle.Fill;
        _patternBox.Name = "_patternBox";
        _patternBox.Text = "[N]";
        //
        // _lblExt
        //
        _lblExt.AutoSize = true;
        _lblExt.Dock = DockStyle.Fill;
        _lblExt.Name = "_lblExt";
        _lblExt.Text = "Extension";
        _lblExt.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_lblExt, "MultiRename.Extension");
        _uiMetadata.SetThemeRole(_lblExt, ThemeRole.Emphasis);
        //
        // _extBox
        //
        _extBox.BorderStyle = BorderStyle.FixedSingle;
        _extBox.Dock = DockStyle.Fill;
        _extBox.Name = "_extBox";
        _extBox.Text = "[E]";
        //
        // _lblStart
        //
        _lblStart.AutoSize = true;
        _lblStart.Dock = DockStyle.Fill;
        _lblStart.Name = "_lblStart";
        _lblStart.Text = "Start at";
        _lblStart.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_lblStart, "MultiRename.StartAt");
        _uiMetadata.SetThemeRole(_lblStart, ThemeRole.Emphasis);
        //
        // _counterPanel
        //
        // A FlowLayoutPanel, not a plain Panel: a plain Panel does not position undocked children at
        // all, so all three used to land on top of each other at (0,0). Margin only takes effect on
        // children of a layout panel.
        //
        // Add order is visual left-to-right in a FlowLayoutPanel, unlike Dock which goes from the
        // highest Controls index down.
        _counterPanel.BackColor = Color.Transparent;
        _counterPanel.Controls.Add(_startIndex);
        _counterPanel.Controls.Add(_lblStep);
        _counterPanel.Controls.Add(_stepIndex);
        _counterPanel.Dock = DockStyle.Fill;
        _counterPanel.FlowDirection = FlowDirection.LeftToRight;
        _counterPanel.Name = "_counterPanel";
        _counterPanel.WrapContents = false;
        //
        // _startIndex
        //
        _startIndex.BorderStyle = BorderStyle.FixedSingle;
        _startIndex.Margin = new Padding(0, 5, 0, 0);
        _startIndex.Maximum = 999999;
        _startIndex.Name = "_startIndex";
        _startIndex.Size = new Size(80, 23);
        _startIndex.Value = 1;
        //
        // _lblStep
        //
        _lblStep.AutoSize = true;
        _lblStep.Margin = new Padding(16, 9, 4, 0);
        _lblStep.Name = "_lblStep";
        _lblStep.Text = "Step";
        _lblStep.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_lblStep, "MultiRename.Step");
        _uiMetadata.SetThemeRole(_lblStep, ThemeRole.Body);
        //
        // _stepIndex
        //
        _stepIndex.BorderStyle = BorderStyle.FixedSingle;
        _stepIndex.Margin = new Padding(0, 5, 0, 0);
        _stepIndex.Maximum = 999;
        _stepIndex.Minimum = 1;
        _stepIndex.Name = "_stepIndex";
        _stepIndex.Size = new Size(60, 23);
        _stepIndex.Value = 1;
        //
        // _lblFind
        //
        // Find/Replace is an extra pass applied to the name AFTER placeholder substitution (the
        // order "Advanced Renamer"-style tools use), not a placeholder itself. An empty Find is a
        // no-op, so patterns using only [N]/[C] are unaffected by default.
        _lblFind.AutoSize = true;
        _lblFind.Dock = DockStyle.Fill;
        _lblFind.Name = "_lblFind";
        _lblFind.Text = "Find";
        _lblFind.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_lblFind, "MultiRename.Find");
        _uiMetadata.SetThemeRole(_lblFind, ThemeRole.Emphasis);
        //
        // _findBox
        //
        _findBox.BorderStyle = BorderStyle.FixedSingle;
        _findBox.Dock = DockStyle.Fill;
        _findBox.Name = "_findBox";
        //
        // _lblReplace
        //
        _lblReplace.AutoSize = true;
        _lblReplace.Dock = DockStyle.Fill;
        _lblReplace.Name = "_lblReplace";
        _lblReplace.Text = "Replace with";
        _lblReplace.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_lblReplace, "MultiRename.Replace");
        _uiMetadata.SetThemeRole(_lblReplace, ThemeRole.Emphasis);
        //
        // _replacePanel
        //
        _replacePanel.BackColor = Color.Transparent;
        _replacePanel.ColumnCount = 2;
        _replacePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _replacePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _replacePanel.Controls.Add(_replaceBox, 0, 0);
        _replacePanel.Controls.Add(_regexCheck, 1, 0);
        _replacePanel.Dock = DockStyle.Fill;
        _replacePanel.Name = "_replacePanel";
        _replacePanel.RowCount = 1;
        _replacePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _replaceBox
        //
        _replaceBox.BorderStyle = BorderStyle.FixedSingle;
        _replaceBox.Dock = DockStyle.Fill;
        _replaceBox.Name = "_replaceBox";
        //
        // _regexCheck
        //
        _regexCheck.AutoSize = true;
        _regexCheck.Margin = new Padding(12, 5, 0, 0);
        _regexCheck.Name = "_regexCheck";
        _regexCheck.Text = "Regular expression";
        _uiMetadata.SetLocalizationKey(_regexCheck, "MultiRename.UseRegex");
        //
        // _hintLabel
        //
        // AutoEllipsis, and therefore AutoSize=false: at AutoSize=true this label word-wrapped
        // inside a row only one line tall, so everything past the wrap point was not truncated
        // but silently dropped - no ellipsis, no clue anything was missing. Same treatment
        // HotkeyBindingsForm._hint and TerminalKeyBindingsForm._hint already use.
        _hintLabel.AutoEllipsis = true;
        _hintLabel.AutoSize = false;
        _hintLabel.Dock = DockStyle.Fill;
        _hintLabel.Name = "_hintLabel";
        _hintLabel.Text = "[N] name, [E] extension, [C] counter";
        _hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_hintLabel, "MultiRename.Hint");
        _uiMetadata.SetThemeRole(_hintLabel, ThemeRole.Muted);
        //
        // _spacer
        //
        _spacer.Dock = DockStyle.Fill;
        _spacer.Name = "_spacer";
        _uiMetadata.SetThemeRole(_spacer, ThemeRole.Background);
        //
        // _previewList
        //
        _previewList.BorderStyle = BorderStyle.None;
        _previewList.Columns.AddRange(new[] { _colOldName, _colNewName, _colStatus });
        _previewList.Dock = DockStyle.Fill;
        _previewList.FullRowSelect = true;
        _previewList.Name = "_previewList";
        _previewList.UseCompatibleStateImageBehavior = false;
        _previewList.View = View.Details;
        //
        // _colOldName
        //
        _colOldName.Text = "Old name";
        _colOldName.Width = 260;
        //
        // _colNewName
        //
        _colNewName.Text = "New name";
        _colNewName.Width = 260;
        //
        // _colStatus
        //
        _colStatus.Text = "Status";
        _colStatus.Width = 80;
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Controls.Add(_leftGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(720, 50);
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        //
        // _buttonGroup
        //
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonGroup.BackColor = Color.Transparent;
        _buttonGroup.Controls.Add(_cancelBtn);
        _buttonGroup.Controls.Add(_okBtn);
        _buttonGroup.Dock = DockStyle.Right;
        _buttonGroup.FlowDirection = FlowDirection.LeftToRight;
        _buttonGroup.Name = "_buttonGroup";
        _buttonGroup.WrapContents = false;
        //
        // _cancelBtn
        //
        _cancelBtn.AutoSize = true;
        _cancelBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Margin = new Padding(0, 0, 8, 0);
        _cancelBtn.MinimumSize = new Size(100, 32);
        _cancelBtn.Name = "_cancelBtn";
        _cancelBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelBtn.Role = ThemeRole.SecondaryButton;
        _cancelBtn.Text = "Cancel";
        _uiMetadata.SetLocalizationKey(_cancelBtn, "Common.Cancel");
        //
        // _okBtn
        //
        // AutoSize, never a fixed Width: a hardcoded 100 truncated "Переименовать" to
        // "Переим..." under Russian.
        _okBtn.AutoSize = true;
        _okBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.Margin = new Padding(0);
        _okBtn.MinimumSize = new Size(100, 32);
        _okBtn.Name = "_okBtn";
        _okBtn.Padding = new Padding(20, 0, 20, 0);
        _okBtn.Role = ThemeRole.PrimaryButton;
        _okBtn.Text = "Rename";
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.Rename");
        //
        // _leftGroup
        //
        // Docking _resetBtn straight into _bottomPanel would stretch it to that panel's
        // inner height (50 less 8px of padding top and bottom = 34px), leaving it
        // visibly taller than the 32px buttons in _buttonGroup. A left-docked FlowLayoutPanel
        // lets the button keep its natural size, and honours the Margin that Dock
        // ignores outright - the same shape ConnectionsForm uses for its own Close.
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
        _resetBtn.AutoSize = true;
        _resetBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _resetBtn.Margin = new Padding(0);
        _resetBtn.MinimumSize = new Size(100, 32);
        _resetBtn.Name = "_resetBtn";
        _resetBtn.Padding = new Padding(20, 0, 20, 0);
        _resetBtn.Role = ThemeRole.SecondaryButton;
        _resetBtn.Text = "Reset";
        _uiMetadata.SetLocalizationKey(_resetBtn, "Common.Reset");
        //
        // MultiRenameForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(720, 520);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        MinimumSize = new Size(600, 420);
        Name = "MultiRenameForm";
        Text = "Multi-rename";
        _uiMetadata.SetLocalizationKey(this, "MultiRename.Title");
        ((System.ComponentModel.ISupportInitialize)_startIndex).EndInit();
        ((System.ComponentModel.ISupportInitialize)_stepIndex).EndInit();
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _counterPanel.ResumeLayout(false);
        _counterPanel.PerformLayout();
        _replacePanel.ResumeLayout(false);
        _replacePanel.PerformLayout();
        _leftGroup.ResumeLayout(false);
        _bottomPanel.ResumeLayout(false);
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
