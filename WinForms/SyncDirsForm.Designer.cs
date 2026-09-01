using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class SyncDirsForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private ListView _diffList = null!;
    private ColumnHeader _colStatus = null!;
    private ColumnHeader _colPath = null!;
    private ColumnHeader _colLeftSize = null!;
    private ColumnHeader _colRightSize = null!;
    private ColumnHeader _colAction = null!;
    private Panel _bottom = null!;
    private Label _statusLabel = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _closeBtn = null!;
    private RoundedButton _copyRightBtn = null!;
    private RoundedButton _copyLeftBtn = null!;
    private TableLayoutPanel _top = null!;
    private Label _leftLabel = null!;
    private TextBox _leftBox = null!;
    private RoundedButton _leftBrowse = null!;
    private Label _rightLabel = null!;
    private TextBox _rightBox = null!;
    private RoundedButton _rightBrowse = null!;
    private ThemedCheckBox _subdirsCheck = null!;
    private ThemedCheckBox _ignoreTimeCheck = null!;
    private RoundedButton _compareBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _diffList?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
            _copyRightBtn?.Dispose();
            _copyLeftBtn?.Dispose();
            _leftLabel?.Dispose();
            _rightLabel?.Dispose();
            _leftBox?.Dispose();
            _rightBox?.Dispose();
            _leftBrowse?.Dispose();
            _rightBrowse?.Dispose();
            _subdirsCheck?.Dispose();
            _ignoreTimeCheck?.Dispose();
            _compareBtn?.Dispose();
            _rightGroup?.Dispose();
            _bottom?.Dispose();
            _top?.Dispose();
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
        _diffList = new ListView();
        _colStatus = new ColumnHeader();
        _colPath = new ColumnHeader();
        _colLeftSize = new ColumnHeader();
        _colRightSize = new ColumnHeader();
        _colAction = new ColumnHeader();
        _bottom = new Panel();
        _statusLabel = new Label();
        _rightGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        _copyRightBtn = new RoundedButton();
        _copyLeftBtn = new RoundedButton();
        _top = new TableLayoutPanel();
        _leftLabel = new Label();
        _leftBox = new TextBox();
        _leftBrowse = new RoundedButton();
        _rightLabel = new Label();
        _rightBox = new TextBox();
        _rightBrowse = new RoundedButton();
        _subdirsCheck = new ThemedCheckBox();
        _ignoreTimeCheck = new ThemedCheckBox();
        _compareBtn = new RoundedButton();
        _bottom.SuspendLayout();
        _rightGroup.SuspendLayout();
        _top.SuspendLayout();
        SuspendLayout();
        //
        // _diffList
        //
        _diffList.BorderStyle = BorderStyle.None;
        _diffList.CheckBoxes = true;
        _diffList.Columns.AddRange(new[] { _colStatus, _colPath, _colLeftSize, _colRightSize, _colAction });
        _diffList.Dock = DockStyle.Fill;
        _diffList.FullRowSelect = true;
        _diffList.Name = "_diffList";
        _diffList.UseCompatibleStateImageBehavior = false;
        _diffList.View = View.Details;
        //
        // _colStatus
        //
        _colStatus.Text = "Status";
        _colStatus.Width = 60;
        //
        // _colPath
        //
        _colPath.Text = "Path";
        _colPath.Width = 380;
        //
        // _colLeftSize
        //
        _colLeftSize.Text = "Left size";
        _colLeftSize.Width = 100;
        //
        // _colRightSize
        //
        _colRightSize.Text = "Right size";
        _colRightSize.Width = 100;
        //
        // _colAction
        //
        _colAction.Text = "Action";
        _colAction.Width = 200;
        //
        // _bottom
        //
        // Fill added before Right: the right-docked button group claims its width first and the
        // filling status label takes what is left.
        _bottom.Controls.Add(_statusLabel);
        _bottom.Controls.Add(_rightGroup);
        _bottom.Dock = DockStyle.Bottom;
        _bottom.Name = "_bottom";
        _bottom.Padding = new Padding(16, 8, 16, 8);
        _bottom.Size = new Size(880, 50);
        _uiMetadata.SetThemeRole(_bottom, ThemeRole.HeaderBackground);
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
        // Three Dock.Right buttons ignored Margin entirely and collapsed every gap between them - a
        // right-aligned FlowLayoutPanel renders them, keeping the same Close/CopyRight/CopyLeft
        // order the old same-side docking produced.
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_closeBtn);
        _rightGroup.Controls.Add(_copyRightBtn);
        _rightGroup.Controls.Add(_copyLeftBtn);
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
        // _copyRightBtn
        //
        _copyRightBtn.AutoSize = true;
        _copyRightBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _copyRightBtn.Margin = new Padding(0, 0, 8, 0);
        _copyRightBtn.MinimumSize = new Size(100, 32);
        _copyRightBtn.Name = "_copyRightBtn";
        _copyRightBtn.Padding = new Padding(20, 0, 20, 0);
        _copyRightBtn.Role = ThemeRole.SecondaryButton;
        _copyRightBtn.Text = "Copy to right →";
        _uiMetadata.SetLocalizationKey(_copyRightBtn, "SyncDirs.CopyToRight");
        //
        // _copyLeftBtn
        //
        _copyLeftBtn.AutoSize = true;
        _copyLeftBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _copyLeftBtn.Margin = new Padding(0);
        _copyLeftBtn.MinimumSize = new Size(100, 32);
        _copyLeftBtn.Name = "_copyLeftBtn";
        _copyLeftBtn.Padding = new Padding(20, 0, 20, 0);
        _copyLeftBtn.Role = ThemeRole.SecondaryButton;
        _copyLeftBtn.Text = "← Copy to left";
        _uiMetadata.SetLocalizationKey(_copyLeftBtn, "SyncDirs.CopyToLeft");
        //
        // _top
        //
        // 4 rows * 32 + Padding(12+12) = 152 - the previous 132 left the last row (the Compare
        // button) squeezed to about 12px tall.
        _top.ColumnCount = 4;
        _top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        _top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        // 130, not 80: this column also holds _ignoreTimeCheck's column span (with the 120px one
        // below) on row 2, and the Russian "Игнорировать время (только размер)" is wider than the
        // English caption - at 80 the checkbox hard-clipped it.
        _top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        // 120, not 80 or 100: wide enough for the localized "Browse…" (at 80 it truncated to
        // "Brows...") and for "Compare" in the same column on row 3 (at 100, "Comp..."). Text
        // truncation is not something a Bounds-based check catches - only a rendered screenshot.
        _top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        _top.Controls.Add(_leftLabel, 0, 0);
        _top.Controls.Add(_leftBox, 1, 0);
        _top.Controls.Add(_leftBrowse, 3, 0);
        _top.Controls.Add(_rightLabel, 0, 1);
        _top.Controls.Add(_rightBox, 1, 1);
        _top.Controls.Add(_rightBrowse, 3, 1);
        _top.Controls.Add(_subdirsCheck, 1, 2);
        _top.Controls.Add(_ignoreTimeCheck, 2, 2);
        // Row 3, not row 2: _ignoreTimeCheck already spans columns 2-3 on row 2, and sharing that
        // cell squeezed the button down to "Com...".
        _top.Controls.Add(_compareBtn, 3, 3);
        _top.Dock = DockStyle.Top;
        _top.Name = "_top";
        _top.Padding = new Padding(16, 12, 16, 12);
        _top.RowCount = 4;
        _top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _top.SetColumnSpan(_ignoreTimeCheck, 2);
        _top.Size = new Size(880, 152);
        _uiMetadata.SetThemeRole(_top, ThemeRole.Background);
        //
        // _leftLabel
        //
        _leftLabel.AutoSize = true;
        _leftLabel.Name = "_leftLabel";
        _leftLabel.Text = "Left:";
        _uiMetadata.SetLocalizationKey(_leftLabel, "SyncDirs.Left");
        _uiMetadata.SetThemeRole(_leftLabel, ThemeRole.Body);
        //
        // _leftBox
        //
        _leftBox.BorderStyle = BorderStyle.FixedSingle;
        _leftBox.Dock = DockStyle.Fill;
        _leftBox.Name = "_leftBox";
        //
        // _leftBrowse
        //
        _leftBrowse.Dock = DockStyle.Fill;
        _leftBrowse.Margin = new Padding(8, 0, 0, 0);
        _leftBrowse.Name = "_leftBrowse";
        _leftBrowse.Role = ThemeRole.SecondaryButton;
        _leftBrowse.Text = "Browse…";
        _uiMetadata.SetLocalizationKey(_leftBrowse, "Common.Browse");
        //
        // _rightLabel
        //
        _rightLabel.AutoSize = true;
        _rightLabel.Name = "_rightLabel";
        _rightLabel.Text = "Right:";
        _uiMetadata.SetLocalizationKey(_rightLabel, "SyncDirs.Right");
        _uiMetadata.SetThemeRole(_rightLabel, ThemeRole.Body);
        //
        // _rightBox
        //
        _rightBox.BorderStyle = BorderStyle.FixedSingle;
        _rightBox.Dock = DockStyle.Fill;
        _rightBox.Name = "_rightBox";
        //
        // _rightBrowse
        //
        _rightBrowse.Dock = DockStyle.Fill;
        _rightBrowse.Margin = new Padding(8, 0, 0, 0);
        _rightBrowse.Name = "_rightBrowse";
        _rightBrowse.Role = ThemeRole.SecondaryButton;
        _rightBrowse.Text = "Browse…";
        _uiMetadata.SetLocalizationKey(_rightBrowse, "Common.Browse");
        //
        // _subdirsCheck
        //
        _subdirsCheck.Dock = DockStyle.Fill;
        _subdirsCheck.Name = "_subdirsCheck";
        _subdirsCheck.Text = "Include subdirectories";
        _uiMetadata.SetLocalizationKey(_subdirsCheck, "SyncDirs.Subdirs");
        //
        // _ignoreTimeCheck
        //
        _ignoreTimeCheck.Dock = DockStyle.Fill;
        _ignoreTimeCheck.Name = "_ignoreTimeCheck";
        _ignoreTimeCheck.Text = "Ignore time (size only)";
        _uiMetadata.SetLocalizationKey(_ignoreTimeCheck, "SyncDirs.IgnoreTime");
        //
        // _compareBtn
        //
        // Margin = 0: this cell's RowStyle is Absolute 32, and the default 3px-per-side margin
        // would shrink the button's rendered height to 26px.
        _compareBtn.Dock = DockStyle.Fill;
        _compareBtn.Margin = new Padding(0);
        _compareBtn.Name = "_compareBtn";
        _compareBtn.Role = ThemeRole.PrimaryButton;
        _compareBtn.Text = "Compare";
        _uiMetadata.SetLocalizationKey(_compareBtn, "SyncDirs.Compare");
        //
        // SyncDirsForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(880, 620);
        // Fill before the Bottom and Top siblings - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_diffList);
        Controls.Add(_bottom);
        Controls.Add(_top);
        MinimumSize = new Size(600, 420);
        Name = "SyncDirsForm";
        Text = "Synchronize directories";
        _uiMetadata.SetLocalizationKey(this, "SyncDirs.Title");
        _bottom.ResumeLayout(false);
        _rightGroup.ResumeLayout(false);
        _top.ResumeLayout(false);
        _top.PerformLayout();
        ResumeLayout(false);
    }
}
