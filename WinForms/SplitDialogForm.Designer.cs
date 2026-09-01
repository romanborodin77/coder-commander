using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class SplitDialogForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Label _destLabel = null!;
    private TextBox _destDirBox = null!;
    private Label _presetLabel = null!;
    private ThemedComboBox _presetCombo = null!;
    private Label _customLabel = null!;
    private TextBox _customSizeBox = null!;
    private ThemedCheckBox _writeCrcCheck = null!;
    private ThemedCheckBox _deleteSourceCheck = null!;
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
            _presetCombo?.Dispose();
            _customSizeBox?.Dispose();
            _deleteSourceCheck?.Dispose();
            _writeCrcCheck?.Dispose();
            _destDirBox?.Dispose();
            _destLabel?.Dispose();
            _presetLabel?.Dispose();
            _customLabel?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The preset combo's items are localized size names added at runtime.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _layout = new TableLayoutPanel();
        _destLabel = new Label();
        _destDirBox = new TextBox();
        _presetLabel = new Label();
        _presetCombo = new ThemedComboBox();
        _customLabel = new Label();
        _customSizeBox = new TextBox();
        _writeCrcCheck = new ThemedCheckBox();
        _deleteSourceCheck = new ThemedCheckBox();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _okBtn = new RoundedButton();
        _cancelBtn = new RoundedButton();
        _layout.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        // Eight alternating rows - a 22px label above a 32px field, four times - then a
        // percent-sized filler. Spelled out rather than looped, which is what the designer needs to
        // be able to round-trip them.
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_destLabel, 0, 0);
        _layout.Controls.Add(_destDirBox, 0, 1);
        _layout.Controls.Add(_presetLabel, 0, 2);
        _layout.Controls.Add(_presetCombo, 0, 3);
        _layout.Controls.Add(_customLabel, 0, 4);
        _layout.Controls.Add(_customSizeBox, 0, 5);
        _layout.Controls.Add(_writeCrcCheck, 0, 6);
        _layout.Controls.Add(_deleteSourceCheck, 0, 7);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(24, 20, 24, 8);
        _layout.RowCount = 9;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _destLabel
        //
        _destLabel.AutoSize = true;
        _destLabel.Dock = DockStyle.Fill;
        _destLabel.Name = "_destLabel";
        _destLabel.Text = "Destination folder";
        _destLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetLocalizationKey(_destLabel, "Split.DestDir");
        _uiMetadata.SetThemeRole(_destLabel, ThemeRole.Body);
        //
        // _destDirBox
        //
        _destDirBox.BorderStyle = BorderStyle.FixedSingle;
        _destDirBox.Dock = DockStyle.Fill;
        _destDirBox.Name = "SplitDestDirBox";
        //
        // _presetLabel
        //
        _presetLabel.AutoSize = true;
        _presetLabel.Dock = DockStyle.Fill;
        _presetLabel.Name = "_presetLabel";
        _presetLabel.Text = "Part size";
        _presetLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetLocalizationKey(_presetLabel, "Split.PartSize");
        _uiMetadata.SetThemeRole(_presetLabel, ThemeRole.Body);
        //
        // _presetCombo
        //
        _presetCombo.Dock = DockStyle.Fill;
        _presetCombo.Name = "SplitPresetCombo";
        //
        // _customLabel
        //
        _customLabel.AutoSize = true;
        _customLabel.Dock = DockStyle.Fill;
        _customLabel.Name = "_customLabel";
        _customLabel.Text = "Custom size (MB)";
        _customLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetLocalizationKey(_customLabel, "Split.CustomSizeMb");
        _uiMetadata.SetThemeRole(_customLabel, ThemeRole.Body);
        //
        // _customSizeBox
        //
        _customSizeBox.BorderStyle = BorderStyle.FixedSingle;
        _customSizeBox.Dock = DockStyle.Fill;
        _customSizeBox.Name = "SplitCustomSizeBox";
        _customSizeBox.Text = "10";
        //
        // _writeCrcCheck
        //
        _writeCrcCheck.AutoSize = true;
        _writeCrcCheck.Dock = DockStyle.Left;
        _writeCrcCheck.Name = "SplitWriteCrcCheck";
        _writeCrcCheck.Text = "Create .crc checksum file";
        _uiMetadata.SetLocalizationKey(_writeCrcCheck, "Split.WriteCrc");
        //
        // _deleteSourceCheck
        //
        _deleteSourceCheck.AutoSize = true;
        _deleteSourceCheck.Dock = DockStyle.Left;
        _deleteSourceCheck.Name = "SplitDeleteSourceCheck";
        _deleteSourceCheck.Text = "Delete source after splitting";
        _uiMetadata.SetLocalizationKey(_deleteSourceCheck, "Split.DeleteSource");
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(440, 50);
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
        _cancelBtn.Name = "SplitCancelButton";
        _cancelBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelBtn.Role = ThemeRole.SecondaryButton;
        _cancelBtn.Text = "Cancel";
        _uiMetadata.SetLocalizationKey(_cancelBtn, "Common.Cancel");
        //
        // _okBtn
        //
        _okBtn.AutoSize = true;
        _okBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.Margin = new Padding(0);
        _okBtn.MinimumSize = new Size(100, 32);
        _okBtn.Name = "SplitOkButton";
        _okBtn.Padding = new Padding(20, 0, 20, 0);
        _okBtn.Role = ThemeRole.PrimaryButton;
        _okBtn.Text = "OK";
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.OK");
        //
        // SplitDialogForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(440, 330);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "SplitDialogForm";
        Text = "Split file";
        _uiMetadata.SetLocalizationKey(this, "Split.Title");
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
