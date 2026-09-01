using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class OperationDialogForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _mainLayout = null!;
    private Panel _headerPanel = null!;
    private Label _iconLabel = null!;
    private Label _titleLabel = null!;
    private TableLayoutPanel _progressPanel = null!;
    private Label _cfLabel = null!;
    private Label _currentFileLabel = null!;
    private ThemedProgressBar _fileProgress = null!;
    private Label _totalLabel = null!;
    private TableLayoutPanel _statsPanel = null!;
    private ThemedProgressBar _overallProgress = null!;
    private TableLayoutPanel _infoPanel = null!;
    private Label _speedLabel = null!;
    private Label _etaLabel = null!;
    private Label _filesLabel = null!;
    private Label _stateLabel = null!;
    private Panel _spacer = null!;
    private Panel _btnPanel = null!;
    private FlowLayoutPanel _leftGroup = null!;
    private RoundedButton _skipBtn = null!;
    private RoundedButton _pauseBtn = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _cancelBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _iconLabel?.Dispose();
            _titleLabel?.Dispose();
            _cfLabel?.Dispose();
            _currentFileLabel?.Dispose();
            _fileProgress?.Dispose();
            _totalLabel?.Dispose();
            _overallProgress?.Dispose();
            _speedLabel?.Dispose();
            _etaLabel?.Dispose();
            _filesLabel?.Dispose();
            _stateLabel?.Dispose();
            _skipBtn?.Dispose();
            _pauseBtn?.Dispose();
            _cancelBtn?.Dispose();
            _leftGroup?.Dispose();
            _rightGroup?.Dispose();
            _btnPanel?.Dispose();
            _spacer?.Dispose();
            _infoPanel?.Dispose();
            _statsPanel?.Dispose();
            _progressPanel?.Dispose();
            _headerPanel?.Dispose();
            _mainLayout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Every value this dialog reports - speed, ETA, file counts, state -
    /// is written by the progress handlers at runtime, so only the captions that never change
    /// carry a LocalizationKey.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _mainLayout = new TableLayoutPanel();
        _headerPanel = new Panel();
        _iconLabel = new Label();
        _titleLabel = new Label();
        _progressPanel = new TableLayoutPanel();
        _cfLabel = new Label();
        _currentFileLabel = new Label();
        _fileProgress = new ThemedProgressBar();
        _totalLabel = new Label();
        _statsPanel = new TableLayoutPanel();
        _overallProgress = new ThemedProgressBar();
        _infoPanel = new TableLayoutPanel();
        _speedLabel = new Label();
        _etaLabel = new Label();
        _filesLabel = new Label();
        _stateLabel = new Label();
        _spacer = new Panel();
        _btnPanel = new Panel();
        _leftGroup = new FlowLayoutPanel();
        _skipBtn = new RoundedButton();
        _pauseBtn = new RoundedButton();
        _rightGroup = new FlowLayoutPanel();
        _cancelBtn = new RoundedButton();
        _mainLayout.SuspendLayout();
        _headerPanel.SuspendLayout();
        _progressPanel.SuspendLayout();
        _statsPanel.SuspendLayout();
        _infoPanel.SuspendLayout();
        _btnPanel.SuspendLayout();
        _leftGroup.SuspendLayout();
        _rightGroup.SuspendLayout();
        SuspendLayout();
        //
        // _mainLayout
        //
        _mainLayout.ColumnCount = 1;
        _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainLayout.Controls.Add(_headerPanel, 0, 0);
        _mainLayout.Controls.Add(_progressPanel, 0, 1);
        _mainLayout.Controls.Add(_statsPanel, 0, 2);
        _mainLayout.Controls.Add(_spacer, 0, 3);
        _mainLayout.Controls.Add(_btnPanel, 0, 4);
        _mainLayout.Dock = DockStyle.Fill;
        _mainLayout.Name = "_mainLayout";
        _mainLayout.Padding = new Padding(0);
        _mainLayout.RowCount = 5;
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));   // header
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));  // progress
        // 78, not 60: the stats panel needs 16 (overall bar) + 24 (speed/eta/files) + 12 (its own
        // bottom padding) + ~22 (state label, SectionFont 10pt bold). At 60 the state row was
        // starved to ~8px and clipped "Выполняется…" to a sliver bleeding into the row below.
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));   // stats
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // spacer
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));   // buttons
        _uiMetadata.SetThemeRole(_mainLayout, ThemeRole.Background);
        //
        // _headerPanel
        //
        _headerPanel.Controls.Add(_iconLabel);
        _headerPanel.Controls.Add(_titleLabel);
        _headerPanel.Dock = DockStyle.Fill;
        _headerPanel.Name = "_headerPanel";
        _headerPanel.Padding = new Padding(20, 16, 20, 16);
        _uiMetadata.SetThemeRole(_headerPanel, ThemeRole.HeaderBackground);
        //
        // _iconLabel
        //
        // Absolute Location: the icon is a hand-drawn vector painted by a Paint handler, not text
        // or an image, so there is nothing for a layout panel to measure.
        _iconLabel.Location = new Point(20, 14);
        _iconLabel.Name = "_iconLabel";
        _iconLabel.Size = new Size(32, 32);
        _iconLabel.TextAlign = ContentAlignment.MiddleCenter;
        //
        // _titleLabel
        //
        // Text is the caller-supplied display name.
        _titleLabel.AutoEllipsis = true;
        _titleLabel.AutoSize = true;
        _titleLabel.Location = new Point(64, 14);
        _titleLabel.MaximumSize = new Size(440, 32);
        _titleLabel.Name = "_titleLabel";
        _uiMetadata.SetThemeRole(_titleLabel, ThemeRole.Subtitle);
        //
        // _progressPanel
        //
        _progressPanel.ColumnCount = 1;
        _progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _progressPanel.Controls.Add(_cfLabel, 0, 0);
        _progressPanel.Controls.Add(_currentFileLabel, 0, 1);
        _progressPanel.Controls.Add(_fileProgress, 0, 2);
        _progressPanel.Controls.Add(_totalLabel, 0, 3);
        _progressPanel.Dock = DockStyle.Fill;
        _progressPanel.Name = "_progressPanel";
        _progressPanel.Padding = new Padding(20, 12, 20, 12);
        _progressPanel.RowCount = 4;
        _progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
        _progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        _uiMetadata.SetThemeRole(_progressPanel, ThemeRole.Background);
        //
        // _cfLabel
        //
        _cfLabel.AutoSize = true;
        _cfLabel.Dock = DockStyle.Fill;
        _cfLabel.Name = "_cfLabel";
        _cfLabel.Text = "Current file";
        _cfLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_cfLabel, "OpDlg.CurrentFile");
        _uiMetadata.SetThemeRole(_cfLabel, ThemeRole.Emphasis);
        //
        // _currentFileLabel
        //
        _currentFileLabel.AutoEllipsis = true;
        _currentFileLabel.AutoSize = true;
        _currentFileLabel.Dock = DockStyle.Fill;
        _currentFileLabel.Name = "_currentFileLabel";
        _currentFileLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_currentFileLabel, ThemeRole.Body);
        //
        // _fileProgress
        //
        _fileProgress.Dock = DockStyle.Fill;
        _fileProgress.Name = "_fileProgress";
        _fileProgress.Size = new Size(460, 8);
        //
        // _totalLabel
        //
        _totalLabel.AutoSize = true;
        _totalLabel.Dock = DockStyle.Fill;
        _totalLabel.Name = "_totalLabel";
        _totalLabel.Text = "Total";
        _totalLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_totalLabel, "OpDlg.Total");
        _uiMetadata.SetThemeRole(_totalLabel, ThemeRole.Emphasis);
        //
        // _statsPanel
        //
        _statsPanel.ColumnCount = 1;
        _statsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _statsPanel.Controls.Add(_overallProgress, 0, 0);
        _statsPanel.Controls.Add(_infoPanel, 0, 1);
        _statsPanel.Controls.Add(_stateLabel, 0, 2);
        _statsPanel.Dock = DockStyle.Fill;
        _statsPanel.Name = "_statsPanel";
        _statsPanel.Padding = new Padding(20, 0, 20, 12);
        _statsPanel.RowCount = 3;
        _statsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
        _statsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _statsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_statsPanel, ThemeRole.Background);
        //
        // _overallProgress
        //
        _overallProgress.Dock = DockStyle.Fill;
        _overallProgress.Name = "_overallProgress";
        _overallProgress.Size = new Size(460, 16);
        //
        // _infoPanel
        //
        _infoPanel.ColumnCount = 3;
        _infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
        _infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33F));
        _infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
        _infoPanel.Controls.Add(_speedLabel, 0, 0);
        _infoPanel.Controls.Add(_etaLabel, 1, 0);
        _infoPanel.Controls.Add(_filesLabel, 2, 0);
        _infoPanel.Dock = DockStyle.Fill;
        _infoPanel.Name = "_infoPanel";
        _infoPanel.RowCount = 1;
        _infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_infoPanel, ThemeRole.Background);
        //
        // _speedLabel
        //
        // All three interpolate live figures, so their text is written by the progress handler.
        _speedLabel.AutoSize = true;
        _speedLabel.Dock = DockStyle.Fill;
        _speedLabel.Name = "_speedLabel";
        _speedLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_speedLabel, ThemeRole.Body);
        //
        // _etaLabel
        //
        _etaLabel.AutoSize = true;
        _etaLabel.Dock = DockStyle.Fill;
        _etaLabel.Name = "_etaLabel";
        _etaLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_etaLabel, ThemeRole.Body);
        //
        // _filesLabel
        //
        _filesLabel.AutoSize = true;
        _filesLabel.Dock = DockStyle.Fill;
        _filesLabel.Name = "_filesLabel";
        _filesLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_filesLabel, ThemeRole.Body);
        //
        // _stateLabel
        //
        // No ThemeRole: its SectionFont + Accent pairing matches none of them (Section itself pairs
        // with HeaderForeground), so ApplyTheme sets both by hand on every theme switch.
        _stateLabel.Dock = DockStyle.Fill;
        _stateLabel.Name = "_stateLabel";
        _stateLabel.Text = "Running…";
        _stateLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_stateLabel, "OpDlg.Running");
        //
        // _spacer
        //
        _spacer.Dock = DockStyle.Fill;
        _spacer.Name = "_spacer";
        _uiMetadata.SetThemeRole(_spacer, ThemeRole.Background);
        //
        // _btnPanel
        //
        // Two FlowLayoutPanels rather than pixel Locations computed from the panel's Width during
        // construction, before it had actually been laid out - correctness used to depend on the
        // Resize handler firing before the first paint.
        _btnPanel.Controls.Add(_leftGroup);
        _btnPanel.Controls.Add(_rightGroup);
        _btnPanel.Dock = DockStyle.Fill;
        _btnPanel.Name = "_btnPanel";
        _btnPanel.Padding = new Padding(20, 12, 20, 12);
        _uiMetadata.SetThemeRole(_btnPanel, ThemeRole.HeaderBackground);
        //
        // _leftGroup
        //
        _leftGroup.AutoSize = true;
        _leftGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _leftGroup.BackColor = Color.Transparent;
        _leftGroup.Controls.Add(_skipBtn);
        _leftGroup.Controls.Add(_pauseBtn);
        _leftGroup.Dock = DockStyle.Left;
        _leftGroup.FlowDirection = FlowDirection.LeftToRight;
        _leftGroup.Name = "_leftGroup";
        _leftGroup.WrapContents = false;
        //
        // _skipBtn
        //
        // AutoSize with a floor, never a fixed Width: a hardcoded width truncated "Пропустить" to
        // "Пропус..." under Russian.
        _skipBtn.AutoSize = true;
        _skipBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _skipBtn.Margin = new Padding(0, 0, 8, 0);
        _skipBtn.MinimumSize = new Size(100, 36);
        _skipBtn.Name = "_skipBtn";
        _skipBtn.Padding = new Padding(20, 0, 20, 0);
        _skipBtn.Role = ThemeRole.SecondaryButton;
        _skipBtn.Text = "Skip";
        _uiMetadata.SetLocalizationKey(_skipBtn, "OpDlg.Skip");
        //
        // _pauseBtn
        //
        // Its caption toggles to Resume once paused, and AutoSize regrows the button when it does -
        // which is why no width is fixed here. Text is set through the localization key for the
        // Pause state and rewritten by OnOperationStateChanged for the Resume state.
        _pauseBtn.AutoSize = true;
        _pauseBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _pauseBtn.Margin = new Padding(0);
        _pauseBtn.MinimumSize = new Size(100, 36);
        _pauseBtn.Name = "_pauseBtn";
        _pauseBtn.Padding = new Padding(20, 0, 20, 0);
        _pauseBtn.Role = ThemeRole.SecondaryButton;
        _pauseBtn.Text = "Pause";
        _uiMetadata.SetLocalizationKey(_pauseBtn, "OpDlg.Pause");
        //
        // _rightGroup
        //
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_cancelBtn);
        _rightGroup.Dock = DockStyle.Right;
        _rightGroup.FlowDirection = FlowDirection.LeftToRight;
        _rightGroup.Name = "_rightGroup";
        _rightGroup.WrapContents = false;
        //
        // _cancelBtn
        //
        _cancelBtn.AutoSize = true;
        _cancelBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelBtn.Margin = new Padding(0);
        _cancelBtn.MinimumSize = new Size(100, 36);
        _cancelBtn.Name = "_cancelBtn";
        _cancelBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelBtn.Role = ThemeRole.PrimaryButton;
        _cancelBtn.Text = "Cancel";
        _uiMetadata.SetLocalizationKey(_cancelBtn, "OpDlg.Cancel");
        //
        // OperationDialogForm
        //
        ClientSize = new Size(540, 380);
        Controls.Add(_mainLayout);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "OperationDialogForm";
        Text = "Operation";
        _mainLayout.ResumeLayout(false);
        _headerPanel.ResumeLayout(false);
        _headerPanel.PerformLayout();
        _progressPanel.ResumeLayout(false);
        _progressPanel.PerformLayout();
        _statsPanel.ResumeLayout(false);
        _statsPanel.PerformLayout();
        _infoPanel.ResumeLayout(false);
        _infoPanel.PerformLayout();
        _btnPanel.ResumeLayout(false);
        _leftGroup.ResumeLayout(false);
        _rightGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
