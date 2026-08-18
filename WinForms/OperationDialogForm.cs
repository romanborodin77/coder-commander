using CoderCommander.Operations;
using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Modern file-operation progress dialog with dual progress bars, speed/ETA display,
/// current file preview, and control buttons.
/// </summary>
public class OperationDialogForm : ThemedForm
{
    private readonly IFileOperation _operation;
    private readonly Label _titleLabel;
    private readonly Label _currentFileLabel;
    private readonly ThemedProgressBar _fileProgress;
    private readonly ThemedProgressBar _overallProgress;
    private readonly Label _speedLabel;
    private readonly Label _etaLabel;
    private readonly Label _filesLabel;
    private readonly Label _stateLabel;
    private readonly Button _skipBtn;
    private readonly Button _pauseBtn;
    private readonly Button _cancelBtn;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Label _iconLabel;

    /// <summary>Raised when the user clicks Skip.</summary>
    public event EventHandler? SkipRequested;

    /// <param name="operation">The file operation to display progress for.</param>
    /// <param name="displayName">Display name shown in the dialog header.</param>
    public OperationDialogForm(IFileOperation operation, string displayName)
    {
        _operation = operation;
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        // operation.Title is a stable, always-English identifier (relied on as such elsewhere -
        // see UiTests/OperationDialogsTests.cs's own doc comment on why CopyOperation.Title stays
        // "Copy") - resolve it through Op.Title.* for display instead of showing it raw, which
        // previously left the window title bar in English regardless of UI language (caught by
        // visual inspection of a live build; the header label below it was already localized via
        // the caller-supplied displayName).
        Text = L.GetString("Op.Title." + operation.Title);
        ClientSize = new Size(540, 380);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = p.Background;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(0),
            BackColor = p.Background
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));  // Progress
        // statsPanel below needs 16 (overall progress) + 24 (speed/eta/files) + 12 (its own bottom
        // padding) + ~22 (state label, SectionFont 10pt bold) = 74px; 60 starved the state label's
        // row down to ~8px, clipping "Выполняется…"/etc. to a sliver bleeding into the row below
        // (caught by visual inspection of a live build).
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));   // Stats
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // Spacer
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));   // Buttons

        // ── Header ──
        var headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(20, 16, 20, 16)
        };

        _iconLabel = new Label
        {
            Text = "",
            Location = new Point(20, 14),
            Size = new Size(32, 32),
            TextAlign = ContentAlignment.MiddleCenter
        };
        // Reads ThemeService.Current.Accent live rather than the `p` captured here, so the
        // vector icon (drawn by hand, not from Text/Font) doesn't freeze at the theme that was
        // active when the dialog was constructed.
        _iconLabel.Paint += (_, e) => DrawOperationIcon(e.Graphics, operation.Type, ThemeService.Current.Accent);

        _titleLabel = new Label
        {
            Text = displayName,
            Font = p.SubtitleFont,
            ForeColor = p.Foreground,
            Location = new Point(64, 14),
            AutoSize = true,
            AutoEllipsis = true,
            MaximumSize = new Size(440, 32),
            Tag = ThemeRole.Subtitle
        };

        headerPanel.Controls.Add(_iconLabel);
        headerPanel.Controls.Add(_titleLabel);
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // ── Progress ──
        var progressPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = p.Background
        };
        progressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        progressPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));

        var cfLabel = UiHelpers.CreateLabel(L.GetString("OpDlg.CurrentFile"), bold: true);
        cfLabel.Dock = DockStyle.Fill;
        cfLabel.TextAlign = ContentAlignment.MiddleLeft;
        progressPanel.Controls.Add(cfLabel, 0, 0);

        _currentFileLabel = UiHelpers.CreateLabel("");
        _currentFileLabel.Dock = DockStyle.Fill;
        _currentFileLabel.TextAlign = ContentAlignment.MiddleLeft;
        _currentFileLabel.AutoEllipsis = true;
        progressPanel.Controls.Add(_currentFileLabel, 0, 1);

        _fileProgress = new ThemedProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 8
        };
        progressPanel.Controls.Add(_fileProgress, 0, 2);

        var totalLabel = UiHelpers.CreateLabel(L.GetString("OpDlg.Total"), bold: true);
        totalLabel.Dock = DockStyle.Fill;
        totalLabel.TextAlign = ContentAlignment.MiddleLeft;
        progressPanel.Controls.Add(totalLabel, 0, 3);

        mainLayout.Controls.Add(progressPanel, 0, 1);

        // ── Overall progress + stats ──
        var statsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 0, 20, 12),
            BackColor = p.Background
        };
        statsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        statsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        statsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _overallProgress = new ThemedProgressBar
        {
            Dock = DockStyle.Fill,
            Height = 16
        };
        statsPanel.Controls.Add(_overallProgress, 0, 0);

        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = p.Background
        };
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _speedLabel = UiHelpers.CreateLabel(L.GetString("OpDlg.Speed", "0 B/s"));
        _speedLabel.Dock = DockStyle.Fill;
        _speedLabel.TextAlign = ContentAlignment.MiddleLeft;
        _etaLabel = UiHelpers.CreateLabel(L.GetString("OpDlg.ETA", "0:00"));
        _etaLabel.Dock = DockStyle.Fill;
        _etaLabel.TextAlign = ContentAlignment.MiddleLeft;
        _filesLabel = UiHelpers.CreateLabel(L.GetString("OpDlg.Files", 0, 0));
        _filesLabel.Dock = DockStyle.Fill;
        _filesLabel.TextAlign = ContentAlignment.MiddleLeft;
        infoPanel.Controls.Add(_speedLabel, 0, 0);
        infoPanel.Controls.Add(_etaLabel, 1, 0);
        infoPanel.Controls.Add(_filesLabel, 2, 0);
        statsPanel.Controls.Add(infoPanel, 0, 1);

        _stateLabel = new Label
        {
            Text = L.GetString("OpDlg.Running"),
            Font = p.SectionFont,
            ForeColor = p.Accent,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        statsPanel.Controls.Add(_stateLabel, 0, 2);

        mainLayout.Controls.Add(statsPanel, 0, 2);

        // Spacer
        var spacer = new Panel { Dock = DockStyle.Fill, BackColor = p.Background };
        mainLayout.Controls.Add(spacer, 0, 3);

        // ── Buttons ─
        // Two FlowLayoutPanels (Dock.Left for Skip+Pause, Dock.Right for Cancel) instead of
        // pixel Locations computed from btnPanel.Width in the constructor, before the panel had
        // actually been laid out - correctness used to depend entirely on the Resize handler
        // below firing before the first paint.
        var btnPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(20, 12, 20, 12)
        };

        // Fixed Size (Width) previously clobbered CreateThemedButton's own text-measured width
        // (same class of bug as WinForms/AboutForm.cs/MultiRenameForm.cs), truncating
        // "Пропустить" ("Skip") to "Пропус..." under Russian (caught by visual inspection of a
        // live build) - Height-only override keeps the buttons a consistent height.
        _skipBtn = ThemedForm.CreateThemedButton(L.GetString("OpDlg.Skip"));
        _skipBtn.Height = 36;
        _skipBtn.Margin = new Padding(0, 0, 8, 0);
        _skipBtn.Click += (_, _) => SkipRequested?.Invoke(this, EventArgs.Empty);

        _pauseBtn = ThemedForm.CreateThemedButton(L.GetString("OpDlg.Pause"));
        _pauseBtn.Height = 36;
        _pauseBtn.Margin = new Padding(0);
        _pauseBtn.Enabled = false;

        var leftGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        leftGroup.Controls.Add(_skipBtn);
        leftGroup.Controls.Add(_pauseBtn);
        btnPanel.Controls.Add(leftGroup);

        _cancelBtn = ThemedForm.CreateThemedButton(L.GetString("OpDlg.Cancel"), accent: true);
        _cancelBtn.Height = 36;
        _cancelBtn.Margin = new Padding(0);
        _cancelBtn.Click += (_, _) =>
        {
            _operation.Cancel();
            Close();
        };

        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        rightGroup.Controls.Add(_cancelBtn);
        btnPanel.Controls.Add(rightGroup);

        mainLayout.Controls.Add(btnPanel, 0, 4);

        Controls.Add(mainLayout);

        _timer = new System.Windows.Forms.Timer { Interval = 200 };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _operation.StateChanged += OnOperationStateChanged;

        FormClosing += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            _operation.StateChanged -= OnOperationStateChanged;
            // Closing via the X button should cancel the operation, not leave it running in the background.
            // Cancel is a no-op if the operation is already in a terminal state (Completed/Canceled/Failed).
            _operation.Cancel();
        };
    }

    protected override void ApplyTheme()
    {
        base.ApplyTheme();
        // _stateLabel's SectionFont + Accent combination doesn't map to any general text role
        // (Section itself pairs with HeaderForeground) - without this, the untagged-Label
        // default would reset it to GridFont/HeaderForeground on every theme switch.
        var p = ThemeService.Current;
        _stateLabel.Font = p.SectionFont;
        _stateLabel.ForeColor = p.Accent;
    }

    private void OnTimerTick(object? sender, EventArgs e) { }

    private void OnOperationStateChanged(object? sender, OperationState state)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            var L = LocalizationService.Current;
            _stateLabel.Text = state switch
            {
                OperationState.Running => L.GetString("OpDlg.Running"),
                OperationState.Paused => L.GetString("OpDlg.Paused"),
                OperationState.Completed => L.GetString("OpDlg.Completed"),
                OperationState.Canceled => L.GetString("OpDlg.Canceled"),
                OperationState.Failed => L.GetString("OpDlg.Failed"),
                _ => ""
            };
            if (state is OperationState.Completed or OperationState.Canceled or OperationState.Failed)
            {
                _timer.Stop();
                if (state == OperationState.Completed)
                {
                    _overallProgress.Value = 100;
                    _fileProgress.Value = 100;
                }
                Task.Delay(800).ContinueWith(_ =>
                {
                    if (IsHandleCreated) BeginInvoke(Close);
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
            }
        });
    }

    /// <summary>Updates the progress display from an OperationProgress report.</summary>
    public void UpdateProgress(OperationProgress p)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            var L = LocalizationService.Current;
            _currentFileLabel.Text = p.CurrentFile;
            _overallProgress.Value = Math.Min(p.Percent, 100);
            _fileProgress.Value = Math.Min(p.Percent, 100);
            _speedLabel.Text = L.GetString("OpDlg.Speed", UiHelpers.FormatSize(p.Speed) + "/s");
            _etaLabel.Text = L.GetString("OpDlg.ETA", p.Remaining);
            _filesLabel.Text = L.GetString("OpDlg.Files", p.FilesProcessed, p.FilesTotal);
        });
    }

    private static void DrawOperationIcon(Graphics g, OperationType type, Color accent)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(accent, 2f);
        using var brush = new SolidBrush(accent);

        switch (type)
        {
            case OperationType.Copy:
                // Two overlapping squares
                g.DrawRectangle(pen, 4, 4, 14, 14);
                g.DrawRectangle(pen, 10, 10, 14, 14);
                break;
            case OperationType.Move:
                // Arrow right
                g.DrawLine(pen, 4, 14, 22, 14);
                g.FillPolygon(brush, new[] { new Point(18, 8), new Point(26, 14), new Point(18, 20) });
                break;
            case OperationType.Delete:
                // Trash can
                g.DrawRectangle(pen, 8, 10, 12, 14);
                g.DrawLine(pen, 6, 10, 22, 10);
                g.DrawLine(pen, 12, 6, 16, 6);
                break;
            case OperationType.Pack:
                // Box with arrow down
                g.DrawRectangle(pen, 6, 6, 16, 16);
                g.DrawLine(pen, 14, 4, 14, 14);
                g.FillPolygon(brush, new[] { new Point(10, 10), new Point(14, 16), new Point(18, 10) });
                break;
            case OperationType.Unpack:
                // Box with arrow up
                g.DrawRectangle(pen, 6, 6, 16, 16);
                g.DrawLine(pen, 14, 14, 14, 4);
                g.FillPolygon(brush, new[] { new Point(10, 8), new Point(14, 2), new Point(18, 8) });
                break;
            case OperationType.Split:
                // One box splitting into three smaller ones
                g.DrawRectangle(pen, 10, 2, 8, 8);
                g.DrawRectangle(pen, 2, 16, 6, 6);
                g.DrawRectangle(pen, 11, 16, 6, 6);
                g.DrawRectangle(pen, 20, 16, 6, 6);
                break;
            case OperationType.Combine:
                // Three small boxes merging into one
                g.DrawRectangle(pen, 2, 2, 6, 6);
                g.DrawRectangle(pen, 11, 2, 6, 6);
                g.DrawRectangle(pen, 20, 2, 6, 6);
                g.DrawRectangle(pen, 10, 16, 8, 8);
                break;
            default:
                g.DrawEllipse(pen, 6, 6, 16, 16);
                break;
        }
    }
}
