using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Editor for one <see cref="ConnectionProfile"/>.
///
/// Works on a <see cref="ConnectionProfile.Clone"/> of the caller's profile, so pressing Cancel
/// leaves the original untouched without the caller having to re-read it from disk.
///
/// The password is handled apart from every other field: it is never read back out of the store to
/// pre-fill the box (there is no reason to put a plaintext secret on screen), an empty box means
/// "leave whatever is stored alone" rather than "clear it", and it is written only when
/// <see cref="ConnectionProfile.SavePassword"/> is on.
/// </summary>
public sealed class ConnectionEditForm : ThemedForm
{
    private readonly ConnectionProfile _draft;
    private readonly CredentialStore _credentials;
    private readonly bool _hadStoredPassword;

    private readonly TextBox _nameBox;
    private readonly ThemedComboBox _schemeBox;
    private readonly TextBox _urlBox;
    private readonly TextBox _userBox;
    private readonly TextBox _passwordBox;
    private readonly ThemedCheckBox _savePasswordCheck;
    private readonly ThemedCheckBox _autoConnectCheck;
    private readonly TextBox _fingerprintBox;

    /// <summary>The edited profile - valid only after <see cref="Form.ShowDialog()"/> returned
    /// <see cref="DialogResult.OK"/>.</summary>
    public ConnectionProfile Result => _draft;

    public ConnectionEditForm(ConnectionProfile profile, CredentialStore? credentials = null)
    {
        _draft = profile.Clone();
        _credentials = credentials ?? CredentialStore.Instance;
        _hadStoredPassword = _credentials.Has(_draft.Id);

        var L = LocalizationService.Current;
        Text = L.GetString("Conn.Edit.Title");
        ClientSize = new Size(560, 400);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(20, 16, 20, 8),
        };
        layout.SetRole(ThemeRole.Background);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var row = 0;
        _nameBox = AddTextRow(layout, ref row, L.GetString("Conn.Field.Name"), _draft.Name);

        _schemeBox = new ThemedComboBox { Dock = DockStyle.Fill };
        // Only registered providers are offered - a profile whose scheme nothing can serve is a
        // button that fails on click, and SettingsService.Validate would drop it on next load.
        _schemeBox.AddItems(FileSystemProviderRegistry.Registered.Select(p => p.Scheme));
        if (_schemeBox.Items.Count == 0 && _draft.Scheme.Length > 0)
            _schemeBox.AddItem(_draft.Scheme);

        // An existing profile keeps its scheme even if that provider is no longer registered -
        // silently rewriting it to the first available one would repoint the connection at a
        // different server without saying so.
        var schemeIndex = _schemeBox.Items.ToList().FindIndex(
            s => string.Equals(s, _draft.Scheme, StringComparison.OrdinalIgnoreCase));
        if (schemeIndex < 0 && _draft.Scheme.Length > 0)
        {
            _schemeBox.AddItem(_draft.Scheme);
            schemeIndex = _schemeBox.Items.Count - 1;
        }
        if (_schemeBox.Items.Count > 0)
            _schemeBox.SelectedIndex = Math.Max(0, schemeIndex);
        AddRow(layout, ref row, L.GetString("Conn.Field.Type"), _schemeBox);

        _urlBox = AddTextRow(layout, ref row, L.GetString("Conn.Field.Url"), _draft.Url);
        _userBox = AddTextRow(layout, ref row, L.GetString("Conn.Field.User"), _draft.UserName);

        _passwordBox = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
        AddRow(layout, ref row, L.GetString("Conn.Field.Password"), _passwordBox);

