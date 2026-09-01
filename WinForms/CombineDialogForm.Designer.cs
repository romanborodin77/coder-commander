using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class CombineDialogForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Label _nameLabel = null!;
    private TextBox _outputNameBox = null!;
    private Label _partsLabel = null!;
    private ListBox _partsList = null!;
    private FlowLayoutPanel _checksLayout = null!;
    private ThemedCheckBox _verifyCrcCheck = null!;
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
            _outputNameBox?.Dispose();
            _deleteSourceCheck?.Dispose();
            _verifyCrcCheck?.Dispose();
            _partsList?.Dispose();
            _nameLabel?.Dispose();
            _partsLabel?.Dispose();
            _checksLayout?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The parts list is filled at runtime from what the caller discovered.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _layout = new TableLayoutPanel();
        _nameLabel = new Label();
        _outputNameBox = new TextBox();
        _partsLabel = new Label();
        _partsList = new ListBox();
        _checksLayout = new FlowLayoutPanel();
        _verifyCrcCheck = new ThemedCheckBox();
        _deleteSourceCheck = new ThemedCheckBox();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _okBtn = new RoundedButton();
        _cancelBtn = new RoundedButton();
        _layout.SuspendLayout();
        _checksLayout.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_nameLabel, 0, 0);
        _layout.Controls.Add(_outputNameBox, 0, 1);
        _layout.Controls.Add(_partsLabel, 0, 2);
        _layout.Controls.Add(_partsList, 0, 3);
        _layout.Controls.Add(_checksLayout, 0, 4);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(24, 20, 24, 8);
        _layout.RowCount = 6;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));
        // AutoSize, not another Absolute 32: this row holds _checksLayout, a TopDown flow of TWO
        // checkboxes needing about 54px together. At 32 the second one ("delete the parts after
        // combining") was clipped away entirely - not merely cut off, but invisible and impossible
        // to tick. SplitDialogForm avoids this by giving each of its checkboxes its own row.
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _nameLabel
        //
        _nameLabel.AutoSize = true;
        _nameLabel.Dock = DockStyle.Fill;
        _nameLabel.Name = "_nameLabel";
        _nameLabel.Text = "Output name";
        _nameLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetLocalizationKey(_nameLabel, "Combine.OutputName");
        _uiMetadata.SetThemeRole(_nameLabel, ThemeRole.Body);
        //
        // _outputNameBox
        //
        _outputNameBox.BorderStyle = BorderStyle.FixedSingle;
        _outputNameBox.Dock = DockStyle.Fill;
        _outputNameBox.Name = "CombineOutputNameBox";
        //
        // _partsLabel
        //
        _partsLabel.AutoSize = true;
        _partsLabel.Dock = DockStyle.Fill;
        _partsLabel.Name = "_partsLabel";
        _partsLabel.Text = "Parts found";
        _partsLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetLocalizationKey(_partsLabel, "Combine.PartsFound");
        _uiMetadata.SetThemeRole(_partsLabel, ThemeRole.Body);
        //
        // _partsList
        //
        _partsList.BorderStyle = BorderStyle.FixedSingle;
        _partsList.Dock = DockStyle.Fill;
        _partsList.Name = "CombinePartsList";
        //
        // _checksLayout
        //
        _checksLayout.AutoSize = true;
        _checksLayout.Controls.Add(_verifyCrcCheck);
        _checksLayout.Controls.Add(_deleteSourceCheck);
        _checksLayout.Dock = DockStyle.Fill;
        _checksLayout.FlowDirection = FlowDirection.TopDown;
        _checksLayout.Name = "_checksLayout";
        _checksLayout.WrapContents = false;
        //
        // _verifyCrcCheck
        //
        _verifyCrcCheck.AutoSize = true;
        _verifyCrcCheck.Name = "CombineVerifyCrcCheck";
        _verifyCrcCheck.Text = "Verify against .crc";
        _uiMetadata.SetLocalizationKey(_verifyCrcCheck, "Combine.VerifyCrc");
        //
        // _deleteSourceCheck
        //
        _deleteSourceCheck.AutoSize = true;
        _deleteSourceCheck.Name = "CombineDeletePartsCheck";
        _deleteSourceCheck.Text = "Delete parts after combining";
        _uiMetadata.SetLocalizationKey(_deleteSourceCheck, "Combine.DeleteParts");
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
        _cancelBtn.Name = "CombineCancelButton";
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
        _okBtn.Name = "CombineOkButton";
        _okBtn.Padding = new Padding(20, 0, 20, 0);
        _okBtn.Role = ThemeRole.PrimaryButton;
        _okBtn.Text = "OK";
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.OK");
        //
        // CombineDialogForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(440, 340);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "CombineDialogForm";
        Text = "Combine files";
        _uiMetadata.SetLocalizationKey(this, "Combine.Title");
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _checksLayout.ResumeLayout(false);
        _checksLayout.PerformLayout();
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
