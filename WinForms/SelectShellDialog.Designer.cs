using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class SelectShellDialog
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private Panel _contentPanel = null!;
    private ThemedComboBox _shellComboBox = null!;
    private Label _label = null!;
    private Panel _bottomPanel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _okButton = null!;
    private RoundedButton _cancelButton = null!;

    /// <summary>Explicit disposal of the control fields (CA2213).</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _shellComboBox?.Dispose();
            _okButton?.Dispose();
            _cancelButton?.Dispose();
            _label?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _contentPanel?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. The combo's items are not here - they come from
    /// <c>ShellCatalog.DiscoverAsync</c> at runtime and differ per machine.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _contentPanel = new Panel();
        _shellComboBox = new ThemedComboBox();
        _label = new Label();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _okButton = new RoundedButton();
        _cancelButton = new RoundedButton();
        _contentPanel.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _contentPanel
        //
        // Padding on a Panel is respected by its docked children, unlike Margin on the children
        // themselves (only Flow/TableLayoutPanel honour that).
        //
        // The combo is added BEFORE the label even though it appears below it: both are Top-docked,
        // and WinForms docks from the highest Controls index down, so the last-added lands topmost.
        _contentPanel.Controls.Add(_shellComboBox);
        _contentPanel.Controls.Add(_label);
        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Name = "_contentPanel";
        _contentPanel.Padding = new Padding(16, 14, 16, 8);
        //
        // _shellComboBox
        //
        _shellComboBox.AutoSize = false;
        _shellComboBox.Dock = DockStyle.Top;
        _shellComboBox.Name = "_shellComboBox";
        _shellComboBox.Size = new Size(328, 30);
        //
        // _label
        //
        _label.AutoSize = false;
        _label.Dock = DockStyle.Top;
        _label.Name = "_label";
        _label.Size = new Size(328, 28);
        _label.Text = "Shell type";
        _uiMetadata.SetLocalizationKey(_label, "Terminal.SelectType");
        _uiMetadata.SetThemeRole(_label, ThemeRole.Body);
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(360, 50);
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        //
        // _buttonGroup
        //
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        // Transparent so ControlThemer leaves it alone and the panel's HeaderBackground shows.
        _buttonGroup.BackColor = Color.Transparent;
        _buttonGroup.Controls.Add(_cancelButton);
        _buttonGroup.Controls.Add(_okButton);
        _buttonGroup.Dock = DockStyle.Right;
        _buttonGroup.FlowDirection = FlowDirection.LeftToRight;
        _buttonGroup.Name = "_buttonGroup";
        _buttonGroup.WrapContents = false;
        //
        // _cancelButton
        //
        _cancelButton.AutoSize = true;
        _cancelButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Margin = new Padding(0, 0, 8, 0);
        _cancelButton.MinimumSize = new Size(100, 32);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.Padding = new Padding(20, 0, 20, 0);
        _cancelButton.Role = ThemeRole.SecondaryButton;
        _cancelButton.Text = "Cancel";
        _uiMetadata.SetLocalizationKey(_cancelButton, "Common.Cancel");
        //
        // _okButton
        //
        _okButton.AutoSize = true;
        _okButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _okButton.Margin = new Padding(0);
        _okButton.MinimumSize = new Size(100, 32);
        _okButton.Name = "_okButton";
        _okButton.Padding = new Padding(20, 0, 20, 0);
        _okButton.Role = ThemeRole.PrimaryButton;
        _okButton.Text = "OK";
        _uiMetadata.SetLocalizationKey(_okButton, "Common.OK");
        //
        // SelectShellDialog
        //
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
        ClientSize = new Size(360, 170);
        // Fill before Bottom - see the docking-order note in DirectoryTreeForm.Designer.cs.
        Controls.Add(_contentPanel);
        Controls.Add(_bottomPanel);
        Name = "SelectShellDialog";
        Text = "Shell type";
        _uiMetadata.SetLocalizationKey(this, "Terminal.SelectType");
        _contentPanel.ResumeLayout(false);
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
