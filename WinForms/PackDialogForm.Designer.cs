using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class PackDialogForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Label _nameLabel = null!;
    private TextBox _nameBox = null!;
    private Label _formatLabel = null!;
    private ThemedComboBox _formatCombo = null!;
    private Label _compressionLabel = null!;
    private ThemedComboBox _compressionCombo = null!;
    private ThemedCheckBox _moveCheck = null!;
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
            _formatCombo?.Dispose();
            _compressionCombo?.Dispose();
            _moveCheck?.Dispose();
            _nameLabel?.Dispose();
            _formatLabel?.Dispose();
            _compressionLabel?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Both combos are filled at runtime - the format list comes from
    /// <c>ArchiveFormatRegistry.Creatable</c> and the compression list depends on which format is
    /// selected, so neither has fixed items the designer could hold.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _layout = new TableLayoutPanel();
        _nameLabel = new Label();
        _nameBox = new TextBox();
        _formatLabel = new Label();
        _formatCombo = new ThemedComboBox();
        _compressionLabel = new Label();
        _compressionCombo = new ThemedComboBox();
        _moveCheck = new ThemedCheckBox();
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
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_nameLabel, 0, 0);
        _layout.Controls.Add(_nameBox, 0, 1);
        _layout.Controls.Add(_formatLabel, 0, 2);
        _layout.Controls.Add(_formatCombo, 0, 3);
        _layout.Controls.Add(_compressionLabel, 0, 4);
        _layout.Controls.Add(_compressionCombo, 0, 5);
        _layout.Controls.Add(_moveCheck, 0, 6);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(24, 20, 24, 8);
        _layout.RowCount = 7;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _nameLabel
        //
        _nameLabel.AutoSize = true;
        _nameLabel.Dock = DockStyle.Fill;
        _nameLabel.Name = "_nameLabel";
        _nameLabel.Text = "Archive name";
        _nameLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetLocalizationKey(_nameLabel, "Archive.PackPrompt");
        _uiMetadata.SetThemeRole(_nameLabel, ThemeRole.Body);
        //
        // _nameBox
        //
        _nameBox.BorderStyle = BorderStyle.FixedSingle;
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Name = "_nameBox";
        //
        // _formatLabel
        //
        _formatLabel.AutoSize = true;
        _formatLabel.Dock = DockStyle.Fill;
        _formatLabel.Name = "_formatLabel";
        _formatLabel.Text = "Format";
        _formatLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetLocalizationKey(_formatLabel, "Archive.PackFormat");
        _uiMetadata.SetThemeRole(_formatLabel, ThemeRole.Body);
        //
        // _formatCombo
        //
        _formatCombo.Dock = DockStyle.Fill;
        _formatCombo.Name = "_formatCombo";
        //
        // _compressionLabel
        //
        _compressionLabel.AutoSize = true;
        _compressionLabel.Dock = DockStyle.Fill;
        _compressionLabel.Name = "_compressionLabel";
        _compressionLabel.Text = "Compression";
        _compressionLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetLocalizationKey(_compressionLabel, "Archive.PackCompression");
        _uiMetadata.SetThemeRole(_compressionLabel, ThemeRole.Body);
        //
        // _compressionCombo
        //
        _compressionCombo.Dock = DockStyle.Fill;
        _compressionCombo.Name = "_compressionCombo";
        //
        // _moveCheck
        //
        _moveCheck.AutoSize = true;
        _moveCheck.Dock = DockStyle.Left;
        _moveCheck.Name = "_moveCheck";
        _moveCheck.Text = "Delete originals after packing";
        _uiMetadata.SetLocalizationKey(_moveCheck, "Archive.PackMoveOriginals");
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
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.Margin = new Padding(0);
        _okBtn.MinimumSize = new Size(100, 32);
        _okBtn.Name = "_okBtn";
        _okBtn.Padding = new Padding(20, 0, 20, 0);
        _okBtn.Role = ThemeRole.PrimaryButton;
        _okBtn.Text = "OK";
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.OK");
        //
        // PackDialogForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(440, 300);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "PackDialogForm";
        Text = "Pack files";
        _uiMetadata.SetLocalizationKey(this, "Archive.PackTitle");
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
