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
        _layout.SuspendLayout();
        _bottomPanel.SuspendLayout();
        _buttonGroup.SuspendLayout();
        SuspendLayout();
        //
        // _promptLabel
        //
        _promptLabel.AutoSize = true;
        _promptLabel.Dock = DockStyle.Fill;
        _promptLabel.Name = "_promptLabel";
        _promptLabel.TabIndex = 0;
        _promptLabel.TextAlign = ContentAlignment.BottomLeft;
        _uiMetadata.SetThemeRole(_promptLabel, ThemeRole.Body);
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        // Margin defaults to WinForms' built-in 3px on every side; zeroed deliberately - a bottom
        // panel that ever lands in a TableLayoutPanel cell would otherwise render 6px short of its
        // stated Height.
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(420, 50);
        _bottomPanel.TabIndex = 1;
        _uiMetadata.SetThemeRole(_bottomPanel, ThemeRole.HeaderBackground);
        //
        // _buttonGroup
        //
        // The buttons MUST stay parented here, not in _bottomPanel. A plain Panel has no layout
        // engine: a button docked straight into it sits at a frozen Location and its Margin is
        // ignored entirely, so a longer localized caption grows the button rightward over its
        // neighbour instead of the pair reflowing. A Visual Studio round-trip once lifted both
        // buttons out into _bottomPanel and left this panel empty (Size 0x34) - if that happens
        // again, this is what to restore.
        _buttonGroup.AutoSize = true;
        _buttonGroup.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        // Transparent is load-bearing: ControlThemer's FlowLayoutPanel case treats a transparent
        // background as "leave this alone", which is what lets the group show the bottom panel's
        // HeaderBackground instead of being repainted with the generic panel default.
        _buttonGroup.BackColor = Color.Transparent;
        // Secondary first, primary last, so the primary button ends up rightmost.
        _buttonGroup.Controls.Add(_cancelBtn);
        _buttonGroup.Controls.Add(_okBtn);
        _buttonGroup.Dock = DockStyle.Right;
        _buttonGroup.FlowDirection = FlowDirection.LeftToRight;
        _buttonGroup.Name = "_buttonGroup";
        _buttonGroup.TabIndex = 0;
        _buttonGroup.WrapContents = false;
        //
        // _cancelBtn
        //
        // AutoSize with a floor, never a fixed Width: a translation longer than the English
        // placeholder grows the button instead of being clipped.
        _cancelBtn.AutoSize = true;
        _cancelBtn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Margin = new Padding(0, 0, 8, 0);
        _cancelBtn.MinimumSize = new Size(100, 32);
        _cancelBtn.Name = "_cancelBtn";
        _cancelBtn.Padding = new Padding(20, 0, 20, 0);
        _cancelBtn.Role = ThemeRole.SecondaryButton;
        _cancelBtn.TabIndex = 0;
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
        _okBtn.TabIndex = 1;
        _okBtn.Text = "OK";
        _uiMetadata.SetLocalizationKey(_okBtn, "Common.OK");
        //
        // _layout
        //
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_promptLabel, 0, 0);
        _layout.Controls.Add(_textBox, 0, 1);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(24, 20, 24, 8);
        _layout.RowCount = 3;
        // 43/30 rather than the original 24/36 - adjusted by hand in the Visual Studio designer.
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _layout.TabIndex = 0;
        //
        // _textBox
        //
        _textBox.BorderStyle = BorderStyle.FixedSingle;
        _textBox.Dock = DockStyle.Fill;
        _textBox.Name = "_textBox";
        _textBox.TabIndex = 1;
        //
        // InputDialogForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(420, 170);
        // Dock=Fill must be added before its Dock=Bottom sibling: WinForms lays docked children out
        // from the last Controls index down to the first, so a Fill added afterwards would be
        // painted over the bottom panel (see WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        Name = "InputDialogForm";
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
