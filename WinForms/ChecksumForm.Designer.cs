using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class ChecksumForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private ListView _resultList = null!;
    private ColumnHeader _colResultName = null!;
    private ColumnHeader _colResultAlgo = null!;
    private ColumnHeader _colResultHash = null!;
    private Panel _bottomPanel = null!;
    private Label _statusLabel = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _closeBtn = null!;
    private RoundedButton _copyBtn = null!;
    private RoundedButton _exportBtn = null!;
    private RoundedButton _calcBtn = null!;
    private TableLayoutPanel _topPanel = null!;
    private ListView _fileList = null!;
    private ColumnHeader _colFileName = null!;
    private ColumnHeader _colFileSize = null!;
    private Label _algoLabel = null!;
    private ThemedComboBox _algoCombo = null!;

    /// <summary>Explicit disposal of the control fields (CA2213), plus the CancellationTokenSource
    /// the behaviour half owns - a form can carry only one <c>Dispose(bool)</c> override.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _resultList?.Dispose();
            _fileList?.Dispose();
            _algoCombo?.Dispose();
            _algoLabel?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
            _copyBtn?.Dispose();
            _exportBtn?.Dispose();
            _calcBtn?.Dispose();
            _rightGroup?.Dispose();
            _bottomPanel?.Dispose();
            _topPanel?.Dispose();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Column captions are localized in the constructor - a
    /// <see cref="ColumnHeader"/> is not a <see cref="Control"/> and cannot carry a
    /// LocalizationKey. The algorithm combo's items are protocol identifiers (CRC32/MD5/SHA1/
    /// SHA256) that must stay unlocalised, so they are added in code beside the switch that
    /// consumes them.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _resultList = new ListView();
        _colResultName = new ColumnHeader();
        _colResultAlgo = new ColumnHeader();
        _colResultHash = new ColumnHeader();
        _bottomPanel = new Panel();
        _statusLabel = new Label();
        _rightGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        _copyBtn = new RoundedButton();
        _exportBtn = new RoundedButton();
        _calcBtn = new RoundedButton();
        _topPanel = new TableLayoutPanel();
        _fileList = new ListView();
        _colFileName = new ColumnHeader();
        _colFileSize = new ColumnHeader();
        _algoLabel = new Label();
        _algoCombo = new ThemedComboBox();
        _bottomPanel.SuspendLayout();
        _rightGroup.SuspendLayout();
        _topPanel.SuspendLayout();
        SuspendLayout();
        //
        // _resultList
        //
        _resultList.BorderStyle = BorderStyle.None;
        _resultList.Columns.AddRange(new[] { _colResultName, _colResultAlgo, _colResultHash });
        _resultList.Dock = DockStyle.Fill;
        _resultList.FullRowSelect = true;
        _resultList.Name = "_resultList";
        _resultList.UseCompatibleStateImageBehavior = false;
        _resultList.View = View.Details;
        //
        // _colResultName
        //
        _colResultName.Text = "File";
        _colResultName.Width = 200;
        //
        // _colResultAlgo
        //
        _colResultAlgo.Text = "Algorithm";
        _colResultAlgo.Width = 80;
        //
        // _colResultHash
        //
        _colResultHash.Text = "Hash";
        _colResultHash.Width = 400;
        //
        // _bottomPanel
        //
        // Fill added before Right: the right-docked button group claims its width first and the
        // filling status label takes what is left.
        _bottomPanel.Controls.Add(_statusLabel);
        _bottomPanel.Controls.Add(_rightGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(700, 50);
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        //
        // _statusLabel
        //
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Name = "_statusLabel";
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_statusLabel, ThemeRole.Muted);
        //
        // _rightGroup
        //
        // Dock.Right ignores Margin entirely, which had collapsed all three gaps - a right-aligned
        // FlowLayoutPanel (add order = visual left-to-right) actually renders them.
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_closeBtn);
        _rightGroup.Controls.Add(_copyBtn);
        _rightGroup.Controls.Add(_exportBtn);
        _rightGroup.Controls.Add(_calcBtn);
        _rightGroup.Dock = DockStyle.Right;
        _rightGroup.FlowDirection = FlowDirection.LeftToRight;
        _rightGroup.Name = "_rightGroup";
        _rightGroup.WrapContents = false;
        //
        // _closeBtn
        //
        _closeBtn.AutoSize = true;
        _closeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _closeBtn.Margin = new Padding(0, 0, 8, 0);
        _closeBtn.MinimumSize = new Size(100, 32);
        _closeBtn.Name = "_closeBtn";
        _closeBtn.Padding = new Padding(20, 0, 20, 0);
        _closeBtn.Role = ThemeRole.SecondaryButton;
        _closeBtn.Text = "Close";
        _uiMetadata.SetLocalizationKey(_closeBtn, "Common.Close");
        //
        // _copyBtn
        //
        _copyBtn.AutoSize = true;
        _copyBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _copyBtn.Margin = new Padding(0, 0, 8, 0);
        _copyBtn.MinimumSize = new Size(100, 32);
        _copyBtn.Name = "_copyBtn";
        _copyBtn.Padding = new Padding(20, 0, 20, 0);
        _copyBtn.Role = ThemeRole.SecondaryButton;
        _copyBtn.Text = "Copy to clipboard";
        _uiMetadata.SetLocalizationKey(_copyBtn, "Checksum.CopyToClipboard");
        //
        // _exportBtn
        //
        _exportBtn.AutoSize = true;
        _exportBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _exportBtn.Margin = new Padding(0, 0, 8, 0);
        _exportBtn.MinimumSize = new Size(100, 32);
        _exportBtn.Name = "_exportBtn";
        _exportBtn.Padding = new Padding(20, 0, 20, 0);
        _exportBtn.Role = ThemeRole.SecondaryButton;
        _exportBtn.Text = "Export…";
        _uiMetadata.SetLocalizationKey(_exportBtn, "Checksum.Export");
        //
        // _calcBtn
        //
        _calcBtn.AutoSize = true;
        _calcBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _calcBtn.Margin = new Padding(0);
        _calcBtn.MinimumSize = new Size(100, 32);
        _calcBtn.Name = "_calcBtn";
        _calcBtn.Padding = new Padding(20, 0, 20, 0);
        _calcBtn.Role = ThemeRole.PrimaryButton;
        _calcBtn.Text = "Calculate";
        _uiMetadata.SetLocalizationKey(_calcBtn, "Checksum.Calculate");
        //
        // _topPanel
        //
        _topPanel.ColumnCount = 2;
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        _topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // The source list starts at column 0 and spans both. Column 0 exists for the "Algorithm:"
        // caption on the row below; row 0 has no caption of its own, so the list used to be added
        // at column 1 and sat indented by 120px behind an empty block - narrower than the result
        // list underneath it, which has always run the full width. Spanning from column 1 does not
        // work: the span would run off the end of a two-column table.
        _topPanel.Controls.Add(_fileList, 0, 0);
        _topPanel.SetColumnSpan(_fileList, 2);
        _topPanel.Controls.Add(_algoLabel, 0, 1);
        _topPanel.Controls.Add(_algoCombo, 1, 1);
        _topPanel.Dock = DockStyle.Top;
        _topPanel.Name = "_topPanel";
        _topPanel.Padding = new Padding(16, 12, 16, 12);
        _topPanel.RowCount = 2;
        _topPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _topPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _topPanel.Size = new Size(700, 200);
        _uiMetadata.SetThemeRole(_topPanel, ThemeRole.Background);
        //
        // _fileList
        //
        _fileList.BorderStyle = BorderStyle.None;
        _fileList.Columns.AddRange(new[] { _colFileName, _colFileSize });
        _fileList.Dock = DockStyle.Fill;
        _fileList.FullRowSelect = true;
        _fileList.Name = "_fileList";
        _fileList.UseCompatibleStateImageBehavior = false;
        _fileList.View = View.Details;
        //
        // _colFileName
        //
        _colFileName.Text = "File";
        _colFileName.Width = 400;
        //
        // _colFileSize
        //
        _colFileSize.Text = "Size";
        _colFileSize.Width = 120;
        //
        // _algoLabel
        //
        _algoLabel.AutoSize = true;
        _algoLabel.Name = "_algoLabel";
        _algoLabel.Text = "Algorithm";
        _uiMetadata.SetLocalizationKey(_algoLabel, "Checksum.Algorithm");
        _uiMetadata.SetThemeRole(_algoLabel, ThemeRole.Body);
        //
        // _algoCombo
        //
        _algoCombo.Dock = DockStyle.Left;
        _algoCombo.Name = "_algoCombo";
        _algoCombo.Size = new Size(120, 30);
        //
        // ChecksumForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(700, 520);
        // Fill before the Bottom and Top siblings - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_resultList);
        Controls.Add(_bottomPanel);
        Controls.Add(_topPanel);
        MinimumSize = new Size(480, 360);
        Name = "ChecksumForm";
        Text = "Checksums";
        _uiMetadata.SetLocalizationKey(this, "Checksum.Title");
        _bottomPanel.ResumeLayout(false);
        _rightGroup.ResumeLayout(false);
        _topPanel.ResumeLayout(false);
        _topPanel.PerformLayout();
        ResumeLayout(false);
    }
}
