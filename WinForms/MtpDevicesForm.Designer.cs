using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class MtpDevicesForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private SplitContainer _split = null!;
    private ListView _devices = null!;
    private ColumnHeader _colDevice = null!;
    private ListView _details = null!;
    private ColumnHeader _colProperty = null!;
    private ColumnHeader _colValue = null!;
    private Panel _buttonBar = null!;
    private FlowLayoutPanel _leftGroup = null!;
    private RoundedButton _openBtn = null!;
    private RoundedButton _refreshBtn = null!;
    private FlowLayoutPanel _rightGroup = null!;
    private RoundedButton _closeBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Cancelled and released in OnFormClosing too, which is where it actually matters -
            // repeated here because a form can be disposed without ever having been closed.
            _detailsCts?.Dispose();
            components?.Dispose();
            _devices?.Dispose();
            _details?.Dispose();
            _split?.Dispose();
            _openBtn?.Dispose();
            _refreshBtn?.Dispose();
            _closeBtn?.Dispose();
            _leftGroup?.Dispose();
            _rightGroup?.Dispose();
            _buttonBar?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Column captions are localized in the constructor - a
    /// <see cref="ColumnHeader"/> is not a <see cref="Control"/> and cannot carry a
    /// LocalizationKey.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _split = new SplitContainer();
        _devices = new ListView();
        _colDevice = new ColumnHeader();
        _details = new ListView();
        _colProperty = new ColumnHeader();
        _colValue = new ColumnHeader();
        _buttonBar = new Panel();
        _leftGroup = new FlowLayoutPanel();
        _openBtn = new RoundedButton();
        _refreshBtn = new RoundedButton();
        _rightGroup = new FlowLayoutPanel();
        _closeBtn = new RoundedButton();
        ((System.ComponentModel.ISupportInitialize)_split).BeginInit();
        _split.Panel1.SuspendLayout();
        _split.Panel2.SuspendLayout();
        _split.SuspendLayout();
        _buttonBar.SuspendLayout();
        _leftGroup.SuspendLayout();
        _rightGroup.SuspendLayout();
        SuspendLayout();
        //
        // _split
        //
        // SplitterDistance is set in the constructor, not here: a SplitContainer is 150px wide
        // until it is parented and docked, and a distance assigned against that default is either
        // clamped or orphaned - the mistake DifferForm had to be repaired for.
        _split.BorderStyle = BorderStyle.None;
        _split.Dock = DockStyle.Fill;
        _split.Name = "_split";
        _split.Orientation = Orientation.Vertical;
        // Panel1MinSize/Panel2MinSize are set in OnLayout, not here: a SplitContainer is 150px
        // wide until it is parented, and a minimum that does not fit inside that default width is
        // rejected outright rather than remembered for later.
        _split.Panel1.Controls.Add(_devices);
        _split.Panel2.Controls.Add(_details);
        _split.SplitterWidth = 4;
        //
        // _devices
        //
        _devices.BorderStyle = BorderStyle.None;
        _devices.Columns.AddRange(new[] { _colDevice });
        _devices.Dock = DockStyle.Fill;
        _devices.FullRowSelect = true;
        _devices.HideSelection = false;
        _devices.MultiSelect = false;
        _devices.Name = "_devices";
        _devices.UseCompatibleStateImageBehavior = false;
        _devices.View = View.Details;
        //
        // _colDevice
        //
        _colDevice.Text = "Device";
        _colDevice.Width = 200;
        //
        // _details
        //
        _details.BorderStyle = BorderStyle.None;
        _details.Columns.AddRange(new[] { _colProperty, _colValue });
        _details.Dock = DockStyle.Fill;
        _details.FullRowSelect = true;
        _details.MultiSelect = false;
        _details.Name = "_details";
        _details.UseCompatibleStateImageBehavior = false;
        _details.View = View.Details;
        //
        // _colProperty
        //
        _colProperty.Text = "Property";
        _colProperty.Width = 150;
        //
        // _colValue
        //
        _colValue.Text = "Value";
        _colValue.Width = 280;
        //
        // _buttonBar
        //
        // Right group added before Left: both are edge-docked and WinForms lays docked children out
        // from the highest Controls index down, so the last-added claims its edge first.
        _buttonBar.Controls.Add(_rightGroup);
        _buttonBar.Controls.Add(_leftGroup);
        _buttonBar.Dock = DockStyle.Bottom;
        _buttonBar.Name = "_buttonBar";
        _buttonBar.Padding = new Padding(16, 10, 16, 10);
        _buttonBar.Size = new Size(720, 56);
        _uiMetadata.SetThemeRole(_buttonBar, ThemeRole.HeaderBackground);
        //
        // _leftGroup
        //
        _leftGroup.AutoSize = true;
        _leftGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _leftGroup.BackColor = Color.Transparent;
        _leftGroup.Controls.Add(_openBtn);
        _leftGroup.Controls.Add(_refreshBtn);
        _leftGroup.Dock = DockStyle.Left;
        _leftGroup.FlowDirection = FlowDirection.LeftToRight;
        _leftGroup.Name = "_leftGroup";
        _leftGroup.WrapContents = false;
        //
        // _openBtn
        //
        _openBtn.AutoSize = true;
        _openBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _openBtn.Margin = new Padding(0, 0, 8, 0);
        _openBtn.MinimumSize = new Size(100, 32);
        _openBtn.Name = "_openBtn";
        _openBtn.Padding = new Padding(20, 0, 20, 0);
        _openBtn.Role = ThemeRole.PrimaryButton;
        _openBtn.Text = "Open in panel";
        _uiMetadata.SetLocalizationKey(_openBtn, "Mtp.OpenInPanel");
        //
        // _refreshBtn
        //
        _refreshBtn.AutoSize = true;
        _refreshBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _refreshBtn.Margin = new Padding(0);
        _refreshBtn.MinimumSize = new Size(100, 32);
        _refreshBtn.Name = "_refreshBtn";
        _refreshBtn.Padding = new Padding(20, 0, 20, 0);
        _refreshBtn.Role = ThemeRole.SecondaryButton;
        _refreshBtn.Text = "Refresh";
        _uiMetadata.SetLocalizationKey(_refreshBtn, "Common.Refresh");
        //
        // _rightGroup
        //
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
        // MtpDevicesForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(720, 420);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_split);
        Controls.Add(_buttonBar);
        MinimumSize = new Size(560, 320);
        Name = "MtpDevicesForm";
        Text = "Devices";
        _uiMetadata.SetLocalizationKey(this, "Mtp.Devices.Title");
        _split.Panel1.ResumeLayout(false);
        _split.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)_split).EndInit();
        _split.ResumeLayout(false);
        _buttonBar.ResumeLayout(false);
        _buttonBar.PerformLayout();
        _leftGroup.ResumeLayout(false);
        _rightGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
