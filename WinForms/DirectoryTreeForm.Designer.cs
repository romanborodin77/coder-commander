using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class DirectoryTreeForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TreeView _tree = null!;
    private Panel _bottomPanel = null!;
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
            _bottomPanel?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only - see <see cref="UiMetadataProvider"/> for how roles and localization
    /// keys reach runtime. The TreeView's own colours come from <see cref="ControlThemer"/>'s
    /// TreeView case on load, so none are set here.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _tree = new TreeView();
        _bottomPanel = new Panel();
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
        _tree.ShowRootLines = true;
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_rightGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(480, 50);
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
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
        // DirectoryTreeForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(480, 520);
        // Dock=Fill before its Dock=Bottom sibling: WinForms lays docked children out from the
        // last-added index down to the first, so adding Fill afterwards left the tree's layout
        // extending under the bottom panel (invisible only because that panel is opaque).
        Controls.Add(_tree);
        Controls.Add(_bottomPanel);
        MinimumSize = new Size(320, 300);
        Name = "DirectoryTreeForm";
        Text = "Directory tree";
        _uiMetadata.SetLocalizationKey(this, "DirTree.Title");
        _rightGroup.ResumeLayout(false);
        _bottomPanel.ResumeLayout(false);
        ResumeLayout(false);
    }
}
