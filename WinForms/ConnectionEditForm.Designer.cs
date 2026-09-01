using CoderCommander.Services;

namespace CoderCommander.WinForms;

partial class ConnectionEditForm
{
    private System.ComponentModel.IContainer components = null!;

    private UiMetadataProvider _uiMetadata = null!;
    private TableLayoutPanel _layout = null!;
    private Label _nameLabel = null!;
    private TextBox _nameBox = null!;
    private Label _typeLabel = null!;
    private ThemedComboBox _schemeBox = null!;
    private Label _urlLabel = null!;
    private TextBox _urlBox = null!;
    private Label _userLabel = null!;
    private TextBox _userBox = null!;
    private Label _passwordLabel = null!;
    private TextBox _passwordBox = null!;
    private Label _passwordHint = null!;
    private ThemedCheckBox _savePasswordCheck = null!;
    private ThemedCheckBox _autoConnectCheck = null!;
    private Label _fingerprintLabel = null!;
    private TextBox _fingerprintBox = null!;
    private Label _fingerprintHint = null!;
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
            _nameBox?.Dispose();
            _schemeBox?.Dispose();
            _urlBox?.Dispose();
            _userBox?.Dispose();
            _passwordBox?.Dispose();
            _savePasswordCheck?.Dispose();
            _autoConnectCheck?.Dispose();
            _fingerprintBox?.Dispose();
            _nameLabel?.Dispose();
            _typeLabel?.Dispose();
            _urlLabel?.Dispose();
            _userLabel?.Dispose();
            _passwordLabel?.Dispose();
            _passwordHint?.Dispose();
            _fingerprintLabel?.Dispose();
            _fingerprintHint?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
            _buttonGroup?.Dispose();
            _bottomPanel?.Dispose();
            _layout?.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Layout only. Rows came from AddRow/AddTextRow helpers driven by a <c>ref int</c>
    /// cursor; they are written out here because the designer can only round-trip explicit cell
    /// coordinates.</summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _uiMetadata = new UiMetadataProvider(components);
        _layout = new TableLayoutPanel();
        _nameLabel = new Label();
        _nameBox = new TextBox();
        _typeLabel = new Label();
        _schemeBox = new ThemedComboBox();
        _urlLabel = new Label();
        _urlBox = new TextBox();
        _userLabel = new Label();
        _userBox = new TextBox();
        _passwordLabel = new Label();
        _passwordBox = new TextBox();
        _passwordHint = new Label();
        _savePasswordCheck = new ThemedCheckBox();
        _autoConnectCheck = new ThemedCheckBox();
        _fingerprintLabel = new Label();
        _fingerprintBox = new TextBox();
        _fingerprintHint = new Label();
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
        // Rows 5, 6, 7 and 9 place their control in column 1 only. There is deliberately no filler
        // control in column 0: TableLayoutPanel handles an empty cell by itself, and a bare
        // `new Label()` would be an untagged control that ControlThemer resets to a generic default
        // on every theme switch.
        _layout.ColumnCount = 2;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_nameLabel, 0, 0);
        _layout.Controls.Add(_nameBox, 1, 0);
        _layout.Controls.Add(_typeLabel, 0, 1);
        _layout.Controls.Add(_schemeBox, 1, 1);
        _layout.Controls.Add(_urlLabel, 0, 2);
        _layout.Controls.Add(_urlBox, 1, 2);
        _layout.Controls.Add(_userLabel, 0, 3);
        _layout.Controls.Add(_userBox, 1, 3);
        _layout.Controls.Add(_passwordLabel, 0, 4);
        _layout.Controls.Add(_passwordBox, 1, 4);
        _layout.Controls.Add(_passwordHint, 1, 5);
        _layout.Controls.Add(_savePasswordCheck, 1, 6);
        _layout.Controls.Add(_autoConnectCheck, 1, 7);
        _layout.Controls.Add(_fingerprintLabel, 0, 8);
        _layout.Controls.Add(_fingerprintBox, 1, 8);
        _layout.Controls.Add(_fingerprintHint, 1, 9);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(20, 16, 20, 8);
        _layout.RowCount = 11;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _uiMetadata.SetThemeRole(_layout, ThemeRole.Background);
        //
        // _nameLabel
        //
        _nameLabel.AutoSize = true;
        _nameLabel.Dock = DockStyle.Fill;
        _nameLabel.Name = "_nameLabel";
        _nameLabel.Text = "Name";
        _nameLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_nameLabel, "Conn.Field.Name");
        _uiMetadata.SetThemeRole(_nameLabel, ThemeRole.Body);
        //
        // _nameBox
        //
        _nameBox.BorderStyle = BorderStyle.FixedSingle;
        _nameBox.Dock = DockStyle.Fill;
        _nameBox.Name = "_nameBox";
        //
        // _typeLabel
        //
        _typeLabel.AutoSize = true;
        _typeLabel.Dock = DockStyle.Fill;
        _typeLabel.Name = "_typeLabel";
        _typeLabel.Text = "Type";
        _typeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_typeLabel, "Conn.Field.Type");
        _uiMetadata.SetThemeRole(_typeLabel, ThemeRole.Body);
        //
        // _schemeBox
        //
        _schemeBox.Dock = DockStyle.Fill;
        _schemeBox.Name = "_schemeBox";
        //
        // _urlLabel
        //
        _urlLabel.AutoSize = true;
        _urlLabel.Dock = DockStyle.Fill;
        _urlLabel.Name = "_urlLabel";
        _urlLabel.Text = "URL";
        _urlLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_urlLabel, "Conn.Field.Url");
        _uiMetadata.SetThemeRole(_urlLabel, ThemeRole.Body);
        //
        // _urlBox
        //
        _urlBox.BorderStyle = BorderStyle.FixedSingle;
        _urlBox.Dock = DockStyle.Fill;
        _urlBox.Name = "_urlBox";
        //
        // _userLabel
        //
        _userLabel.AutoSize = true;
        _userLabel.Dock = DockStyle.Fill;
        _userLabel.Name = "_userLabel";
        _userLabel.Text = "User name";
        _userLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_userLabel, "Conn.Field.User");
        _uiMetadata.SetThemeRole(_userLabel, ThemeRole.Body);
        //
        // _userBox
        //
        _userBox.BorderStyle = BorderStyle.FixedSingle;
        _userBox.Dock = DockStyle.Fill;
        _userBox.Name = "_userBox";
        //
        // _passwordLabel
        //
        _passwordLabel.AutoSize = true;
        _passwordLabel.Dock = DockStyle.Fill;
        _passwordLabel.Name = "_passwordLabel";
        _passwordLabel.Text = "Password";
        _passwordLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_passwordLabel, "Conn.Field.Password");
        _uiMetadata.SetThemeRole(_passwordLabel, ThemeRole.Body);
        //
        // _passwordBox
        //
        _passwordBox.BorderStyle = BorderStyle.FixedSingle;
        _passwordBox.Dock = DockStyle.Fill;
        _passwordBox.Name = "_passwordBox";
        _passwordBox.UseSystemPasswordChar = true;
        //
        // _passwordHint
        //
        // Text is set in the constructor: it says a password exists without revealing anything
        // about it, so it depends on whether the credential store has one for this profile.
        _passwordHint.AutoSize = true;
        _passwordHint.Dock = DockStyle.Fill;
        _passwordHint.Name = "_passwordHint";
        _uiMetadata.SetThemeRole(_passwordHint, ThemeRole.Hint);
        //
        // _savePasswordCheck
        //
        _savePasswordCheck.Dock = DockStyle.Fill;
        _savePasswordCheck.Name = "_savePasswordCheck";
        _savePasswordCheck.Text = "Save password";
        _uiMetadata.SetLocalizationKey(_savePasswordCheck, "Conn.Field.SavePassword");
        //
        // _autoConnectCheck
        //
        _autoConnectCheck.Dock = DockStyle.Fill;
        _autoConnectCheck.Name = "_autoConnectCheck";
        _autoConnectCheck.Text = "Connect on startup";
        _uiMetadata.SetLocalizationKey(_autoConnectCheck, "Conn.Field.AutoConnect");
        //
        // _fingerprintLabel
        //
        // The field that makes an untrusted server's identity acceptable - a TLS certificate's
        // SHA-256 thumbprint, or an SSH host key's SHA-256 fingerprint. Without it the profile's
        // AcceptedCertificateThumbprint was unreachable: the trust policies read it and nothing
        // could set it, so a self-signed server simply could not be connected to.
        _fingerprintLabel.AutoSize = true;
        _fingerprintLabel.Dock = DockStyle.Fill;
        _fingerprintLabel.Name = "_fingerprintLabel";
        _fingerprintLabel.Text = "Accepted fingerprint";
        _fingerprintLabel.TextAlign = ContentAlignment.MiddleLeft;
        _uiMetadata.SetLocalizationKey(_fingerprintLabel, "Conn.Field.Fingerprint");
        _uiMetadata.SetThemeRole(_fingerprintLabel, ThemeRole.Body);
        //
        // _fingerprintBox
        //
        _fingerprintBox.BorderStyle = BorderStyle.FixedSingle;
        _fingerprintBox.Dock = DockStyle.Fill;
        _fingerprintBox.Name = "_fingerprintBox";
        //
        // _fingerprintHint
        //
        // AutoEllipsis, and therefore AutoSize=false: at AutoSize=true this label word-wrapped
        // inside a row only one line tall, so everything past the wrap point was not truncated
        // but silently dropped - no ellipsis, no clue anything was missing. Same treatment
        // HotkeyBindingsForm._hint and TerminalKeyBindingsForm._hint already use.
        _fingerprintHint.AutoEllipsis = true;
        _fingerprintHint.AutoSize = false;
        _fingerprintHint.Dock = DockStyle.Fill;
        _fingerprintHint.Name = "_fingerprintHint";
        _fingerprintHint.Text = "Leave empty to require a trusted chain.";
        _uiMetadata.SetLocalizationKey(_fingerprintHint, "Conn.FingerprintHint");
        _uiMetadata.SetThemeRole(_fingerprintHint, ThemeRole.Hint);
        //
        // _bottomPanel
        //
        _bottomPanel.Controls.Add(_buttonGroup);
        _bottomPanel.Dock = DockStyle.Bottom;
        _bottomPanel.Margin = new Padding(0);
        _bottomPanel.Name = "_bottomPanel";
        _bottomPanel.Padding = new Padding(16, 8, 16, 8);
        _bottomPanel.Size = new Size(560, 50);
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
        // ConnectionEditForm
        //
        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
        ClientSize = new Size(560, 400);
        // Fill before Bottom - see DirectoryTreeForm.Designer.cs.
        Controls.Add(_layout);
        Controls.Add(_bottomPanel);
        Name = "ConnectionEditForm";
        Text = "Edit connection";
        _uiMetadata.SetLocalizationKey(this, "Conn.Edit.Title");
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _bottomPanel.ResumeLayout(false);
        _bottomPanel.PerformLayout();
        _buttonGroup.ResumeLayout(false);
        ResumeLayout(false);
    }
}
