using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Modeless "downloading archive" indicator shown while <c>MainForm.EnterArchiveAsync</c> pulls a
/// non-local archive down to a local temp copy before it can be browsed (audit finding G044) - a
/// multi-gigabyte archive on FTP/SFTP/WebDAV used to block entering it with no feedback and no way
/// to back out short of killing the app. There is no byte-level progress to report (the underlying
/// <c>MaterializedFile.AcquireAsync</c> stream copy has no <c>IProgress&lt;T&gt;</c> hook), so this
/// shows an indeterminate pulse rather than a real percentage - honest about what is actually known.
/// <see cref="CancelRequested"/> is the caller's cue to cancel the <see cref="CancellationTokenSource"/>
/// it passed into the materialize call; this form never cancels anything itself.
/// </summary>
public sealed class MaterializingWaitForm : ThemedForm
{
    private readonly ThemedProgressBar _progress;
    private readonly System.Windows.Forms.Timer _pulseTimer;
    private int _pulseDirection = 1;

    /// <summary>True once the user has clicked Cancel. The caller polls this via
    /// <see cref="CancelClicked"/>, not by reading the property directly on a timer.</summary>
    public bool CancelRequested { get; private set; }

    /// <summary>Raised once, the first time the user clicks Cancel.</summary>
    public event EventHandler? CancelClicked;

    public MaterializingWaitForm(string itemName)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        Text = L.GetString("Archive.MaterializingTitle");
        ClientSize = new Size(420, 128);
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor = p.Background;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(20, 20, 20, 16)
        };
        root.SetRole(ThemeRole.Background);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var messageLabel = UiHelpers.CreateLabel(L.GetString("Archive.MaterializingMessage", itemName));
        messageLabel.Dock = DockStyle.Fill;
        messageLabel.AutoEllipsis = true;
        messageLabel.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(messageLabel, 0, 0);

        _progress = new ThemedProgressBar { Dock = DockStyle.Fill, Height = 8, Maximum = 100 };
        root.Controls.Add(_progress, 0, 1);

        var buttonFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        buttonFlow.SetRole(ThemeRole.Background);

        var cancelBtn = CreateThemedButton(L.GetString("Common.Cancel"), name: "CancelButton");
        cancelBtn.Click += (_, _) =>
        {
            if (CancelRequested) return;
            CancelRequested = true;
            cancelBtn.Enabled = false;
            CancelClicked?.Invoke(this, EventArgs.Empty);
        };
        buttonFlow.Controls.Add(cancelBtn);

        var buttonRow = new Panel { Dock = DockStyle.Bottom, Height = 32 };
        buttonRow.SetRole(ThemeRole.Background);
        buttonRow.Controls.Add(buttonFlow); // Dock=Fill sibling first (docking-order pitfall - N/A here, buttonFlow docks Right)
        root.Controls.Add(buttonRow, 0, 2);

        Controls.Add(root);

        // Indeterminate bounce - the only honest representation available without real byte
        // progress. Ping-pongs across the bar's own [0, Maximum] range.
        _pulseTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _pulseTimer.Tick += (_, _) =>
        {
            var next = _progress.Value + _pulseDirection * 3;
            if (next >= _progress.Maximum) { next = _progress.Maximum; _pulseDirection = -1; }
            else if (next <= _progress.Minimum) { next = _progress.Minimum; _pulseDirection = 1; }
            _progress.Value = next;
        };
        _pulseTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pulseTimer.Dispose();
            _progress.Dispose();
        }
        base.Dispose(disposing);
    }
}
