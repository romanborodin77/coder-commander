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
public sealed partial class CustomShellsForm : ThemedForm
{
    public CustomShellsForm()
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colName.Text = L.GetString("Settings.Terminal.CustomShells.Name");
        _colCommand.Text = L.GetString("Settings.Terminal.CustomShells.Command");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _list.DoubleClick += (_, _) => EditSelected();
        _list.SelectedIndexChanged += (_, _) => UpdateButtonState();
        _addBtn.Click += (_, _) => AddShell();
        _editBtn.Click += (_, _) => EditSelected();
        _removeBtn.Click += (_, _) => RemoveSelected();
        _closeBtn.Click += (_, _) => Close();

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
                ForeColor = DesignerSafeThemeService.Current.DimForeground,
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

}
