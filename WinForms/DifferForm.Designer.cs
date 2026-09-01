using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class DifferForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private SplitContainer _split = null!;
    private TextBox _leftBox = null!;
    private TextBox _rightBox = null!;
    private Panel _bottomPanel = null!;
    private Label _statusLabel = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _closeBtn = null!;
    private RoundedButton _compareBtn = null!;
    private TableLayoutPanel _topBar = null!;
    private Label _leftLabel = null!;
    private TextBox _leftPathBox = null!;
    private RoundedButton _leftBrowseBtn = null!;
    private Label _rightLabel = null!;
    private TextBox _rightPathBox = null!;
    private RoundedButton _rightBrowseBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213), plus the compare cancellation
    /// source the behaviour half owns - a form can carry only one <c>Dispose(bool)</c> override.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _leftBox?.Dispose();
            _rightBox?.Dispose();
            _leftPathBox?.Dispose();
            _rightPathBox?.Dispose();
            _leftBrowseBtn?.Dispose();
            _rightBrowseBtn?.Dispose();
            _leftLabel?.Dispose();
            _rightLabel?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
            _compareBtn?.Dispose();
            _rightGroup?.Dispose();
            _bottomPanel?.Dispose();
            _topBar?.Dispose();
            _split?.Dispose();
            // Cancel before dispose: a compare may still be reading both files.
            _compareCts?.Cancel();
            _compareCts?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. SplitterDistance is deliberately NOT set here - see the constructor
    /// for why it can only be assigned after the container is parented and docked.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _split = new SplitContainer();
        _leftBox = new TextBox();
        _rightBox = new TextBox();
        _bottomPanel = new Panel();
        _statusLabel = new Label();
        _rightGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        _compareBtn = new RoundedButton();
        _topBar = new TableLayoutPanel();
        _leftLabel = new Label();
        _leftPathBox = new TextBox();
        _leftBrowseBtn = new RoundedButton();
        _rightLabel = new Label();
        _rightPathBox = new TextBox();
        _rightBrowseBtn = new RoundedButton();
        ((System.ComponentModel.ISupportInitialize)_split).BeginInit();
        _split.Panel1.SuspendLayout();
        _split.Panel2.SuspendLayout();
        _split.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _rightGroup.SuspendLayout();
        _topBar.SuspendLayout();
        SuspendLayout();
        //
        // _split
        //
        _split.BorderStyle = BorderStyle.None;
        _split.Dock = DockStyle.Fill;
        _split.Name = "_split";
        _split.Orientation = Orientation.Vertical;
        _split.Panel1.Controls.Add(_leftBox);
        _split.Panel2.Controls.Add(_rightBox);
        _split.SplitterWidth = 4;
        //
        // _leftBox
        //
        _leftBox.BorderStyle = BorderStyle.None;
        _leftBox.Dock = DockStyle.Fill;
        _leftBox.Multiline = true;
        _leftBox.Name = "_leftBox";
        _leftBox.ReadOnly = true;
        _leftBox.ScrollBars = ScrollBars.Both;
        _leftBox.WordWrap = false;
        //
        // _rightBox
        //
        _rightBox.BorderStyle = BorderStyle.None;
        _rightBox.Dock = DockStyle.Fill;
        _rightBox.Multiline = true;
        _rightBox.Name = "_rightBox";
        _rightBox.ReadOnly = true;
        _rightBox.ScrollBars = ScrollBars.Both;
        _rightBox.WordWrap = false;
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
        _bottomPanel.Size = new Size(900, 50);
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
        // A right-aligned FlowLayoutPanel rather than Dock.Right + Margin, which Dock.Right ignores,
        // so the gap between the two buttons actually renders.
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_closeBtn);
        _rightGroup.Controls.Add(_compareBtn);
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
        // _compareBtn
        //
        _compareBtn.AutoSize = true;
        _compareBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _compareBtn.Margin = new Padding(0);
        _compareBtn.MinimumSize = new Size(100, 32);
        _compareBtn.Name = "_compareBtn";
        _compareBtn.Padding = new Padding(20, 0, 20, 0);
        _compareBtn.Role = ThemeRole.PrimaryButton;
        _compareBtn.Text = "Compare";
        _uiMetadata.SetLocalizationKey(_compareBtn, "Differ.Compare");
        //
        // _topBar
        //
        // Height(88) minus Padding(8+8) leaves 72 for the two rows, and the RowStyles must sum to
        // exactly that: TableLayoutPanel dumps any leftover into the LAST row alone, which at the
        // old 32+32 made the Right row's Browse button render 34px against the Left row's 26px.
        // Three columns, not four: label, path box, Browse button. A fourth Percent-50 column used
        // to sit here with nothing in it, which split the free width evenly between the path box
        // and 354px of dead space - the path boxes showed roughly half the path they had room for.
        _topBar.ColumnCount = 3;
        _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60F));
        _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        // Wide enough for the localized "Browse…" text - at the old 60 the button's EndEllipsis
        // silently truncated it to "Bro...".
        _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        _topBar.Controls.Add(_leftLabel, 0, 0);
        _topBar.Controls.Add(_leftPathBox, 1, 0);
        _topBar.Controls.Add(_leftBrowseBtn, 2, 0);
        _topBar.Controls.Add(_rightLabel, 0, 1);
        _topBar.Controls.Add(_rightPathBox, 1, 1);
        _topBar.Controls.Add(_rightBrowseBtn, 2, 1);
        _topBar.Dock = DockStyle.Top;
        _topBar.Name = "_topBar";
        _topBar.Padding = new Padding(16, 8, 16, 8);
        _topBar.RowCount = 2;
        _topBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _topBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _topBar.Size = new Size(900, 88);
        _uiMetadata.SetThemeRole(_topBar, ThemeRole.Background);
        //
        // _leftLabel
        //
        _leftLabel.AutoSize = true;
        _leftLabel.Name = "_leftLabel";
        _leftLabel.Text = "Left:";
        _uiMetadata.SetLocalizationKey(_leftLabel, "Differ.Left");
        _uiMetadata.SetThemeRole(_leftLabel, ThemeRole.Body);
        //
        // _leftPathBox
        //
        _leftPathBox.BorderStyle = BorderStyle.FixedSingle;
        _leftPathBox.Dock = DockStyle.Fill;
        _leftPathBox.Name = "_leftPathBox";
        //
        // _leftBrowseBtn
        //
        _leftBrowseBtn.Dock = DockStyle.Fill;
        _leftBrowseBtn.Margin = new Padding(4, 3, 4, 7);
        _leftBrowseBtn.Name = "_leftBrowseBtn";
        _leftBrowseBtn.Role = ThemeRole.SecondaryButton;
        _leftBrowseBtn.Text = "Browse…";
        _uiMetadata.SetLocalizationKey(_leftBrowseBtn, "Common.Browse");
        //
        // _rightLabel
        //
        _rightLabel.AutoSize = true;
        _rightLabel.Name = "_rightLabel";
        _rightLabel.Text = "Right:";
        _uiMetadata.SetLocalizationKey(_rightLabel, "Differ.Right");
        _uiMetadata.SetThemeRole(_rightLabel, ThemeRole.Body);
        //
        // _rightPathBox
        //
        _rightPathBox.BorderStyle = BorderStyle.FixedSingle;
        _rightPathBox.Dock = DockStyle.Fill;
        _rightPathBox.Name = "_rightPathBox";
        //
        // _rightBrowseBtn
        //
        _rightBrowseBtn.Dock = DockStyle.Fill;
        _rightBrowseBtn.Margin = new Padding(4, 3, 4, 7);
        _rightBrowseBtn.Name = "_rightBrowseBtn";
        _rightBrowseBtn.Role = ThemeRole.SecondaryButton;
        _rightBrowseBtn.Text = "Browse…";
        _uiMetadata.SetLocalizationKey(_rightBrowseBtn, "Common.Browse");
        //
        // DifferForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(900, 616);
        // Fill before the Bottom and Top siblings - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_split);
        Controls.Add(_bottomPanel);
        Controls.Add(_topBar);
        MinimumSize = new Size(560, 400);
        Name = "DifferForm";
        Text = "Compare files";
        _uiMetadata.SetLocalizationKey(this, "Differ.Title");
        _split.Panel1.ResumeLayout(false);
        _split.Panel1.PerformLayout();
        _split.Panel2.ResumeLayout(false);
        _split.Panel2.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_split).EndInit();
        _split.ResumeLayout(false);
        _bottomPanel.ResumeLayout(false);
        _rightGroup.ResumeLayout(false);
        _topBar.ResumeLayout(false);
        _topBar.PerformLayout();
        ResumeLayout(false);
    }
}
