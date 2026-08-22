using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Terminal.Shells;

namespace CoderCommander.WinForms;

/// <summary>
/// The list of user-defined terminal shells (Settings ▸ Terminal ▸ Custom Shells), with
/// add/edit/remove - same shape as <see cref="ConnectionsForm"/>: persists straight to
/// <c>AppSettings.TerminalCustomShells</c> on every change rather than batching to an OK button,
/// and invalidates <see cref="ShellCatalog"/>'s cache after each change so a newly added/edited/
/// removed shell shows up the next time a terminal tab picker or this dialog's own "Default Shell"
/// combo (in <see cref="SettingsForm"/>) is populated.
/// </summary>
public sealed class CustomShellsForm : ThemedForm
{
    private readonly ListView _list;
    private readonly Button _addBtn;
    private readonly Button _editBtn;
    private readonly Button _removeBtn;

    public CustomShellsForm()
    {
        var L = LocalizationService.Current;
        Text = L.GetString("Settings.Terminal.CustomShells.Title");
        ClientSize = new Size(560, 360);
        Resizable = true;
        MinimumSize = new Size(420, 280);

        _list = UiHelpers.CreateListView(
            (L.GetString("Settings.Terminal.CustomShells.Name"), 160),
            (L.GetString("Settings.Terminal.CustomShells.Command"), 280));
        _list.Dock = DockStyle.Fill;
        _list.DoubleClick += (_, _) => EditSelected();
        _list.SelectedIndexChanged += (_, _) => UpdateButtonState();

        _addBtn = CreateThemedButton(L.GetString("Conn.Add"), accent: true);
        _addBtn.Margin = new Padding(0, 0, 8, 0);
        _addBtn.Click += (_, _) => AddShell();

        _editBtn = CreateThemedButton(L.GetString("Conn.Edit"));
        _editBtn.Margin = new Padding(0, 0, 8, 0);
        _editBtn.Click += (_, _) => EditSelected();

        _removeBtn = CreateThemedButton(L.GetString("Conn.Remove"));
        _removeBtn.Margin = new Padding(0);
        _removeBtn.Click += (_, _) => RemoveSelected();

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

        Controls.Add(_list);
        Controls.Add(buttonBar);

        CancelButton = closeBtn;
        Load += (_, _) => RefreshList();
    }

    private static List<CustomShellDefinition> Shells => SettingsService.Load().TerminalCustomShells;

    private void RefreshList()
    {
        var L = LocalizationService.Current;
        _list.BeginUpdate();
        _list.Items.Clear();

        var shells = Shells;
        if (shells.Count == 0)
        {
            _list.Items.Add(new ListViewItem(L.GetString("Settings.Terminal.CustomShells.Empty"))
            {
                ForeColor = ThemeService.Current.DimForeground,
            });
        }
        else
        {
            foreach (var shell in shells)
            {
                var item = new ListViewItem(shell.Name) { Tag = shell };
                item.SubItems.Add(shell.Command);
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
    }

    private CustomShellDefinition? Selected() =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as CustomShellDefinition : null;

    private void AddShell()
    {
        using var editor = new CustomShellEditForm(new CustomShellDefinition());
        if (editor.ShowDialog(this) != DialogResult.OK) return;

        SettingsService.MutateCustomShells(list => list.Add(editor.Result));
        Persist();
    }

    private void EditSelected()
    {
        if (Selected() is not { } shell) return;

        using var editor = new CustomShellEditForm(shell);
        if (editor.ShowDialog(this) != DialogResult.OK) return;

        SettingsService.MutateCustomShells(list =>
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
        if (Selected() is not { } shell) return;

        var L = LocalizationService.Current;
        var answer = StyledMessageBox.Show(
            L.GetString("Settings.Terminal.CustomShells.RemoveConfirm", shell.Name),
            Text, MsgBoxButtons.YesNo, MsgBoxIcon.Question, this);
        if (answer != MsgBoxResult.Yes) return;

        SettingsService.MutateCustomShells(list => list.RemoveAll(c => c.Id == shell.Id));
        Persist();
    }

    private void Persist()
    {
        ShellCatalog.InvalidateCache();
        RefreshList();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _list?.Dispose();
            _addBtn?.Dispose();
            _editBtn?.Dispose();
            _removeBtn?.Dispose();
        }
        base.Dispose(disposing);
    }
}
