using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class DuplicateFinderForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private ListView _resultList = null!;
    private ColumnHeader _colName = null!;
    private ColumnHeader _colSize = null!;
    private ColumnHeader _colPath = null!;
    private Panel _bottomPanel = null!;
    private Label _statusLabel = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _closeBtn = null!;
    private RoundedButton _gotoBtn = null!;
    private RoundedButton _deleteBtn = null!;
    private RoundedButton _scanBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213), plus the two non-designer
    /// resources this form owns - a form can carry only one <c>Dispose(bool)</c> override and by
    /// designer convention it lives here.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _resultList?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
            _gotoBtn?.Dispose();
            _deleteBtn?.Dispose();
            _scanBtn?.Dispose();
            _rightGroup?.Dispose();
            _bottomPanel?.Dispose();
            _cts?.Dispose();
            _boldFont?.Dispose();
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
        _resultList = new ListView();
        _colName = new ColumnHeader();
        _colSize = new ColumnHeader();
        _colPath = new ColumnHeader();
        _bottomPanel = new Panel();
        _statusLabel = new Label();
        _rightGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        _gotoBtn = new RoundedButton();
        _deleteBtn = new RoundedButton();
        _scanBtn = new RoundedButton();
        _bottomPanel.SuspendLayout();
        _rightGroup.SuspendLayout();
        SuspendLayout();
        //
        // _resultList
        //
        _resultList.BorderStyle = BorderStyle.None;
        _resultList.CheckBoxes = true;
        _resultList.Columns.AddRange(new[] { _colName, _colSize, _colPath });
        _resultList.Dock = DockStyle.Fill;
        _resultList.FullRowSelect = true;
        _resultList.Name = "_resultList";
        _resultList.UseCompatibleStateImageBehavior = false;
        _resultList.View = View.Details;
        //
        // _colName
        //
        _colName.Text = "Name";
        _colName.Width = 280;
        //
        // _colSize
        //
        _colSize.Text = "Size";
        _colSize.Width = 100;
        //
        // _colPath
        //
        _colPath.Text = "Path";
        _colPath.Width = 280;
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
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_closeBtn);
        _rightGroup.Controls.Add(_gotoBtn);
        _rightGroup.Controls.Add(_deleteBtn);
        _rightGroup.Controls.Add(_scanBtn);
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
        // _gotoBtn
        //
        _gotoBtn.AutoSize = true;
        _gotoBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _gotoBtn.Enabled = false;
        _gotoBtn.Margin = new Padding(0, 0, 8, 0);
        _gotoBtn.MinimumSize = new Size(100, 32);
        _gotoBtn.Name = "_gotoBtn";
        _gotoBtn.Padding = new Padding(20, 0, 20, 0);
        _gotoBtn.Role = ThemeRole.SecondaryButton;
        _gotoBtn.Text = "Go to";
        _uiMetadata.SetLocalizationKey(_gotoBtn, "Dup.GoTo");
        //
        // _deleteBtn
        //
        _deleteBtn.AutoSize = true;
        _deleteBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _deleteBtn.Enabled = false;
        _deleteBtn.Margin = new Padding(0, 0, 8, 0);
        _deleteBtn.MinimumSize = new Size(100, 32);
        _deleteBtn.Name = "_deleteBtn";
        _deleteBtn.Padding = new Padding(20, 0, 20, 0);
        _deleteBtn.Role = ThemeRole.SecondaryButton;
        _deleteBtn.Text = "Delete";
        _uiMetadata.SetLocalizationKey(_deleteBtn, "Dup.Delete");
        //
        // _scanBtn
        //
        _scanBtn.AutoSize = true;
        _scanBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _scanBtn.Margin = new Padding(0);
        _scanBtn.MinimumSize = new Size(100, 32);
        _scanBtn.Name = "_scanBtn";
        _scanBtn.Padding = new Padding(20, 0, 20, 0);
        _scanBtn.Role = ThemeRole.PrimaryButton;
        _scanBtn.Text = "Scan";
        _uiMetadata.SetLocalizationKey(_scanBtn, "Dup.Scan");
        //
        // DuplicateFinderForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(700, 520);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_resultList);
        Controls.Add(_bottomPanel);
        MinimumSize = new Size(480, 360);
        Name = "DuplicateFinderForm";
        Text = "Find duplicates";
        _uiMetadata.SetLocalizationKey(this, "Dup.Title");
        _bottomPanel.ResumeLayout(false);
        _rightGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
