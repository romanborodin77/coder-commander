using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class PasswordPromptForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Label _promptLabel = null!;
    private TextBox _textBox = null!;
    private ThemedCheckBox _showCheck = null!;
    private Panel _bottomPanel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _okBtn = null!;
    private RoundedButton _cancelBtn = null!;

    /// <summary>Explicit disposal of the control fields (CA2213) - redundant at runtime, since the
    /// form disposes its own control tree, but the analyzer requires it.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _textBox?.Dispose();
            _showCheck?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _promptLabel?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only - no colours, no fonts, no final strings. See
    /// <see cref="UiMetadataProvider"/> for how roles and localization keys reach runtime.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _layout = new TableLayoutPanel();
        _promptLabel = new Label();
        _textBox = new TextBox();
        _showCheck = new ThemedCheckBox();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _okBtn = new RoundedButton();
        _cancelBtn = new RoundedButton();
        _layout.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_promptLabel, 0, 0);
        _layout.Controls.Add(_textBox, 0, 1);
        _layout.Controls.Add(_showCheck, 0, 2);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(24, 20, 24, 8);
        _layout.RowCount = 4;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _promptLabel
        //
        // AutoSize must stay false or the AutoEllipsis above does nothing at all - WinForms
        // only ellipsizes a Label that is not sizing itself to its own text.
        _promptLabel.AutoEllipsis = true;
        _promptLabel.AutoSize = false;
        _promptLabel.Dock = DockStyle.Fill;
        _promptLabel.Name = "_promptLabel";
        _promptLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetThemeRole(_promptLabel, ThemeRole.Body);
        //
        // _textBox
        //
        _textBox.BorderStyle = BorderStyle.FixedSingle;
        _textBox.Dock = DockStyle.Fill;
        _textBox.Name = "_textBox";
        _textBox.UseSystemPasswordChar = true;
        //
        // _showCheck
        //
        _showCheck.Dock = DockStyle.Fill;
        _showCheck.Name = "PasswordShowCheck";
        _showCheck.Text = "Show password";
        _uiMetadata.SetLocalizationKey(_showCheck, "Archive.PasswordShow");
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(420, 50);
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        //
        // _buttonGroup
        //
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        // Transparent is load-bearing: ControlThemer skips a transparent FlowLayoutPanel, which is
        // what lets the bottom panel's HeaderBackground show through here.
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
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.Margin = new Padding(0);
        _okBtn.MinimumSize = new Size(100, 32);
        _okBtn.Name = "_okBtn";
        _okBtn.Padding = new Padding(20, 0, 20, 0);
        _okBtn.Role = ThemeRole.PrimaryButton;
        _okBtn.Text = "OK";
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.OK");
        //
        // PasswordPromptForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(420, 190);
        // Dock=Fill before its Dock=Bottom sibling - WinForms docks from the last Controls index
        // down, so a Fill added afterwards would be painted over the bottom panel.
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "PasswordPromptForm";
        Text = "Archive password";
        _uiMetadata.SetLocalizationKey(this, "Archive.PasswordTitle");
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
