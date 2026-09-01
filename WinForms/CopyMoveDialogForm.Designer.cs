using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class CopyMoveDialogForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _mainLayout = null!;
    private Panel _headerPanel = null!;
    private PictureBox _iconBox = null!;
    private Label _headerLabel = null!;
    private Panel _fileListPanel = null!;
    private ListView _fileList = null!;
    private ColumnHeader _colName = null!;
    private ColumnHeader _colSize = null!;
    private ColumnHeader _colType = null!;
    private TableLayoutPanel _optionsPanel = null!;
    private Label _destLabel = null!;
    private Panel _destPanel = null!;
    private TextBox _destBox = null!;
    private RoundedButton _browseBtn = null!;
    private TableLayoutPanel _infoPanel = null!;
    private Label _fileCountLabel = null!;
    private Label _totalSizeLabel = null!;
    private Label _owLabel = null!;
    private ThemedComboBox _overwriteCombo = null!;
    private FlowLayoutPanel _checksFlow = null!;
    private ThemedCheckBox _copyAttrsCheck = null!;
    private ThemedCheckBox _copyTsCheck = null!;
    private ThemedCheckBox _queueCheck = null!;
    private Panel _btnPanel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _okBtn = null!;
    private RoundedButton _cancelBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _iconBox?.Dispose();
            _headerLabel?.Dispose();
            _fileList?.Dispose();
            _destLabel?.Dispose();
            _destBox?.Dispose();
            _browseBtn?.Dispose();
            _fileCountLabel?.Dispose();
            _totalSizeLabel?.Dispose();
            _owLabel?.Dispose();
            _overwriteCombo?.Dispose();
            _copyAttrsCheck?.Dispose();
            _copyTsCheck?.Dispose();
            _queueCheck?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _buttonGroup?.Dispose();
            _btnPanel?.Dispose();
            _checksFlow?.Dispose();
            _infoPanel?.Dispose();
            _destPanel?.Dispose();
            _optionsPanel?.Dispose();
            _fileListPanel?.Dispose();
            _headerPanel?.Dispose();
            _mainLayout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The header caption and window title differ between Copy and Move, the
    /// file list is filled from the caller's selection, and the two info labels interpolate counts
    /// and sizes - all set in the constructor.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _mainLayout = new TableLayoutPanel();
        _headerPanel = new Panel();
        _iconBox = new PictureBox();
        _headerLabel = new Label();
        _fileListPanel = new Panel();
        _fileList = new ListView();
        _colName = new ColumnHeader();
        _colSize = new ColumnHeader();
        _colType = new ColumnHeader();
        _optionsPanel = new TableLayoutPanel();
        _destLabel = new Label();
        _destPanel = new Panel();
        _destBox = new TextBox();
        _browseBtn = new RoundedButton();
        _infoPanel = new TableLayoutPanel();
        _fileCountLabel = new Label();
        _totalSizeLabel = new Label();
        _owLabel = new Label();
        _overwriteCombo = new ThemedComboBox();
        _checksFlow = new FlowLayoutPanel();
        _copyAttrsCheck = new ThemedCheckBox();
        _copyTsCheck = new ThemedCheckBox();
        _queueCheck = new ThemedCheckBox();
        _btnPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _okBtn = new RoundedButton();
        _cancelBtn = new RoundedButton();
        ((System.ComponentModel.ISupportInitialize)_iconBox).BeginInit();
        _mainLayout.SuspendLayout();
        _headerPanel.SuspendLayout();
        _fileListPanel.SuspendLayout();
        _optionsPanel.SuspendLayout();
        _destPanel.SuspendLayout();
        _infoPanel.SuspendLayout();
        _checksFlow.SuspendLayout();
        _btnPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _mainLayout
        //
        _mainLayout.ColumnCount = 1;
        _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainLayout.Controls.Add(_headerPanel, 0, 0);
        _mainLayout.Controls.Add(_fileListPanel, 0, 1);
        _mainLayout.Controls.Add(_optionsPanel, 0, 2);
        _mainLayout.Controls.Add(_btnPanel, 0, 3);
        _mainLayout.Dock = DockStyle.Fill;
        _mainLayout.Name = "_mainLayout";
        _mainLayout.Padding = new Padding(0);
        _mainLayout.RowCount = 4;
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        _uiMetadata.SetThemeRole(_mainLayout, ThemeRole.Background);
        //
        // _headerPanel
        //
        _headerPanel.Controls.Add(_iconBox);
        _headerPanel.Controls.Add(_headerLabel);
        _headerPanel.Dock = DockStyle.Fill;
        _headerPanel.Name = "_headerPanel";
        _headerPanel.Padding = new Padding(20, 12, 20, 12);
        _uiMetadata.SetThemeRole(_headerPanel, ThemeRole.HeaderBackground);
        //
        // _iconBox
        //
        // Absolute Location inside a plain Panel - the icon is drawn by a Paint handler, not loaded
        // from an Image, so there is nothing for a layout panel to measure.
        _iconBox.BackColor = Color.Transparent;
        _iconBox.Location = new Point(20, 12);
        _iconBox.Name = "_iconBox";
        _iconBox.Size = new Size(32, 32);
        _iconBox.SizeMode = PictureBoxSizeMode.StretchImage;
        _iconBox.TabStop = false;
        //
        // _headerLabel
        //
        // Text is set in the constructor - it is the Copy or the Move caption, not one fixed key.
        _headerLabel.AutoSize = true;
        _headerLabel.Location = new Point(64, 12);
        _headerLabel.Name = "_headerLabel";
        _uiMetadata.SetThemeRole(_headerLabel, ThemeRole.Subtitle);
        //
        // _fileListPanel
        //
        _fileListPanel.Controls.Add(_fileList);
        _fileListPanel.Dock = DockStyle.Fill;
        _fileListPanel.Name = "_fileListPanel";
        _fileListPanel.Padding = new Padding(20, 8, 20, 8);
        _uiMetadata.SetThemeRole(_fileListPanel, ThemeRole.PanelBackground);
        //
        // _fileList
        //
        _fileList.BorderStyle = BorderStyle.None;
        _fileList.Columns.AddRange(new[] { _colName, _colSize, _colType });
        _fileList.Dock = DockStyle.Fill;
        _fileList.FullRowSelect = false;
        _fileList.GridLines = false;
        _fileList.HeaderStyle = ColumnHeaderStyle.None;
        _fileList.MultiSelect = false;
        _fileList.Name = "_fileList";
        _fileList.Scrollable = true;
        _fileList.UseCompatibleStateImageBehavior = false;
        _fileList.View = View.Details;
        //
        // _colName
        //
        _colName.Text = "Name";
        _colName.Width = 320;
        //
        // _colSize
        //
        _colSize.TextAlign = HorizontalAlignment.Right;
        _colSize.Text = "Size";
        _colSize.Width = 100;
        //
        // _colType
        //
        _colType.Text = "Type";
        _colType.Width = 80;
        //
        // _optionsPanel
        //
        _optionsPanel.ColumnCount = 2;
        _optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        _optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _optionsPanel.Controls.Add(_destLabel, 0, 0);
        _optionsPanel.Controls.Add(_destPanel, 1, 0);
        _optionsPanel.Controls.Add(_infoPanel, 1, 1);
        _optionsPanel.Controls.Add(_owLabel, 0, 2);
        _optionsPanel.Controls.Add(_overwriteCombo, 1, 2);
        _optionsPanel.Controls.Add(_checksFlow, 1, 3);
        _optionsPanel.Dock = DockStyle.Fill;
        _optionsPanel.Name = "_optionsPanel";
        _optionsPanel.Padding = new Padding(20, 12, 20, 12);
        _optionsPanel.RowCount = 4;
        _optionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _optionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _optionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _optionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_optionsPanel, ThemeRole.Background);
        //
        // _destLabel
        //
        _destLabel.AutoSize = true;
        _destLabel.Dock = DockStyle.Fill;
        _destLabel.Name = "_destLabel";
        _destLabel.Text = "Destination";
        _destLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_destLabel, "CopyMove.Destination");
        _uiMetadata.SetThemeRole(_destLabel, ThemeRole.Emphasis);
        //
        // _destPanel
        //
        // Margin = 0: this row is RowStyle(Absolute, 32) and the default 3px Control.Margin would
        // shrink the cell's usable height to 26px, 6px short of the Browse button's 32px.
        //
        // Fill added before Right, so the right-docked button claims its width first.
        _destPanel.Controls.Add(_destBox);
        _destPanel.Controls.Add(_browseBtn);
        _destPanel.Dock = DockStyle.Fill;
        _destPanel.Margin = new Padding(0);
        _destPanel.Name = "_destPanel";
        //
        // _destBox
        //
        _destBox.BorderStyle = BorderStyle.FixedSingle;
        _destBox.Dock = DockStyle.Fill;
        _destBox.Name = "_destBox";
        //
        // _browseBtn
        //
        // An ellipsis glyph, not a word - no localization key.
        _browseBtn.Dock = DockStyle.Right;
        _browseBtn.Margin = new Padding(4, 0, 0, 0);
        _browseBtn.Name = "_browseBtn";
        _browseBtn.Role = ThemeRole.SecondaryButton;
        _browseBtn.Size = new Size(40, 32);
        _browseBtn.Text = "...";
        //
        // _infoPanel
        //
        _infoPanel.ColumnCount = 2;
        _infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _infoPanel.Controls.Add(_fileCountLabel, 0, 0);
        _infoPanel.Controls.Add(_totalSizeLabel, 1, 0);
        _infoPanel.Dock = DockStyle.Fill;
        _infoPanel.Name = "_infoPanel";
        _infoPanel.RowCount = 1;
        _infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_infoPanel, ThemeRole.Background);
        //
        // _fileCountLabel
        //
        // Both info labels interpolate a count or a formatted size, so their text is set in code.
        _fileCountLabel.AutoSize = true;
        _fileCountLabel.Dock = DockStyle.Fill;
        _fileCountLabel.Name = "_fileCountLabel";
        _fileCountLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_fileCountLabel, ThemeRole.Body);
        //
        // _totalSizeLabel
        //
        _totalSizeLabel.AutoSize = true;
        _totalSizeLabel.Dock = DockStyle.Fill;
        _totalSizeLabel.Name = "_totalSizeLabel";
        _totalSizeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_totalSizeLabel, ThemeRole.Body);
        //
        // _owLabel
        //
        _owLabel.AutoSize = true;
        _owLabel.Dock = DockStyle.Fill;
        _owLabel.Name = "_owLabel";
        _owLabel.Text = "On conflict";
        _owLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_owLabel, "CopyMove.OverwritePolicy");
        _uiMetadata.SetThemeRole(_owLabel, ThemeRole.Emphasis);
        //
        // _overwriteCombo
        //
        _overwriteCombo.Dock = DockStyle.Fill;
        _overwriteCombo.Name = "_overwriteCombo";
        //
        // _checksFlow
        //
        _checksFlow.Controls.Add(_copyAttrsCheck);
        _checksFlow.Controls.Add(_copyTsCheck);
        _checksFlow.Controls.Add(_queueCheck);
        _checksFlow.Dock = DockStyle.Fill;
        _checksFlow.FlowDirection = FlowDirection.TopDown;
        _checksFlow.Name = "_checksFlow";
        _checksFlow.Padding = new Padding(0, 4, 0, 0);
        _checksFlow.WrapContents = false;
        _uiMetadata.SetThemeRole(_checksFlow, ThemeRole.Background);
        //
        // _copyAttrsCheck
        //
        _copyAttrsCheck.AutoSize = true;
        _copyAttrsCheck.Name = "_copyAttrsCheck";
        _copyAttrsCheck.Text = "Copy attributes";
        _uiMetadata.SetLocalizationKey(_copyAttrsCheck, "CopyMove.CopyAttributes");
        //
        // _copyTsCheck
        //
        _copyTsCheck.AutoSize = true;
        _copyTsCheck.Name = "_copyTsCheck";
        _copyTsCheck.Text = "Copy timestamps";
        _uiMetadata.SetLocalizationKey(_copyTsCheck, "CopyMove.CopyTimestamps");
        //
        // _queueCheck
        //
        _queueCheck.AutoSize = true;
        _queueCheck.Name = "_queueCheck";
        _queueCheck.Text = "Add to queue";
        _uiMetadata.SetLocalizationKey(_queueCheck, "CopyMove.Queue");
        //
        // _btnPanel
        //
        _btnPanel.Controls.Add(_buttonGroup);
        _btnPanel.Dock = DockStyle.Fill;
        // Margin = 0 for the same reason as _destPanel: this is an Absolute 50 row, and the default
        // 3px margin rendered the bar 44px tall, 6px short.
        _btnPanel.Margin = new Padding(0);
        _btnPanel.Name = "_btnPanel";
        _btnPanel.Padding = new Padding(16, 8, 16, 8);
        _uiMetadata.SetThemeRole(_btnPanel, ThemeRole.HeaderBackground);
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
        // CopyMoveDialogForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(560, 460);
        Controls.Add(_mainLayout);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "CopyMoveDialogForm";
        Text = "Copy";
        ((System.ComponentModel.ISupportInitialize)_iconBox).EndInit();
        _mainLayout.ResumeLayout(false);
        _headerPanel.ResumeLayout(false);
        _headerPanel.PerformLayout();
        _fileListPanel.ResumeLayout(false);
        _optionsPanel.ResumeLayout(false);
        _optionsPanel.PerformLayout();
        _destPanel.ResumeLayout(false);
        _destPanel.PerformLayout();
        _infoPanel.ResumeLayout(false);
        _infoPanel.PerformLayout();
        _checksFlow.ResumeLayout(false);
        _checksFlow.PerformLayout();
        _btnPanel.ResumeLayout(false);
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
