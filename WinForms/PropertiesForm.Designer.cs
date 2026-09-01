using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class PropertiesForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private Panel _scroll = null!;
    private TableLayoutPanel _root = null!;
    private Panel _bottom = null!;
    private Label _statusLabel = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _closeBtn = null!;
    private RoundedButton _applyBtn = null!;
    private RoundedButton _resetBtn = null!;
    private FlowLayoutPanel _leftGroup = null!;

    /// <summary>Explicit disposal of the control fields (CA2213). Controls added to
    /// <see cref="_root"/> by the section builders are owned by that panel and disposed with it.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
            _applyBtn?.Dispose();
            _resetBtn?.Dispose();
            _leftGroup?.Dispose();
            _rightGroup?.Dispose();
            _bottom?.Dispose();
            _root?.Dispose();
            _scroll?.Dispose();
            // Owned by the behaviour half - a form can carry only one Dispose(bool) override.
            _cts?.Dispose();
            _recursiveCheckbox?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// The frame only - the scroll host, the empty content grid, and the button bar.
    ///
    /// <para><b>Deliberately a partial conversion.</b> The five sections inside <see cref="_root"/>
    /// are appended by BuildHeader/BuildInfoSection/BuildAttributesSection/BuildRecursiveCheckbox/
    /// BuildTimestampSection at runtime, and cannot live here: which of them exist at all depends on
    /// the filesystem's capabilities (Attributes, NativePaths) and on whether the selection is one
    /// item or many, a directory or a file - and every row they add is read from live file metadata.
    /// What the designer does own is the part worth dragging: the window's size bounds, the content
    /// column's width, the section padding and the whole button bar.</para>
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _scroll = new Panel();
        _root = new TableLayoutPanel();
        _bottom = new Panel();
        _statusLabel = new Label();
        _rightGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        _applyBtn = new RoundedButton();
        _resetBtn = new RoundedButton();
        _leftGroup = new FlowLayoutPanel();
        _scroll.SuspendLayout();
        _bottom.SuspendLayout();
        _leftGroup.SuspendLayout();
        _rightGroup.SuspendLayout();
        SuspendLayout();
        //
        // _scroll
        //
        _scroll.AutoScroll = true;
        _scroll.Controls.Add(_root);
        _scroll.Dock = DockStyle.Fill;
        _scroll.Name = "_scroll";
        _uiMetadata.SetThemeRole(_scroll, ThemeRole.Background);
        //
        // _root
        //
        // AutoSize + Dock=Top so the grid grows downward as sections are appended and the scroll
        // host above it takes over once it exceeds the window.
        _root.AutoSize = true;
        _root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _root.ColumnCount = 1;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480F));
        _root.Dock = DockStyle.Top;
        _root.Name = "_root";
        _root.Padding = new Padding(20, 18, 20, 12);
        _root.RowCount = 0;
        _uiMetadata.SetThemeRole(_root, ThemeRole.Background);
        //
        // _bottom
        //
        // Fill added first so it docks last and takes the remainder, then the two edge-docked groups.
        _bottom.Controls.Add(_statusLabel);
        _bottom.Controls.Add(_rightGroup);
        _bottom.Controls.Add(_leftGroup);
        _bottom.Dock = DockStyle.Bottom;
        _bottom.Name = "_bottom";
        _bottom.Padding = new Padding(16, 10, 16, 10);
        _bottom.Size = new Size(540, 54);
        _uiMetadata.SetThemeRole(_bottom, ThemeRole.HeaderBackground);
        //
        // _statusLabel
        //
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Name = "_statusLabel";
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_statusLabel, ThemeRole.Muted);
        //
        // _rightGroup
        //
        // A FlowLayoutPanel rather than two Dock.Right buttons: docking ignores Margin, and
        // same-side docking stacks from the last-added control outward - together those had
        // rendered Close as the rightmost, primary-looking button instead of the accent Apply, with
        // no gap between them either.
        _rightGroup.AutoSize = true;
        _rightGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rightGroup.BackColor = Color.Transparent;
        _rightGroup.Controls.Add(_closeBtn);
        _rightGroup.Controls.Add(_applyBtn);
        _rightGroup.Dock = DockStyle.Right;
        _rightGroup.FlowDirection = FlowDirection.LeftToRight;
        _rightGroup.Name = "_rightGroup";
        _rightGroup.WrapContents = false;
        //
        // _closeBtn
        //
        _closeBtn.AutoSize = true;
        _closeBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _closeBtn.DialogResult = DialogResult.Cancel;
        _closeBtn.Margin = new Padding(0, 0, 8, 0);
        _closeBtn.MinimumSize = new Size(100, 32);
        _closeBtn.Name = "_closeBtn";
        _closeBtn.Padding = new Padding(20, 0, 20, 0);
        _closeBtn.Role = ThemeRole.SecondaryButton;
        _closeBtn.Text = "Close";
        _uiMetadata.SetLocalizationKey(_closeBtn, "Common.Close");
        //
        // _applyBtn
        //
        _applyBtn.AutoSize = true;
        _applyBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _applyBtn.Margin = new Padding(0);
        _applyBtn.MinimumSize = new Size(100, 32);
        _applyBtn.Name = "_applyBtn";
        _applyBtn.Padding = new Padding(20, 0, 20, 0);
        _applyBtn.Role = ThemeRole.PrimaryButton;
        _applyBtn.Text = "Apply";
        _uiMetadata.SetLocalizationKey(_applyBtn, "Common.Apply");
        //
        // _leftGroup
        //
        // Docking _resetBtn straight into _bottom would stretch it to that panel's
        // inner height (54 less 10px of padding top and bottom = 34px), leaving it
        // visibly taller than the 32px buttons in _rightGroup. A left-docked FlowLayoutPanel
        // lets the button keep its natural size, and honours the Margin that Dock
        // ignores outright - the same shape ConnectionsForm uses for its own Close.
        _leftGroup.AutoSize = true;
        _leftGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _leftGroup.BackColor = Color.Transparent;
        _leftGroup.Controls.Add(_resetBtn);
        _leftGroup.Dock = DockStyle.Left;
        _leftGroup.FlowDirection = FlowDirection.LeftToRight;
        _leftGroup.Name = "_leftGroup";
        _leftGroup.WrapContents = false;
        //
        // _resetBtn
        //
        _resetBtn.AutoSize = true;
        _resetBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _resetBtn.Margin = new Padding(0);
        _resetBtn.MinimumSize = new Size(100, 32);
        _resetBtn.Name = "_resetBtn";
        _resetBtn.Padding = new Padding(20, 0, 20, 0);
        _resetBtn.Role = ThemeRole.SecondaryButton;
        _resetBtn.Text = "Reset";
        _uiMetadata.SetLocalizationKey(_resetBtn, "Common.Reset");
        //
        // PropertiesForm
        //
        AcceptButton = _applyBtn;
        CancelButton = _closeBtn;
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_scroll);
        Controls.Add(_bottom);
        MaximizeBox = false;
        // 540, not 520: the content column is Absolute 480 plus Padding(20, _, 20, _) = 520, which
        // was exactly the old window width with nothing left for the AutoScroll panel's ~17px
        // vertical scrollbar - the header and info labels lost their last 15-30px to AutoEllipsis.
        MaximumSize = new Size(540, 1200);
        MinimumSize = new Size(540, 360);
        MinimizeBox = false;
        Name = "PropertiesForm";
        Text = "Properties";
        _scroll.ResumeLayout(false);
        _scroll.PerformLayout();
        _leftGroup.ResumeLayout(false);
        _bottom.ResumeLayout(false);
        _rightGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
