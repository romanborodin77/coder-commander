using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class NetworkBrowseForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TreeView _tree = null!;
    private Panel _bottomPanel = null!;
    private Label _statusLabel = null!;
    private RoundedButton _closeBtn = null!;
    private FlowLayoutPanel _rightGroup = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _tree?.Dispose();
            _closeBtn?.Dispose();
            _rightGroup?.Dispose();
            _statusLabel?.Dispose();
            _bottomPanel?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The status label carries no localization key - its text is progress
    /// reporting written by <c>PopulateRootAsync</c> as the scan runs.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _tree = new TreeView();
        _bottomPanel = new Panel();
        _statusLabel = new Label();
        _closeBtn = new RoundedButton();
        _rightGroup = new FlowLayoutPanel();
        _bottomPanel.SuspendLayout();
        _rightGroup.SuspendLayout();
        SuspendLayout();
        //
        // _tree
        //
        _tree.BorderStyle = BorderStyle.None;
        _tree.Dock = DockStyle.Fill;
        _tree.Name = "_tree";
        _tree.ShowLines = true;
        _tree.ShowPlusMinus = true;
        _tree.ShowRootLines = false;
        //
        // _bottomPanel
        //
        // Fill added before Right: WinForms lays docked children out from the highest Controls
        // index down, so the right-docked button claims its width first and the filling status
        // label takes what is left - the reverse order would leave the label under the button.
        _bottomPanel.Controls.Add(_statusLabel);
        _bottomPanel.Controls.Add(_rightGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(480, 50);
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
        // Docking _closeBtn straight into _bottomPanel would stretch it to that panel's
        // inner height (50 less 8px of padding top and bottom = 34px), leaving it
        // visibly taller than the same button on every other dialog. A right-docked FlowLayoutPanel
        // lets the button keep its natural size, and honours the Margin that Dock
        // ignores outright - the same shape ConnectionsForm uses for its own Close.
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_closeBtn);
        _rightGroup.Dock = DockStyle.Right;
        _rightGroup.FlowDirection = FlowDirection.LeftToRight;
        _rightGroup.Name = "_rightGroup";
        _rightGroup.WrapContents = false;
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
        // NetworkBrowseForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(480, 520);
        Controls.Add(_tree);
        Controls.Add(_bottomPanel);
        MinimumSize = new Size(320, 300);
        Name = "NetworkBrowseForm";
        Text = "Network";
        _uiMetadata.SetLocalizationKey(this, "Network.Title");
        _rightGroup.ResumeLayout(false);
        _bottomPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
