using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class ChecksumVerifyForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private ListView _resultList = null!;
    private ColumnHeader _colFileName = null!;
    private ColumnHeader _colStatus = null!;
    private ColumnHeader _colHash = null!;
    private Panel _bottomPanel = null!;
    private Label _statusLabel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _closeBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213). ColumnHeaders belong to the
    /// ListView's own collection and are disposed with it.
    ///
    /// <para>Also disposes <c>_cts</c>, which belongs to the behaviour half of this partial class - a
    /// form can only carry one <c>Dispose(bool)</c> override, and by designer convention it lives
    /// here.</para></summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _resultList?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _cts?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Column header captions are localized in the constructor - a
    /// <see cref="ColumnHeader"/> is not a <see cref="Control"/>, so it cannot carry a
    /// LocalizationKey through <see cref="UiMetadataProvider"/> the way a real control does.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _resultList = new ListView();
        _colFileName = new ColumnHeader();
        _colStatus = new ColumnHeader();
        _colHash = new ColumnHeader();
        _bottomPanel = new Panel();
        _statusLabel = new Label();
        _buttonGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _resultList
        //
        _resultList.BorderStyle = BorderStyle.None;
        _resultList.Columns.AddRange(new[] { _colFileName, _colStatus, _colHash });
        _resultList.Dock = DockStyle.Fill;
        _resultList.FullRowSelect = true;
        _resultList.Name = "_resultList";
        _resultList.UseCompatibleStateImageBehavior = false;
        _resultList.View = View.Details;
        //
        // _colFileName
        //
        _colFileName.Text = "File";
        _colFileName.Width = 320;
        //
        // _colStatus
        //
        _colStatus.Text = "Status";
        _colStatus.Width = 140;
        //
        // _colHash
        //
        _colHash.Text = "Hash";
        _colHash.Width = 160;
        //
        // _bottomPanel
        //
        // Fill added before Right: the right-docked button group claims its width first, and the
        // filling status label takes what is left.
        _bottomPanel.Controls.Add(_statusLabel);
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(640, 50);
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        //
        // _statusLabel
        //
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Name = "_statusLabel";
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_statusLabel, ThemeRole.Muted);
        //
        // _buttonGroup
        //
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonGroup.BackColor = Color.Transparent;
        _buttonGroup.Controls.Add(_closeBtn);
        _buttonGroup.Dock = DockStyle.Right;
        _buttonGroup.FlowDirection = FlowDirection.LeftToRight;
        _buttonGroup.Name = "_buttonGroup";
        _buttonGroup.WrapContents = false;
        //
        // _closeBtn
        //
        _closeBtn.AutoSize = true;
        _closeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _closeBtn.Margin = new Padding(0);
        _closeBtn.MinimumSize = new Size(100, 32);
        _closeBtn.Name = "_closeBtn";
        _closeBtn.Padding = new Padding(20, 0, 20, 0);
        _closeBtn.Role = ThemeRole.SecondaryButton;
        _closeBtn.Text = "Close";
        _uiMetadata.SetLocalizationKey(_closeBtn, "Common.Close");
        //
        // ChecksumVerifyForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(640, 480);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_resultList);
        Controls.Add(_bottomPanel);
        MinimumSize = new Size(420, 300);
        Name = "ChecksumVerifyForm";
        Text = "Verify checksums";
        _bottomPanel.ResumeLayout(false);
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
