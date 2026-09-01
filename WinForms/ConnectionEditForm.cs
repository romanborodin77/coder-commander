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
public sealed partial class ConnectionEditForm : ThemedForm
{
    private readonly ConnectionProfile _draft;
    private readonly CredentialStore _credentials;
    private readonly bool _hadStoredPassword;

    /// <summary>The edited profile - valid only after <see cref="Form.ShowDialog()"/> returned
    /// <see cref="DialogResult.OK"/>.</summary>
    public ConnectionProfile Result => _draft;

    public ConnectionEditForm(ConnectionProfile profile, CredentialStore? credentials = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _draft = profile.Clone();
        _credentials = credentials ?? CredentialStore.Instance;
        _hadStoredPassword = _credentials.Has(_draft.Id);

        var L = LocalizationService.Current;

        _nameBox.Text = _draft.Name;
        _urlBox.Text = _draft.Url;
        _userBox.Text = _draft.UserName;
        _fingerprintBox.Text = _draft.AcceptedCertificateThumbprint;
        _savePasswordCheck.Checked = _draft.SavePassword;
        _autoConnectCheck.Checked = _draft.AutoConnect;

        // Says a password exists without revealing anything about it - so the text depends on the
        // credential store, not on a fixed key.
        _passwordHint.Text = _hadStoredPassword ? L.GetString("Conn.PasswordStored") : "";

        PopulateSchemes();

        _okBtn.Click += OnOk;
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    /// <summary>Fills the scheme combo from the provider registry and selects the draft's own
    /// scheme.</summary>
    private void PopulateSchemes()
    {
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

}
