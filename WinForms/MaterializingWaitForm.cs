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
///
/// <para>Layout lives in <c>MaterializingWaitForm.Designer.cs</c> and is editable in Visual Studio.</para>
/// </summary>
public sealed partial class MaterializingWaitForm : ThemedForm
{
    private int _pulseDirection = 1;

    /// <summary>True once the user has clicked Cancel. The caller polls this via
    /// <see cref="CancelClicked"/>, not by reading the property directly on a timer.</summary>
    public bool CancelRequested { get; private set; }

    /// <summary>Raised once, the first time the user clicks Cancel.</summary>
    public event EventHandler? CancelClicked;

    public MaterializingWaitForm(string itemName)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        // Interpolates the item name, so it cannot travel as a plain LocalizationKey.
        _messageLabel.Text = LocalizationService.Current.GetString("Archive.MaterializingMessage", itemName);

        _cancelBtn.Click += OnCancelClicked;
        _pulseTimer.Tick += OnPulseTick;
        _pulseTimer.Start();
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        if (CancelRequested) return;
        CancelRequested = true;
        _cancelBtn.Enabled = false;
        CancelClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Indeterminate bounce - the only honest representation available without real byte
    /// progress. Ping-pongs across the bar's own [Minimum, Maximum] range.</summary>
    private void OnPulseTick(object? sender, EventArgs e)
    {
        var next = _progress.Value + _pulseDirection * 3;
        if (next >= _progress.Maximum) { next = _progress.Maximum; _pulseDirection = -1; }
        else if (next <= _progress.Minimum) { next = _progress.Minimum; _pulseDirection = 1; }
        _progress.Value = next;
    }
}
