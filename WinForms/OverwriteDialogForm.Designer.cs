using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class OverwriteDialogForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _root = null!;
    private TableLayoutPanel _content = null!;
    private Label _fileLabel = null!;
    private Panel _sourceBox = null!;
    private TableLayoutPanel _sourceLayout = null!;
    private Label _sourceTitle = null!;
    private Label _sourceValue = null!;
    private Label _vsLabel = null!;
    private Panel _destBox = null!;
    private TableLayoutPanel _destLayout = null!;
    private Label _destTitle = null!;
    private Label _destValue = null!;
    private Panel _btnPanel = null!;
    private TableLayoutPanel _btnGrid = null!;
    private RoundedButton _overwriteBtn = null!;
    private RoundedButton _skipBtn = null!;
    private RoundedButton _renameBtn = null!;
    private RoundedButton _overwriteAllBtn = null!;
    private RoundedButton _skipAllBtn = null!;
    private RoundedButton _overwriteOlderBtn = null!;
    private Button _escapeBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _fileLabel?.Dispose();
            _sourceTitle?.Dispose();
            _sourceValue?.Dispose();
            _sourceLayout?.Dispose();
            _sourceBox?.Dispose();
            _vsLabel?.Dispose();
            _destTitle?.Dispose();
            _destValue?.Dispose();
            _destLayout?.Dispose();
            _destBox?.Dispose();
            _overwriteBtn?.Dispose();
            _skipBtn?.Dispose();
            _renameBtn?.Dispose();
            _overwriteAllBtn?.Dispose();
            _skipAllBtn?.Dispose();
            _overwriteOlderBtn?.Dispose();
            _escapeBtn?.Dispose();
            _btnGrid?.Dispose();
            _btnPanel?.Dispose();
            _content?.Dispose();
            _root?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Layout only. The two info boxes came from a shared <c>CreateInfoBox</c> factory and the six
    /// policy buttons from a loop over a tuple array; both are written out here because the designer
    /// can only round-trip explicit controls.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _root = new TableLayoutPanel();
        _content = new TableLayoutPanel();
        _fileLabel = new Label();
        _sourceBox = new Panel();
        _sourceLayout = new TableLayoutPanel();
        _sourceTitle = new Label();
        _sourceValue = new Label();
        _vsLabel = new Label();
        _destBox = new Panel();
        _destLayout = new TableLayoutPanel();
        _destTitle = new Label();
        _destValue = new Label();
        _btnPanel = new Panel();
        _btnGrid = new TableLayoutPanel();
        _overwriteBtn = new RoundedButton();
        _skipBtn = new RoundedButton();
        _renameBtn = new RoundedButton();
        _overwriteAllBtn = new RoundedButton();
        _skipAllBtn = new RoundedButton();
        _overwriteOlderBtn = new RoundedButton();
        _escapeBtn = new Button();
        _root.SuspendLayout();
        _content.SuspendLayout();
        _sourceBox.SuspendLayout();
        _sourceLayout.SuspendLayout();
        _destBox.SuspendLayout();
        _destLayout.SuspendLayout();
        _btnPanel.SuspendLayout();
        _btnGrid.SuspendLayout();
        SuspendLayout();
        //
        // _root
        //
        _root.ColumnCount = 1;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _root.Controls.Add(_content, 0, 0);
        _root.Controls.Add(_btnPanel, 0, 1);
        _root.Dock = DockStyle.Fill;
        _root.Name = "_root";
        _root.Padding = new Padding(0);
        _root.RowCount = 2;
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        // 88, not 72: with two rows of buttons, the panel's Padding(12,6,12,8) and each button's
        // Margin(2), 72 left about 25px per row - under the 30px floor ThemeSingleControl tries to
        // enforce.
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
        _uiMetadata.SetThemeRole(_root, ThemeRole.Background);
        //
        // _content
        //
        _content.ColumnCount = 1;
        _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _content.Controls.Add(_fileLabel, 0, 0);
        _content.Controls.Add(_sourceBox, 0, 1);
        _content.Controls.Add(_vsLabel, 0, 2);
        _content.Controls.Add(_destBox, 0, 3);
        _content.Dock = DockStyle.Fill;
        _content.Name = "_content";
        _content.Padding = new Padding(16, 12, 16, 8);
        _content.RowCount = 5;
        _content.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        _content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        _content.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
        _content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        _content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_content, ThemeRole.Background);
        //
        // _fileLabel
        //
        _fileLabel.AutoEllipsis = true;
        _fileLabel.Dock = DockStyle.Fill;
        _fileLabel.Name = "_fileLabel";
        _fileLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_fileLabel, ThemeRole.Body);
        //
        // _sourceBox
        //
        _sourceBox.Controls.Add(_sourceLayout);
        _sourceBox.Dock = DockStyle.Fill;
        _sourceBox.Name = "_sourceBox";
        _sourceBox.Padding = new Padding(10, 5, 10, 5);
        _uiMetadata.SetThemeRole(_sourceBox, ThemeRole.PanelBackground);
        //
        // _sourceLayout
        //
        _sourceLayout.BackColor = Color.Transparent;
        _sourceLayout.ColumnCount = 2;
        _sourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
        _sourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _sourceLayout.Controls.Add(_sourceTitle, 0, 0);
        _sourceLayout.Controls.Add(_sourceValue, 1, 0);
        _sourceLayout.Dock = DockStyle.Fill;
        _sourceLayout.Name = "_sourceLayout";
        _sourceLayout.RowCount = 1;
        _sourceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _sourceTitle
        //
        // The .lng values for OverwriteDlg.Source/.Destination already end in ":" - appending
        // another one produced a literal "Источник::".
        _sourceTitle.Dock = DockStyle.Fill;
        _sourceTitle.Name = "_sourceTitle";
        _sourceTitle.Text = "Source:";
        _sourceTitle.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_sourceTitle, "OverwriteDlg.Source");
        _uiMetadata.SetThemeRole(_sourceTitle, ThemeRole.Emphasis);
        //
        // _sourceValue
        //
        _sourceValue.AutoEllipsis = true;
        _sourceValue.Dock = DockStyle.Fill;
        _sourceValue.Name = "_sourceValue";
        _sourceValue.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_sourceValue, ThemeRole.Body);
        //
        // _vsLabel
        //
        _vsLabel.Dock = DockStyle.Fill;
        _vsLabel.Name = "_vsLabel";
        _vsLabel.Text = "vs";
        _vsLabel.TextAlign = ContentAlignment.MiddleCenter;
        _uiMetadata.SetLocalizationKey(_vsLabel, "OverwriteDlg.Vs");
        _uiMetadata.SetThemeRole(_vsLabel, ThemeRole.Hint);
        //
        // _destBox
        //
        _destBox.Controls.Add(_destLayout);
        _destBox.Dock = DockStyle.Fill;
        _destBox.Name = "_destBox";
        _destBox.Padding = new Padding(10, 5, 10, 5);
        _uiMetadata.SetThemeRole(_destBox, ThemeRole.PanelBackground);
        //
        // _destLayout
        //
        _destLayout.BackColor = Color.Transparent;
        _destLayout.ColumnCount = 2;
        _destLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
        _destLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _destLayout.Controls.Add(_destTitle, 0, 0);
        _destLayout.Controls.Add(_destValue, 1, 0);
        _destLayout.Dock = DockStyle.Fill;
        _destLayout.Name = "_destLayout";
        _destLayout.RowCount = 1;
        _destLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _destTitle
        //
        _destTitle.Dock = DockStyle.Fill;
        _destTitle.Name = "_destTitle";
        _destTitle.Text = "Destination:";
        _destTitle.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_destTitle, "OverwriteDlg.Destination");
        _uiMetadata.SetThemeRole(_destTitle, ThemeRole.Emphasis);
        //
        // _destValue
        //
        _destValue.AutoEllipsis = true;
        _destValue.Dock = DockStyle.Fill;
        _destValue.Name = "_destValue";
        _destValue.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_destValue, ThemeRole.Body);
        //
        // _btnPanel
        //
        _btnPanel.Controls.Add(_btnGrid);
        _btnPanel.Dock = DockStyle.Fill;
        _btnPanel.Name = "_btnPanel";
        _btnPanel.Padding = new Padding(12, 6, 12, 8);
        _uiMetadata.SetThemeRole(_btnPanel, ThemeRole.HeaderBackground);
        //
        // _btnGrid
        //
        _btnGrid.BackColor = Color.Transparent;
        _btnGrid.ColumnCount = 3;
        _btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
        _btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        _btnGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
        _btnGrid.Controls.Add(_overwriteBtn, 0, 0);
        _btnGrid.Controls.Add(_skipBtn, 1, 0);
        _btnGrid.Controls.Add(_renameBtn, 2, 0);
        _btnGrid.Controls.Add(_overwriteAllBtn, 0, 1);
        _btnGrid.Controls.Add(_skipAllBtn, 1, 1);
        _btnGrid.Controls.Add(_overwriteOlderBtn, 2, 1);
        _btnGrid.Dock = DockStyle.Fill;
        _btnGrid.Name = "_btnGrid";
        _btnGrid.Padding = new Padding(0);
        _btnGrid.RowCount = 2;
        _btnGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        _btnGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        //
        // _overwriteBtn
        //
        _overwriteBtn.Dock = DockStyle.Fill;
        _overwriteBtn.Margin = new Padding(2);
        _overwriteBtn.Name = "_overwriteBtn";
        _overwriteBtn.Role = ThemeRole.PrimaryButton;
        _overwriteBtn.Text = "Overwrite";
        _uiMetadata.SetLocalizationKey(_overwriteBtn, "Overwrite.Overwrite");
        //
        // _skipBtn
        //
        _skipBtn.Dock = DockStyle.Fill;
        _skipBtn.Margin = new Padding(2);
        _skipBtn.Name = "_skipBtn";
        _skipBtn.Role = ThemeRole.SecondaryButton;
        _skipBtn.Text = "Skip";
        _uiMetadata.SetLocalizationKey(_skipBtn, "Overwrite.Skip");
        //
        // _renameBtn
        //
        _renameBtn.Dock = DockStyle.Fill;
        _renameBtn.Margin = new Padding(2);
        _renameBtn.Name = "_renameBtn";
        _renameBtn.Role = ThemeRole.SecondaryButton;
        _renameBtn.Text = "Rename";
        _uiMetadata.SetLocalizationKey(_renameBtn, "Overwrite.Rename");
        //
        // _overwriteAllBtn
        //
        _overwriteAllBtn.Dock = DockStyle.Fill;
        _overwriteAllBtn.Margin = new Padding(2);
        _overwriteAllBtn.Name = "_overwriteAllBtn";
        _overwriteAllBtn.Role = ThemeRole.SecondaryButton;
        _overwriteAllBtn.Text = "Overwrite all";
        _uiMetadata.SetLocalizationKey(_overwriteAllBtn, "Overwrite.OverwriteAll");
        //
        // _skipAllBtn
        //
        _skipAllBtn.Dock = DockStyle.Fill;
        _skipAllBtn.Margin = new Padding(2);
        _skipAllBtn.Name = "_skipAllBtn";
        _skipAllBtn.Role = ThemeRole.SecondaryButton;
        _skipAllBtn.Text = "Skip all";
        _uiMetadata.SetLocalizationKey(_skipAllBtn, "Overwrite.SkipAll");
        //
        // _overwriteOlderBtn
        //
        _overwriteOlderBtn.Dock = DockStyle.Fill;
        _overwriteOlderBtn.Margin = new Padding(2);
        _overwriteOlderBtn.Name = "_overwriteOlderBtn";
        _overwriteOlderBtn.Role = ThemeRole.SecondaryButton;
        _overwriteOlderBtn.Text = "Overwrite if older";
        _uiMetadata.SetLocalizationKey(_overwriteOlderBtn, "Overwrite.OverwriteOlder");
        //
        // _escapeBtn
        //
        // No policy button means "cancel", but Escape should still close the dialog - the caller
        // already treats a non-OK result as "skip this file".
        _escapeBtn.DialogResult = DialogResult.Cancel;
        _escapeBtn.Name = "_escapeBtn";
        _escapeBtn.Visible = false;
        //
        // OverwriteDialogForm
        //
        CancelButton = _escapeBtn;
        // Width 640, not 500: the narrowest policy column has to fit "Перезаписать если старее" -
        // 151px of text plus 44px of button padding = 195px minimum, which 500px could not give
        // (about 157px per column after the dialog's own padding), truncating it to
        // "Перезаписать ес...".
        ClientSize = new Size(640, 292);
        Controls.Add(_root);
        Controls.Add(_escapeBtn);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "OverwriteDialogForm";
        Text = "File already exists";
        _uiMetadata.SetLocalizationKey(this, "OverwriteDlg.Title");
        _root.ResumeLayout(false);
        _content.ResumeLayout(false);
        _sourceBox.ResumeLayout(false);
        _sourceLayout.ResumeLayout(false);
        _destBox.ResumeLayout(false);
        _destLayout.ResumeLayout(false);
        _btnPanel.ResumeLayout(false);
        _btnGrid.ResumeLayout(false);
        ResumeLayout(false);
    }
}
