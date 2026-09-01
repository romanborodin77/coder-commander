using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class InputDialogForm
{
    /// <summary>Designer-owned components (the <see cref="UiMetadataProvider"/> below).</summary>
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Label _promptLabel = null!;
    private TextBox _textBox = null!;
    private Panel _bottomPanel = null!;
    private FlowLayoutPanel _buttonGroup = null!;
    private RoundedButton _okBtn = null!;
    private RoundedButton _cancelBtn = null!;

    /// <summary>Explicit disposal of every control field (CA2213). Controls parented into
    /// <see cref="Control.Controls"/> are already disposed by the form itself, so these calls are
    /// redundant at runtime - they are here to satisfy the analyzer, and double-disposing a control
    /// is a no-op.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _textBox?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _promptLabel?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Layout, and only layout. No colours and no fonts: every control here inherits both ambiently
    /// from the form, and <see cref="ControlThemer"/> re-applies the live palette on load and on
    /// every theme switch - so a literal here would be both redundant and wrong the moment the user
    /// switches themes. Where a control needs a specific palette role rather than the default for its
    /// type, that role is set through <see cref="UiMetadataProvider"/> below.
    ///
    /// <para>No literal strings either, beyond placeholders: the two buttons carry a
    /// <c>LocalizationKey</c> and get their real text from <c>lang/*.lng</c> in
    /// <see cref="UiMetadataProvider.ApplyLocalization"/>. The window title and the prompt come from
    /// constructor arguments, already localized by the caller.</para>
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _promptLabel = new Label();
        _bottomPanel = new Panel();
        _buttonGroup = new FlowLayoutPanel();
        _cancelBtn = new RoundedButton();
        _okBtn = new RoundedButton();
        _layout = new TableLayoutPanel();
        _textBox = new TextBox();
        _bottomPanel.SuspendLayout();
        _layout.SuspendLayout();
        SuspendLayout();
        // 
        // _promptLabel
        // 
        _promptLabel.AutoSize = true;
        _promptLabel.Dock = DockStyle.Fill;
        _promptLabel.Location = new Point(27, 20);
        _promptLabel.Name = "_promptLabel";
        _promptLabel.Size = new Size(366, 43);
        _promptLabel.TabIndex = 0;
        _promptLabel.Tag = ThemeRole.Body;
        _promptLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetThemeRole(_promptLabel, ThemeRole.Body);
        // 
        // _bottomPanel
        // 
        _bottomPanel.Controls.Add(_okBtn);
        _bottomPanel.Controls.Add(_cancelBtn);
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Location = new Point(0, 120);
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(420, 50);
        _bottomPanel.TabIndex = 1;
        _bottomPanel.Tag = ThemeRole.HeaderBackground;
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        // 
        // _buttonGroup
        // 
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonGroup.BackColor = Color.Transparent;
        _buttonGroup.Dock = DockStyle.Right;
        _buttonGroup.Location = new Point(404, 8);
        _buttonGroup.Name = "_buttonGroup";
        _buttonGroup.Size = new Size(0, 34);
        _buttonGroup.TabIndex = 0;
        _buttonGroup.WrapContents = false;
        // 
        // _cancelBtn
        // 
        _cancelBtn.AutoSize = true;
        _cancelBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelBtn.BorderColor = Color.Empty;
        _cancelBtn.BorderWidth = 0;
        _cancelBtn.CornerRadius = 4;
        _cancelBtn.Cursor = Cursors.Hand;
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.DrawShadow = false;
        _cancelBtn.FlatStyle = FlatStyle.Flat;
        _cancelBtn.GradientBottomColor = Color.Empty;
        _cancelBtn.GradientTopColor = Color.Empty;
        _cancelBtn.HoverColor = Color.Empty;
        _uiMetadata.SetLocalizationKey(_cancelBtn, "Common.Cancel");
        _cancelBtn.Location = new Point(203, 8);
        _cancelBtn.Margin = new Padding(0, 0, 8, 0);
        _cancelBtn.MinimumSize = new Size(100, 32);
        _cancelBtn.Name = "_cancelBtn";
        _cancelBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelBtn.PressedColor = Color.Empty;
        _cancelBtn.Role = ThemeRole.SecondaryButton;
        _cancelBtn.ShadowBlur = 4;
        _cancelBtn.ShadowColor = Color.FromArgb(48, 0, 0, 0);
        _cancelBtn.ShadowOffset = 2;
        _cancelBtn.Size = new Size(100, 32);
        _cancelBtn.TabIndex = 0;
        _cancelBtn.Text = "Cancel";
        _cancelBtn.UseGradient = true;
        // 
        // _okBtn
        // 
        _okBtn.AutoSize = true;
        _okBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _okBtn.BorderColor = Color.Empty;
        _okBtn.BorderWidth = 0;
        _okBtn.CornerRadius = 4;
        _okBtn.Cursor = Cursors.Hand;
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.DrawShadow = false;
        _okBtn.FlatStyle = FlatStyle.Flat;
        _okBtn.GradientBottomColor = Color.Empty;
        _okBtn.GradientTopColor = Color.Empty;
        _okBtn.HoverColor = Color.Empty;
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.OK");
        _okBtn.Location = new Point(311, 9);
        _okBtn.Margin = new Padding(0);
        _okBtn.MinimumSize = new Size(100, 32);
        _okBtn.Name = "_okBtn";
        _okBtn.Padding = new Padding(20, 0, 20, 0);
        _okBtn.PressedColor = Color.Empty;
        _okBtn.Role = ThemeRole.PrimaryButton;
        _okBtn.ShadowBlur = 4;
        _okBtn.ShadowColor = Color.FromArgb(48, 0, 0, 0);
        _okBtn.ShadowOffset = 2;
        _okBtn.Size = new Size(100, 32);
        _okBtn.TabIndex = 1;
        _okBtn.Text = "OK";
        _okBtn.UseGradient = true;
        // 
        // _layout
        // 
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_promptLabel, 0, 0);
        _layout.Controls.Add(_textBox, 0, 1);
        _layout.Dock = DockStyle.Fill;
        _layout.Location = new Point(0, 0);
        _layout.Name = "_layout";
        _layout.Padding = new Padding(24, 20, 24, 8);
        _layout.RowCount = 3;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _layout.Size = new Size(420, 120);
        _layout.TabIndex = 0;
        // 
        // _textBox
        // 
        _textBox.BorderStyle = BorderStyle.FixedSingle;
        _textBox.Dock = DockStyle.Fill;
        _textBox.Location = new Point(27, 66);
        _textBox.Name = "_textBox";
        _textBox.Size = new Size(366, 23);
        _textBox.TabIndex = 1;
        // 
        // InputDialogForm
        // 
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(420, 170);
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        Name = "InputDialogForm";
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        ResumeLayout(false);
    }
}
