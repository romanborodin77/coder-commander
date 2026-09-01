using CoderCommander.Operations;
using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Modern file-operation progress dialog with dual progress bars, speed/ETA display,
/// current file preview, and control buttons.
/// </summary>
public sealed partial class OperationDialogForm : ThemedForm
{
    private readonly IFileOperation _operation;

    /// <summary>Raised when the user clicks Skip.</summary>
    public event EventHandler? SkipRequested;

    /// <param name="operation">The file operation to display progress for.</param>
    /// <param name="displayName">Display name shown in the dialog header.</param>
    public OperationDialogForm(IFileOperation operation, string displayName)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _operation = operation;
        var L = LocalizationService.Current;

        // operation.Title is a stable, always-English identifier (relied on as such elsewhere - see
        // UiTests/OperationDialogsTests.cs on why CopyOperation.Title stays "Copy"), so it is
        // resolved through Op.Title.* for display rather than shown raw, which had left the window
        // title in English regardless of UI language.
        Text = L.GetString("Op.Title." + operation.Title);
        _titleLabel.Text = displayName;

        _speedLabel.Text = L.GetString("OpDlg.Speed", "0 B");
        _etaLabel.Text = L.GetString("OpDlg.ETA", "0:00");
        _filesLabel.Text = L.GetString("OpDlg.Files", 0, 0);

        // Reads ThemeService.Current.Accent live rather than a palette captured here, so the vector
        // icon does not freeze at the theme active when the dialog was constructed.
        _iconLabel.Paint += (_, e) => DrawOperationIcon(e.Graphics, operation.Type, ThemeService.Current.Accent);

        // Both buttons only make sense for an operation that actually checks for pause/skip in its
        // own per-file loop (audit findings G051/G052 - Pause was permanently disabled with no
        // handler at all, and Skip raised an event nobody subscribed to).
        _pauseBtn.Enabled = operation.SupportsPauseAndSkip;
        _skipBtn.Enabled = operation.SupportsPauseAndSkip;

        _skipBtn.Click += (_, _) =>
        {
            _operation.RequestSkip();
            SkipRequested?.Invoke(this, EventArgs.Empty);
        };
        _pauseBtn.Click += (_, _) =>
        {
            if (_operation.State == OperationState.Paused)
                _operation.Resume();
            else
                _operation.Pause();
        };
        _cancelBtn.Click += (_, _) =>
        {
            _operation.Cancel();
            Close();
        };

        _operation.StateChanged += OnOperationStateChanged;

        FormClosing += (_, _) =>
        {
            _operation.StateChanged -= OnOperationStateChanged;
            // Closing via the X button should cancel the operation, not leave it running in the
            // background. Cancel is a no-op if the operation is already in a terminal state.
            _operation.Cancel();
        };
    }

    protected override void ApplyTheme()
    {
        base.ApplyTheme();
        // _stateLabel's SectionFont + Accent combination doesn't map to any general text role
        // (Section itself pairs with HeaderForeground) - without this, the untagged-Label
        // default would reset it to GridFont/HeaderForeground on every theme switch.
        var p = DesignerSafeThemeService.Current;
        _stateLabel.Font = p.SectionFont;
        _stateLabel.ForeColor = p.Accent;
    }

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
            if (_operation.SupportsPauseAndSkip)
            {
                // Toggle label/target between Pause and Resume as the operation's own state
                // actually changes, rather than the button click handler guessing - state can also
                // change from Cancel() releasing a pause wait (see FileOperation.Cancel), which the
                // click handler never sees directly.
                _pauseBtn.Text = state == OperationState.Paused
                    ? L.GetString("OpDlg.Resume")
                    : L.GetString("OpDlg.Pause");
                // Skip works even while paused - RequestSkip() cancels the same per-file token
                // WaitIfPausedAsync is registered against, so it interrupts a paused wait too, then
                // the operation returns to waiting on the next file (still paused, since Resume()
                // was never called) rather than silently un-pausing.
                var active = state is OperationState.Running or OperationState.Paused;
                _pauseBtn.Enabled = active;
                _skipBtn.Enabled = active;
            }

            if (state is OperationState.Completed or OperationState.Canceled or OperationState.Failed)
            {
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
            _speedLabel.Text = L.GetString("OpDlg.Speed", UiHelpers.FormatSize(p.Speed));
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