        // Says a password exists without revealing anything about it.
        var passwordHint = UiHelpers.CreateLabel(_hadStoredPassword ? L.GetString("Conn.PasswordStored") : "");
        passwordHint.Dock = DockStyle.Fill;
        passwordHint.SetRole(ThemeRole.Hint);
        // No filler control in column 0: TableLayoutPanel handles an empty cell by itself, and a
        // bare `new Label()` would be an untagged control that ControlThemer resets to a generic
        // default on every theme switch.
        layout.Controls.Add(passwordHint, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        row++;

        _savePasswordCheck = UiHelpers.CreateCheckBox(L.GetString("Conn.Field.SavePassword"), _draft.SavePassword);
        _savePasswordCheck.Dock = DockStyle.Fill;
        layout.Controls.Add(_savePasswordCheck, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        row++;

        _autoConnectCheck = UiHelpers.CreateCheckBox(L.GetString("Conn.Field.AutoConnect"), _draft.AutoConnect);
        _autoConnectCheck.Dock = DockStyle.Fill;
        layout.Controls.Add(_autoConnectCheck, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        row++;

        // The field that makes an untrusted server's identity acceptable - a TLS certificate's
        // SHA-256 thumbprint, or an SSH host key's SHA-256 fingerprint. Without it here the
        // profile's AcceptedCertificateThumbprint was unreachable: the trust policies read it, and
        // nothing could ever set it, so a self-signed server simply could not be connected to.
        _fingerprintBox = AddTextRow(layout, ref row, L.GetString("Conn.Field.Fingerprint"),
            _draft.AcceptedCertificateThumbprint);

        var fingerprintHint = UiHelpers.CreateLabel(L.GetString("Conn.FingerprintHint"));
        fingerprintHint.Dock = DockStyle.Fill;
        fingerprintHint.SetRole(ThemeRole.Hint);
        layout.Controls.Add(fingerprintHint, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        row++;

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var okBtn = CreateThemedButton(L.GetString("Common.OK"), accent: true);
        okBtn.Click += OnOk;
        var cancelBtn = CreateThemedButton(L.GetString("Common.Cancel"));
        cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        // Dock=Fill sibling first, then the docked bottom bar (docking order pitfall).
        Controls.Add(layout);
        Controls.Add(CreateBottomPanel(okBtn, cancelBtn));

        AcceptButton = okBtn;
        CancelButton = cancelBtn;
    }

    private static TextBox AddTextRow(TableLayoutPanel layout, ref int row, string caption, string value)
    {
        var box = UiHelpers.CreateTextBox();
        box.Dock = DockStyle.Fill;
        box.Text = value;
        AddRow(layout, ref row, caption, box);
        return box;
    }

    private static void AddRow(TableLayoutPanel layout, ref int row, string caption, Control control)
    {
        var label = UiHelpers.CreateLabel(caption);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        row++;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var L = LocalizationService.Current;

        var name = _nameBox.Text.Trim();
        var url = _urlBox.Text.Trim();

        if (name.Length == 0)
        {
            Reject(L.GetString("Conn.Invalid.Name"), _nameBox);
            return;
        }
        // Absolute, not merely non-empty. "example.com/dav" looks like an address and is not one:
        // every provider parses it with Uri.TryCreate(..., UriKind.Absolute) and would refuse it at
        // connect time, by which point the dialog is closed and the message has lost its field.
        if (url.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            Reject(L.GetString("Conn.Invalid.Url"), _urlBox);
            return;
        }

        // Reject credentials embedded in the address itself (e.g. "ftp://user:pass@host" or
        // "https://user:secret@dav.example.com/..."). ConnectionProfile.Url is serialised into
        // settings.json in the clear - its own doc comment says a secret must never end up there,
        // no matter how the file is later copied, backed up, or attached to a bug report. The
        // User/Password fields below route through CredentialStore's DPAPI-encrypted storage
        // instead; letting a pasted URL silently bypass that would defeat the whole design.
        if (uri.UserInfo.Length > 0)
        {
            Reject(L.GetString("Conn.Invalid.UserInfoInUrl"), _urlBox);
            return;
        }

        // The URL scheme must match the selected provider scheme. A mismatch (e.g. "dav" selected
        // with an "ftp://" URL) would save a profile that fails at connect time with a confusing
        // provider-side error.
        var selectedScheme = _schemeBox.SelectedItem?.ToString() ?? _draft.Scheme;
        var expectedSchemes = selectedScheme switch
        {
            "dav" => new[] { "HTTP", "HTTPS" },
            "smb" => new[] { "FILE" },
            "ftp" or "ftps" => new[] { "FTP" },
            "sftp" => new[] { "SFTP", "SSH" },
            _ => new[] { selectedScheme }
        };
        if (Array.IndexOf(expectedSchemes, uri.Scheme.ToUpperInvariant()) < 0)
        {
            Reject(L.GetString("Conn.Invalid.SchemeMismatch"), _urlBox);
            return;
        }

        var user = _userBox.Text.Trim();
        var savePassword = _savePasswordCheck.Checked;
        var willHavePassword = savePassword && (_passwordBox.Text.Length > 0 || _hadStoredPassword);

        // Mirrors the repair SettingsService.Validate performs, but as a refusal rather than a
        // silent correction: telling the user now is better than quietly unticking a box they
        // deliberately ticked and letting them discover it later.
        if (_autoConnectCheck.Checked && user.Length > 0 && !willHavePassword)
        {
            Reject(L.GetString("Conn.AutoConnectNeedsPassword"), _passwordBox);
            return;
        }

        _draft.Name = name;
        _draft.Scheme = _schemeBox.SelectedItem?.ToString() ?? _draft.Scheme;
        _draft.Url = url;
        _draft.UserName = user;
        _draft.SavePassword = savePassword;
        _draft.AutoConnect = _autoConnectCheck.Checked;
        _draft.AcceptedCertificateThumbprint = _fingerprintBox.Text.Trim();

        if (!savePassword)
        {
            // Unticking "save password" must actually delete the stored secret, not merely stop
            // updating it - otherwise the box says the password isn't saved while it still is.
            _credentials.Remove(_draft.Id);
        }
        else if (_passwordBox.Text.Length > 0)
        {
            // An empty box with the option on means "keep what is stored", which is why the box is
            // never pre-filled from the store: a blank field can then mean exactly one thing.
            _credentials.TrySet(_draft.Id, _passwordBox.Text);
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Reject(string message, Control focus)
    {
        StyledMessageBox.Show(message, Text, MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
        focus.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameBox?.Dispose();
            _schemeBox?.Dispose();
            _urlBox?.Dispose();
            _userBox?.Dispose();
            _passwordBox?.Dispose();
            _fingerprintBox?.Dispose();
            _savePasswordCheck?.Dispose();
            _autoConnectCheck?.Dispose();
        }
        base.Dispose(disposing);
    }
}
