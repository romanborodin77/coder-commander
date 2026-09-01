using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class CustomShellEditForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Label _nameLabel = null!;
    private TextBox _nameBox = null!;
    private Label _commandLabel = null!;
    private TableLayoutPanel _commandRow = null!;
    private TextBox _commandBox = null!;
    private RoundedButton _browseBtn = null!;
    private Label _commandHint = null!;
    private Label _argumentsLabel = null!;
    private TextBox _argumentsBox = null!;
    private Panel _bottomPanel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _okBtn = null!;
    private RoundedButton _cancelBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _nameBox?.Dispose();
            _commandBox?.Dispose();
            _argumentsBox?.Dispose();
            _browseBtn?.Dispose();
            _nameLabel?.Dispose();
            _commandLabel?.Dispose();
            _commandHint?.Dispose();
            _argumentsLabel?.Dispose();
            _commandRow?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The old constructor built these rows through AddRow/AddTextRow helpers
    /// driven by a <c>ref int row</c> cursor; they are spelled out here because the designer can only
    /// round-trip explicit cell coordinates.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _layout = new TableLayoutPanel();
        _nameLabel = new Label();
        _nameBox = new TextBox();
        _commandLabel = new Label();
        _commandRow = new TableLayoutPanel();
        _commandBox = new TextBox();
        _browseBtn = new RoundedButton();
        _commandHint = new Label();
        _argumentsLabel = new Label();
        _argumentsBox = new TextBox();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _okBtn = new RoundedButton();
        _cancelBtn = new RoundedButton();
        _layout.SuspendLayout();
        _commandRow.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 2;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_nameLabel, 0, 0);
        _layout.Controls.Add(_nameBox, 1, 0);
        _layout.Controls.Add(_commandLabel, 0, 1);
        _layout.Controls.Add(_commandRow, 1, 1);
        // The hint sits in the value column only - row 2 has no caption of its own.
        _layout.Controls.Add(_commandHint, 1, 2);
        _layout.Controls.Add(_argumentsLabel, 0, 3);
        _layout.Controls.Add(_argumentsBox, 1, 3);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(20, 16, 20, 8);
        _layout.RowCount = 5;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_layout, ThemeRole.Background);
        //
        // _nameLabel
        //
        _nameLabel.AutoSize = true;
        _nameLabel.Dock = DockStyle.Fill;
        _nameLabel.Name = "_nameLabel";
        _nameLabel.Text = "Name";
        _nameLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_nameLabel, "Settings.Terminal.CustomShells.Name");
        _uiMetadata.SetThemeRole(_nameLabel, ThemeRole.Body);
        //
        // _nameBox
        //
        _nameBox.BorderStyle = BorderStyle.FixedSingle;
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Name = "_nameBox";
        //
        // _commandLabel
        //
        _commandLabel.AutoSize = true;
        _commandLabel.Dock = DockStyle.Fill;
        _commandLabel.Name = "_commandLabel";
        _commandLabel.Text = "Command";
        _commandLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_commandLabel, "Settings.Terminal.CustomShells.Command");
        _uiMetadata.SetThemeRole(_commandLabel, ThemeRole.Body);
        //
        // _commandRow
        //
        _commandRow.ColumnCount = 2;
        _commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // AutoSize, not a fixed Absolute width: the Browse button sizes itself to its localized text
        // ("Browse…"/"Обзор…" plus its own padding), and a fixed column narrower than that clipped it
        // to "Brow..."/"Обзо..." in both languages.
        _commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _commandRow.Controls.Add(_commandBox, 0, 0);
        _commandRow.Controls.Add(_browseBtn, 1, 0);
        _commandRow.Dock = DockStyle.Fill;
        _commandRow.Name = "_commandRow";
        _commandRow.RowCount = 1;
        _commandRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_commandRow, ThemeRole.Background);
        //
        // _commandBox
        //
        _commandBox.BorderStyle = BorderStyle.FixedSingle;
        _commandBox.Dock = DockStyle.Fill;
        _commandBox.Name = "_commandBox";
        //
        // _browseBtn
        //
        _browseBtn.AutoSize = true;
        _browseBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _browseBtn.Margin = new Padding(6, 0, 0, 0);
        _browseBtn.MinimumSize = new Size(80, 32);
        _browseBtn.Name = "_browseBtn";
        _browseBtn.Padding = new Padding(20, 0, 20, 0);
        _browseBtn.Role = ThemeRole.SecondaryButton;
        _browseBtn.Text = "Browse…";
        _uiMetadata.SetLocalizationKey(_browseBtn, "Common.Browse");
        //
        // _commandHint
        //
        // AutoEllipsis, and therefore AutoSize=false: at AutoSize=true this label word-wrapped
        // inside a row only one line tall, so everything past the wrap point was not truncated
        // but silently dropped - no ellipsis, no clue anything was missing. Same treatment
        // HotkeyBindingsForm._hint and TerminalKeyBindingsForm._hint already use.
        _commandHint.AutoEllipsis = true;
        _commandHint.AutoSize = false;
        _commandHint.Dock = DockStyle.Fill;
        _commandHint.Name = "_commandHint";
        _commandHint.Text = "Absolute path, or a name resolved through PATH.";
        _uiMetadata.SetLocalizationKey(_commandHint, "Settings.Terminal.CustomShells.CommandHint");
        _uiMetadata.SetThemeRole(_commandHint, ThemeRole.Hint);
        //
        // _argumentsLabel
        //
        _argumentsLabel.AutoSize = true;
        _argumentsLabel.Dock = DockStyle.Fill;
        _argumentsLabel.Name = "_argumentsLabel";
        _argumentsLabel.Text = "Arguments";
        _argumentsLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_argumentsLabel, "Settings.Terminal.CustomShells.Arguments");
        _uiMetadata.SetThemeRole(_argumentsLabel, ThemeRole.Body);
        //
        // _argumentsBox
        //
        _argumentsBox.BorderStyle = BorderStyle.FixedSingle;
        _argumentsBox.Dock = DockStyle.Fill;
        _argumentsBox.Name = "_argumentsBox";
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(520, 50);
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
        _okBtn.AutoSize = true;
        _okBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _okBtn.Margin = new Padding(0);
        _okBtn.MinimumSize = new Size(100, 32);
        _okBtn.Name = "_okBtn";
        _okBtn.Padding = new Padding(20, 0, 20, 0);
        _okBtn.Role = ThemeRole.PrimaryButton;
        _okBtn.Text = "OK";
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.OK");
        //
        // CustomShellEditForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(520, 220);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        Name = "CustomShellEditForm";
        Text = "Edit shell";
        _uiMetadata.SetLocalizationKey(this, "Settings.Terminal.CustomShells.EditTitle");
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _commandRow.ResumeLayout(false);
        _commandRow.PerformLayout();
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
