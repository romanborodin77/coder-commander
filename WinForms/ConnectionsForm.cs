using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// The list of saved connections, with add/edit/remove.
///
/// Persists straight to <c>AppSettings.Connections</c> on every change rather than batching to an
/// OK button: this dialog has no OK, and a connection the user just created must survive whatever
/// happens next, including a crash. The per-profile editor is the thing with OK/Cancel semantics.
/// </summary>
public sealed partial class ConnectionsForm : ThemedForm
{
    private readonly CredentialStore _credentials;

    /// <summary>Raised after any change is persisted, so the places bar can rebuild.</summary>
    public event EventHandler? ConnectionsChanged;

    public ConnectionsForm(CredentialStore? credentials = null)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _credentials = credentials ?? CredentialStore.Instance;

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colName.Text = L.GetString("Conn.Col.Name");
        _colAddress.Text = L.GetString("Conn.Col.Address");
        _colAuto.Text = L.GetString("Conn.Col.Auto");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _list.DoubleClick += (_, _) => EditSelected();
        _list.SelectedIndexChanged += (_, _) => UpdateButtonState();
        _addBtn.Click += (_, _) => AddConnection();
        _editBtn.Click += (_, _) => EditSelected();
        _removeBtn.Click += (_, _) => RemoveSelected();
        _closeBtn.Click += (_, _) => Close();

        Load += (_, _) => RefreshList();
    }

    private static List<ConnectionProfile> Profiles => SettingsService.Load().Connections;

    private void RefreshList()
    {
        var L = LocalizationService.Current;
        _list.BeginUpdate();
        _list.Items.Clear();

        var profiles = Profiles;
        if (profiles.Count == 0)
        {
            // Distinguish "you haven't added any" from "this build cannot serve any". Without the
            // second message, Add would open a form whose result SettingsService.Validate discards
            // on the next load - the user would fill it in and watch the entry vanish.
            var hasProviders = FileSystem.FileSystemProviderRegistry.Registered.Any();
            _list.Items.Add(new ListViewItem(L.GetString(hasProviders ? "Conn.Empty" : "Conn.NoProviders"))
            {
                ForeColor = ThemeService.Current.DimForeground,
            });
        }
        else
        {
            foreach (var profile in profiles)
            {
                var item = new ListViewItem(profile.DisplayName) { Tag = profile };
                item.SubItems.Add(profile.Url);
                item.SubItems.Add(profile.AutoConnect ? L.GetString("Common.Yes") : L.GetString("Common.No"));
                _list.Items.Add(item);
            }
        }

        _list.EndUpdate();
        UpdateButtonState();
    }

    private void UpdateButtonState()
    {
        var hasSelection = Selected() is not null;
        _editBtn.Enabled = hasSelection;
        _removeBtn.Enabled = hasSelection;
        _addBtn.Enabled = FileSystem.FileSystemProviderRegistry.Registered.Any();
    }

    /// <summary>Selected profile, or <c>null</c> - including when the "no connections" placeholder
    /// row is selected, which carries no Tag.</summary>
    private ConnectionProfile? Selected() =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ConnectionProfile : null;

    private void AddConnection()
    {
        var draft = new ConnectionProfile
        {
            Scheme = FileSystem.FileSystemProviderRegistry.Registered.FirstOrDefault()?.Scheme ?? "",
        };

        using var editor = new ConnectionEditForm(draft, _credentials);
        if (editor.ShowDialog(this) != DialogResult.OK) return;

        SettingsService.MutateConnections(list => list.Add(editor.Result));
        Persist();
    }

    private void EditSelected()
    {
        if (Selected() is not { } profile) return;

        using var editor = new ConnectionEditForm(profile, _credentials);
        if (editor.ShowDialog(this) != DialogResult.OK) return;

        SettingsService.MutateConnections(list =>
        {
            var index = list.FindIndex(c => c.Id == editor.Result.Id);
            if (index < 0)
                list.Add(editor.Result);   // vanished underneath us - keep the edit
            else
                list[index] = editor.Result;
        });
        Persist();
    }

    private void RemoveSelected()
    {
        if (Selected() is not { } profile) return;

        var L = LocalizationService.Current;
        var answer = StyledMessageBox.Show(
            L.GetString("Conn.RemoveConfirm", profile.DisplayName),
            Text, MsgBoxButtons.YesNo, MsgBoxIcon.Question, this);
        if (answer != MsgBoxResult.Yes) return;

        SettingsService.MutateConnections(list => list.RemoveAll(c => c.Id == profile.Id));

        // The stored password must go with the profile. Leaving it would keep a live secret for a
        // connection the user believes they deleted - CredentialStore.RemoveOrphans is the
        // backstop for paths that skip this, not a substitute for it.
        _credentials.Remove(profile.Id);
        Persist();
    }

    /// <summary>Refreshes the UI and notifies listeners after <see cref="SettingsService.MutateConnections"/>
    /// has already persisted the change - unlike the old shape, saving is no longer this method's
    /// job (<see cref="SettingsService.MutateConnections"/> saves under the same lock it mutates
    /// under, closing a race with <see cref="Services.ConnectionManager"/>'s background reads).</summary>
    private void Persist()
    {
        RefreshList();
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }

}
