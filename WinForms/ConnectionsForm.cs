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
public sealed class ConnectionsForm : ThemedForm
{
    private readonly ListView _list;
    private readonly Button _addBtn;
    private readonly Button _editBtn;
    private readonly Button _removeBtn;
    private readonly CredentialStore _credentials;

    /// <summary>Raised after any change is persisted, so the places bar can rebuild.</summary>
    public event EventHandler? ConnectionsChanged;

    public ConnectionsForm(CredentialStore? credentials = null)
    {
        _credentials = credentials ?? CredentialStore.Instance;

        var L = LocalizationService.Current;
        Text = L.GetString("Conn.Title");
        ClientSize = new Size(660, 400);
        Resizable = true;
        MinimumSize = new Size(480, 300);

        _list = UiHelpers.CreateListView(
            (L.GetString("Conn.Col.Name"), 170),
            (L.GetString("Conn.Col.Address"), 320),
            (L.GetString("Conn.Col.Auto"), 120));
        _list.Dock = DockStyle.Fill;
        _list.DoubleClick += (_, _) => EditSelected();
        _list.SelectedIndexChanged += (_, _) => UpdateButtonState();

        _addBtn = CreateThemedButton(L.GetString("Conn.Add"), accent: true);
        _addBtn.Margin = new Padding(0, 0, 8, 0);
        _addBtn.Click += (_, _) => AddConnection();

        _editBtn = CreateThemedButton(L.GetString("Conn.Edit"));
        _editBtn.Margin = new Padding(0, 0, 8, 0);
        _editBtn.Click += (_, _) => EditSelected();

        _removeBtn = CreateThemedButton(L.GetString("Conn.Remove"));
        _removeBtn.Margin = new Padding(0);
        _removeBtn.Click += (_, _) => RemoveSelected();

        // Margin is ignored on a control docked straight into a Panel, and two Dock.Left siblings
        // stack outward from the last added - both traps this codebase has already been bitten by
        // (see BookmarksForm). A docked FlowLayoutPanel avoids each.
        var leftGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        leftGroup.Controls.Add(_addBtn);
        leftGroup.Controls.Add(_editBtn);
        leftGroup.Controls.Add(_removeBtn);

        var closeBtn = CreateThemedButton(L.GetString("Common.Close"));
        closeBtn.Click += (_, _) => Close();

        // Docking the button directly would stretch it to the panel's inner height (36px here),
        // leaving it visibly taller than the 32px buttons on the left. A right-docked
        // FlowLayoutPanel lets it keep its natural size, which is what ThemedForm.CreateBottomPanel
        // does for the same reason.
        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        rightGroup.Controls.Add(closeBtn);

        var buttonBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(16, 10, 16, 10),
        };
        buttonBar.SetRole(ThemeRole.HeaderBackground);
        buttonBar.Controls.Add(rightGroup);
        buttonBar.Controls.Add(leftGroup);

        // Dock=Fill sibling before any docked sibling (docking order pitfall).
        Controls.Add(_list);
        Controls.Add(buttonBar);

        CancelButton = closeBtn;
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

        var settings = SettingsService.Load();
        settings.Connections.Add(editor.Result);
        Persist(settings);
    }

    private void EditSelected()
    {
        if (Selected() is not { } profile) return;

        using var editor = new ConnectionEditForm(profile, _credentials);
        if (editor.ShowDialog(this) != DialogResult.OK) return;

        var settings = SettingsService.Load();
        var index = settings.Connections.FindIndex(c => c.Id == editor.Result.Id);
        if (index < 0)
            settings.Connections.Add(editor.Result);   // vanished underneath us - keep the edit
        else
            settings.Connections[index] = editor.Result;
        Persist(settings);
    }

    private void RemoveSelected()
    {
        if (Selected() is not { } profile) return;

        var L = LocalizationService.Current;
        var answer = StyledMessageBox.Show(
            L.GetString("Conn.RemoveConfirm", profile.DisplayName),
            Text, MsgBoxButtons.YesNo, MsgBoxIcon.Question, this);
        if (answer != MsgBoxResult.Yes) return;

        var settings = SettingsService.Load();
        settings.Connections.RemoveAll(c => c.Id == profile.Id);

        // The stored password must go with the profile. Leaving it would keep a live secret for a
        // connection the user believes they deleted - CredentialStore.RemoveOrphans is the
        // backstop for paths that skip this, not a substitute for it.
        _credentials.Remove(profile.Id);
        Persist(settings);
    }

    private void Persist(AppSettings settings)
    {
        SettingsService.Save(settings);
        RefreshList();
        ConnectionsChanged?.Invoke(this, EventArgs.Empty);
    }
}
