using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class TerminalKeyBindingsForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private ListView _list = null!;
    private ColumnHeader _colAction = null!;
    private ColumnHeader _colShortcut = null!;
    private Label _hint = null!;
    private FlowLayoutPanel _midPanel = null!;
    private RoundedButton _clearBtn = null!;
    private RoundedButton _resetBtn = null!;
    private Panel _bottomPanel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _okBtn = null!;
    private RoundedButton _cancelBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _list?.Dispose();
            _hint?.Dispose();
            _clearBtn?.Dispose();
            _resetBtn?.Dispose();
            _midPanel?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Rows come from the terminal action table at runtime; column captions
    /// are localized in the constructor since a <see cref="ColumnHeader"/> is not a
    /// <see cref="Control"/> and cannot carry a LocalizationKey.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _list = new ListView();
        _colAction = new ColumnHeader();
        _colShortcut = new ColumnHeader();
        _hint = new Label();
        _midPanel = new FlowLayoutPanel();
        _clearBtn = new RoundedButton();
        _resetBtn = new RoundedButton();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _okBtn = new RoundedButton();
        _cancelBtn = new RoundedButton();
        _midPanel.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _list
        //
        _list.Columns.AddRange(new[] { _colAction, _colShortcut });
        _list.Dock = DockStyle.Fill;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = false;
        _list.Name = "_list";
        _list.UseCompatibleStateImageBehavior = false;
        _list.View = View.Details;
        //
        // _colAction
        //
        _colAction.Text = "Action";
        _colAction.Width = 220;
        //
        // _colShortcut
        //
        _colShortcut.Text = "Shortcut";
        _colShortcut.Width = 160;
        //
        // _hint
        //
        // AutoEllipsis matters here: Dock=Top overrides AutoSize, and without it Russian's longer
        // text was raw-clipped mid-word ("...для от") instead of degrading to "...".
        _hint.AutoEllipsis = true;
        _hint.Dock = DockStyle.Top;
        _hint.Name = "_hint";
        _hint.Size = new Size(440, 24);
        _hint.Text = "Double-click a row to set a new shortcut.";
        _uiMetadata.SetLocalizationKey(_hint, "Settings.Terminal.KeyBindings.Hint");
        _uiMetadata.SetThemeRole(_hint, ThemeRole.Muted);
        //
        // _midPanel
        //
        _midPanel.AutoSize = true;
        _midPanel.Controls.Add(_clearBtn);
        _midPanel.Controls.Add(_resetBtn);
        _midPanel.Dock = DockStyle.Bottom;
        _midPanel.FlowDirection = FlowDirection.LeftToRight;
        _midPanel.Name = "_midPanel";
        _midPanel.Padding = new Padding(0, 6, 0, 6);
        //
        // _clearBtn
        //
        _clearBtn.AutoSize = true;
        _clearBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _clearBtn.MinimumSize = new Size(100, 32);
        _clearBtn.Name = "_clearBtn";
        _clearBtn.Padding = new Padding(20, 0, 20, 0);
        _clearBtn.Role = ThemeRole.SecondaryButton;
        _clearBtn.Text = "Clear shortcut";
        _uiMetadata.SetLocalizationKey(_clearBtn, "Settings.Terminal.KeyBindings.Clear");
        //
        // _resetBtn
        //
        _resetBtn.AutoSize = true;
        _resetBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _resetBtn.MinimumSize = new Size(100, 32);
        _resetBtn.Name = "_resetBtn";
        _resetBtn.Padding = new Padding(20, 0, 20, 0);
        _resetBtn.Role = ThemeRole.SecondaryButton;
        _resetBtn.Text = "Reset all to defaults";
        _uiMetadata.SetLocalizationKey(_resetBtn, "Settings.Terminal.KeyBindings.ResetAll");
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(440, 50);
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        //
        // _buttonGroup
        //
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonGroup.BackColor = Color.Transparent;
        _buttonGroup.Controls.Add(_cancelBtn);
        _buttonGroup.Controls.Add(_okBtn);
        _buttonGroup.Dock = DockStyle.Right;
        _buttonGroup.FlowDirection = FlowDirection.LeftToRight;
        _buttonGroup.Name = "_buttonGroup";
        _buttonGroup.WrapContents = false;
        //
        // _cancelBtn
        //
        _cancelBtn.AutoSize = true;
        _cancelBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Margin = new Padding(0, 0, 8, 0);
        _cancelBtn.MinimumSize = new Size(100, 32);
        _cancelBtn.Name = "_cancelBtn";
        _cancelBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelBtn.Role = ThemeRole.SecondaryButton;
        _cancelBtn.Text = "Cancel";
        _uiMetadata.SetLocalizationKey(_cancelBtn, "Common.Cancel");
        //
        // _okBtn
        //
        _okBtn.AutoSize = true;
        _okBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _okBtn.Margin = new Padding(0);
        _okBtn.MinimumSize = new Size(100, 32);
        _okBtn.Name = "_okBtn";
        _okBtn.Padding = new Padding(20, 0, 20, 0);
        _okBtn.Role = ThemeRole.PrimaryButton;
        _okBtn.Text = "OK";
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.OK");
        //
        // TerminalKeyBindingsForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(440, 420);
        // Fill first, then the Top/Bottom edge-docked siblings - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_list);
        Controls.Add(_hint);
        Controls.Add(_midPanel);
        Controls.Add(_bottomPanel);
        // The form itself sees key presses first, which is how a chord is captured for a row.
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "TerminalKeyBindingsForm";
        Text = "Terminal key bindings";
        _uiMetadata.SetLocalizationKey(this, "Settings.Terminal.KeyBindings");
        _midPanel.ResumeLayout(false);
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
