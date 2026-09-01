using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class MaterializingWaitForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _root = null!;
    private Label _messageLabel = null!;
    private ThemedProgressBar _progress = null!;
    private Panel _buttonRow = null!;
    private FlowLayoutPanel _buttonFlow = null!;
    private RoundedButton _cancelBtn = null!;
    private System.Windows.Forms.Timer _pulseTimer = null!;

    /// <summary>Explicit disposal of the control fields (CA2213). The timer belongs to
    /// <see cref="components"/>, so disposing that covers it.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _progress?.Dispose();
            _cancelBtn?.Dispose();
            _messageLabel?.Dispose();
            _buttonFlow?.Dispose();
            _buttonRow?.Dispose();
            _root?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The pulse timer is a designer component here but is started from the
    /// constructor - running it is behaviour, and a timer ticking inside the IDE would be exactly
    /// the kind of design-time side effect this migration is careful to avoid.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _pulseTimer = new System.Windows.Forms.Timer(components);
        _root = new TableLayoutPanel();
        _messageLabel = new Label();
        _progress = new ThemedProgressBar();
        _buttonRow = new Panel();
        _buttonFlow = new FlowLayoutPanel();
        _cancelBtn = new RoundedButton();
        _root.SuspendLayout();
        _buttonRow.SuspendLayout();
        _buttonFlow.SuspendLayout();
        SuspendLayout();
        //
        // _pulseTimer
        //
        _pulseTimer.Interval = 20;
        //
        // _root
        //
        _root.ColumnCount = 1;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _root.Controls.Add(_messageLabel, 0, 0);
        _root.Controls.Add(_progress, 0, 1);
        _root.Controls.Add(_buttonRow, 0, 2);
        _root.Dock = DockStyle.Fill;
        _root.Name = "_root";
        _root.Padding = new Padding(20, 20, 20, 16);
        _root.RowCount = 3;
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_root, ThemeRole.Background);
        //
        // _messageLabel
        //
        _messageLabel.AutoEllipsis = true;
        _messageLabel.AutoSize = true;
        _messageLabel.Dock = DockStyle.Fill;
        _messageLabel.Name = "_messageLabel";
        _messageLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_messageLabel, ThemeRole.Body);
        //
        // _progress
        //
        _progress.Dock = DockStyle.Fill;
        _progress.Maximum = 100;
        _progress.Name = "_progress";
        //
        // _buttonRow
        //
        _buttonRow.Controls.Add(_buttonFlow);
        _buttonRow.Dock = DockStyle.Bottom;
        _buttonRow.Name = "_buttonRow";
        _buttonRow.Size = new Size(380, 32);
        _uiMetadata.SetThemeRole(_buttonRow, ThemeRole.Background);
        //
        // _buttonFlow
        //
        _buttonFlow.AutoSize = true;
        _buttonFlow.Controls.Add(_cancelBtn);
        _buttonFlow.Dock = DockStyle.Right;
        _buttonFlow.FlowDirection = FlowDirection.LeftToRight;
        _buttonFlow.Name = "_buttonFlow";
        _buttonFlow.WrapContents = false;
        _uiMetadata.SetThemeRole(_buttonFlow, ThemeRole.Background);
        //
        // _cancelBtn
        //
        _cancelBtn.AutoSize = true;
        _cancelBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelBtn.MinimumSize = new Size(100, 32);
        // Name is the AutomationId the UI tests address this button by - not cosmetic.
        _cancelBtn.Name = "CancelButton";
        _cancelBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelBtn.Role = ThemeRole.SecondaryButton;
        _cancelBtn.Text = "Cancel";
        _uiMetadata.SetLocalizationKey(_cancelBtn, "Common.Cancel");
        //
        // MaterializingWaitForm
        //
        ClientSize = new Size(420, 128);
        // No close box: this dialog is dismissed by cancelling the operation behind it, never by
        // closing the window out from under it.
        ControlBox = false;
        Controls.Add(_root);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "MaterializingWaitForm";
        Text = "Downloading archive";
        _uiMetadata.SetLocalizationKey(this, "Archive.MaterializingTitle");
        _root.ResumeLayout(false);
        _root.PerformLayout();
        _buttonRow.ResumeLayout(false);
        _buttonRow.PerformLayout();
        _buttonFlow.ResumeLayout(false);
        ResumeLayout(false);
    }
}
