using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class OperationQueueForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private ListView _listView = null!;
    private ColumnHeader _colType = null!;
    private ColumnHeader _colSource = null!;
    private ColumnHeader _colStatus = null!;
    private Panel _btnPanel = null!;
    private Label _statusLabel = null!;
    private RoundedButton _closeBtn = null!;
    private FlowLayoutPanel _leftGroup = null!;
    private RoundedButton _cancelAllBtn = null!;
    private RoundedButton _clearBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _listView?.Dispose();
            _statusLabel?.Dispose();
            _cancelAllBtn?.Dispose();
            _clearBtn?.Dispose();
            _closeBtn?.Dispose();
            _leftGroup?.Dispose();
            _btnPanel?.Dispose();
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
        _listView = new ListView();
        _colType = new ColumnHeader();
        _colSource = new ColumnHeader();
        _colStatus = new ColumnHeader();
        _btnPanel = new Panel();
        _statusLabel = new Label();
        _closeBtn = new RoundedButton();
        _leftGroup = new FlowLayoutPanel();
        _cancelAllBtn = new RoundedButton();
        _clearBtn = new RoundedButton();
        _btnPanel.SuspendLayout();
        _leftGroup.SuspendLayout();
        SuspendLayout();
        //
        // _listView
        //
        _listView.BorderStyle = BorderStyle.None;
        _listView.Columns.AddRange(new[] { _colType, _colSource, _colStatus });
        _listView.Dock = DockStyle.Fill;
        _listView.FullRowSelect = true;
        _listView.Name = "_listView";
        _listView.UseCompatibleStateImageBehavior = false;
        _listView.View = View.Details;
        //
        // _colType
        //
        _colType.Text = "Type";
        _colType.Width = 80;
        //
        // _colSource
        //
        _colSource.Text = "Source";
        _colSource.Width = 350;
        //
        // _colStatus
        //
        _colStatus.Text = "Status";
        _colStatus.Width = 80;
        //
        // _btnPanel
        //
        // Fill added first so it docks last and takes the remainder - added last it would have
        // claimed the whole panel before the Left/Right children carved out their own space.
        _btnPanel.Controls.Add(_statusLabel);
        _btnPanel.Controls.Add(_closeBtn);
        _btnPanel.Controls.Add(_leftGroup);
        _btnPanel.Dock = DockStyle.Bottom;
        _btnPanel.Name = "_btnPanel";
        _btnPanel.Padding = new Padding(16, 8, 16, 8);
        _btnPanel.Size = new Size(680, 50);
        _uiMetadata.SetThemeRole(_btnPanel, ThemeRole.HeaderBackground);
        //
        // _statusLabel
        //
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Padding = new Padding(12, 0, 0, 0);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetThemeRole(_statusLabel, ThemeRole.Muted);
        //
        // _closeBtn
        //
        _closeBtn.Dock = DockStyle.Right;
        _closeBtn.MinimumSize = new Size(100, 32);
        _closeBtn.Name = "_closeBtn";
        _closeBtn.Padding = new Padding(20, 0, 20, 0);
        _closeBtn.Role = ThemeRole.PrimaryButton;
        _closeBtn.Text = "Close";
        _uiMetadata.SetLocalizationKey(_closeBtn, "OpQueue.Close");
        //
        // _leftGroup
        //
        // A FlowLayoutPanel, not two Dock.Left buttons: same-side docking stacks from the last-added
        // control outward, which rendered these as "Clear CancelAll" instead of "CancelAll Clear" -
        // and Dock.Left ignores Margin entirely, which the flow panel honours.
        _leftGroup.AutoSize = true;
        _leftGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _leftGroup.BackColor = Color.Transparent;
        _leftGroup.Controls.Add(_cancelAllBtn);
        _leftGroup.Controls.Add(_clearBtn);
        _leftGroup.Dock = DockStyle.Left;
        _leftGroup.FlowDirection = FlowDirection.LeftToRight;
        _leftGroup.Name = "_leftGroup";
        _leftGroup.WrapContents = false;
        //
        // _cancelAllBtn
        //
        _cancelAllBtn.AutoSize = true;
        _cancelAllBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelAllBtn.Margin = new Padding(0, 0, 8, 0);
        _cancelAllBtn.MinimumSize = new Size(100, 32);
        _cancelAllBtn.Name = "_cancelAllBtn";
        _cancelAllBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelAllBtn.Role = ThemeRole.SecondaryButton;
        _cancelAllBtn.Text = "Cancel all";
        _uiMetadata.SetLocalizationKey(_cancelAllBtn, "OpQueue.CancelAll");
        //
        // _clearBtn
        //
        _clearBtn.AutoSize = true;
        _clearBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _clearBtn.Margin = new Padding(0);
        _clearBtn.MinimumSize = new Size(100, 32);
        _clearBtn.Name = "_clearBtn";
        _clearBtn.Padding = new Padding(20, 0, 20, 0);
        _clearBtn.Role = ThemeRole.SecondaryButton;
        _clearBtn.Text = "Clear completed";
        _uiMetadata.SetLocalizationKey(_clearBtn, "OpQueue.Clear");
        //
        // OperationQueueForm
        //
        CancelButton = _closeBtn;
        ClientSize = new Size(680, 440);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_listView);
        Controls.Add(_btnPanel);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "OperationQueueForm";
        Text = "Operation queue";
        _uiMetadata.SetLocalizationKey(this, "OpQueue.Title");
        _btnPanel.ResumeLayout(false);
        _leftGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
