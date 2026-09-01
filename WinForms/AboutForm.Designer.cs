using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class AboutForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _root = null!;
    private Panel _btnPanel = null!;
    private Panel _topSeparator = null!;
    private FlowLayoutPanel _buttons = null!;
    private RoundedButton _copyBtn = null!;
    private RoundedButton _closeBtn = null!;
    private System.Windows.Forms.Timer _fadeTimer = null!;

    /// <summary>Explicit disposal of the control fields (CA2213). The fade timer belongs to
    /// <see cref="components"/>, so disposing that covers it.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _copyBtn?.Dispose();
            _closeBtn?.Dispose();
            _buttons?.Dispose();
            _topSeparator?.Dispose();
            _btnPanel?.Dispose();
            _root?.Dispose();
            // Owned by the behaviour half - a form can carry only one Dispose(bool) override.
            _banner?.Dispose();
            _toolTip?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// The structural frame only - the root grid's four measured row heights, the button bar, and
    /// the two buttons.
    ///
    /// <para><b>Deliberately a partial conversion.</b> Rows 0-2 are filled in the constructor rather
    /// than here, because their content is not layout the designer could usefully hold: the banner is
    /// an owner-drawn control that paints itself, the info grid is a list of live environment facts
    /// (assembly version, GC memory, registered archive formats) that only exist at runtime, and the
    /// links row is a set of hyperlinks bound to click handlers. What the designer does own is
    /// exactly the part worth dragging - the row heights and the dialog's size.</para>
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _fadeTimer = new System.Windows.Forms.Timer(components);
        _root = new TableLayoutPanel();
        _btnPanel = new Panel();
        _topSeparator = new Panel();
        _buttons = new FlowLayoutPanel();
        _copyBtn = new RoundedButton();
        _closeBtn = new RoundedButton();
        _root.SuspendLayout();
        _btnPanel.SuspendLayout();
        _buttons.SuspendLayout();
        SuspendLayout();
        //
        // _fadeTimer
        //
        _fadeTimer.Interval = 16;
        //
        // _root
        //
        _root.ColumnCount = 1;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _root.Controls.Add(_btnPanel, 0, 3);
        _root.Dock = DockStyle.Fill;
        _root.Name = "_root";
        _root.Padding = new Padding(0);
        _root.RowCount = 4;
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132F));  // banner
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // info grid
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));   // links
        // 68, not 60: the bar's own margin (3+3), padding (10+10) and 1px separator eat 27px, and
        // the buttons need 40 (34 tall plus 3+3 margin). At 60 the flow panel ended up 33 tall and
        // clipped the bottom 4px off both buttons.
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68F));   // buttons
        _uiMetadata.SetThemeRole(_root, ThemeRole.Background);
        //
        // _btnPanel
        //
        // Fill-ish content added before the Top separator so the separator lands above it.
        _btnPanel.Controls.Add(_buttons);
        _btnPanel.Controls.Add(_topSeparator);
        _btnPanel.Dock = DockStyle.Fill;
        _btnPanel.Name = "_btnPanel";
        _btnPanel.Padding = new Padding(20, 10, 20, 10);
        _uiMetadata.SetThemeRole(_btnPanel, ThemeRole.HeaderBackground);
        //
        // _topSeparator
        //
        _topSeparator.Dock = DockStyle.Top;
        _topSeparator.Name = "_topSeparator";
        _topSeparator.Size = new Size(480, 1);
        _uiMetadata.SetThemeRole(_topSeparator, ThemeRole.PanelBackground);
        //
        // _buttons
        //
        // Margin is ignored on a control docked straight into a Panel, so the buttons live in a
        // docked FlowLayoutPanel to get the gap between them. GrowAndShrink matters too: without it
        // the panel keeps its GrowOnly default, does not tighten to the buttons, and they end up
        // laid out past its edge.
        _buttons.AutoSize = true;
        _buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttons.Controls.Add(_copyBtn);
        _buttons.Controls.Add(_closeBtn);
        _buttons.Dock = DockStyle.Right;
        _buttons.FlowDirection = FlowDirection.LeftToRight;
        _buttons.Name = "_buttons";
        _buttons.WrapContents = false;
        _uiMetadata.SetThemeRole(_buttons, ThemeRole.HeaderBackground);
        //
        // _copyBtn
        //
        // Height-only, never a fixed Size: AutoSize measures the button's own localized text, and a
        // hardcoded width truncated "Скопировать сведения" to "Скопировать све..." under Russian.
        _copyBtn.AutoSize = true;
        _copyBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _copyBtn.Margin = new Padding(0, 3, 10, 3);
        _copyBtn.MinimumSize = new Size(100, 34);
        _copyBtn.Name = "_copyBtn";
        _copyBtn.Padding = new Padding(20, 0, 20, 0);
        _copyBtn.Role = ThemeRole.SecondaryButton;
        _copyBtn.Text = "Copy info";
        _uiMetadata.SetLocalizationKey(_copyBtn, "About.CopyInfo");
        //
        // _closeBtn
        //
        _closeBtn.AutoSize = true;
        _closeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _closeBtn.Margin = new Padding(0, 3, 0, 3);
        _closeBtn.MinimumSize = new Size(100, 34);
        _closeBtn.Name = "_closeBtn";
        _closeBtn.Padding = new Padding(20, 0, 20, 0);
        _closeBtn.Role = ThemeRole.PrimaryButton;
        _closeBtn.Text = "Close";
        _uiMetadata.SetLocalizationKey(_closeBtn, "Common.Close");
        //
        // AboutForm
        //
        AcceptButton = _closeBtn;
        CancelButton = _closeBtn;
        ClientSize = new Size(520, 484);
        Controls.Add(_root);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "AboutForm";
        Text = "About";
        _uiMetadata.SetLocalizationKey(this, "About.Title");
        _root.ResumeLayout(false);
        _btnPanel.ResumeLayout(false);
        _buttons.ResumeLayout(false);
        ResumeLayout(false);
    }
}
