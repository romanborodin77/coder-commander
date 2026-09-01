using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Terminal.Shells;

namespace CoderCommander.WinForms;

/// <summary>
/// Editor for one <see cref="CustomShellDefinition"/>. Works on a copy of the caller's definition
/// (same reasoning as <see cref="ConnectionEditForm"/>: Cancel must leave the original untouched),
/// but unlike <see cref="ConnectionEditForm"/>'s URL field, <see cref="CustomShellDefinition.Command"/>
/// is not required to resolve successfully right now - a portable or not-yet-installed executable is
/// still a valid entry to save, and <see cref="ShellCatalog"/> already skips (with a logged warning)
/// whatever fails to resolve when the shell list is actually built.
///
/// <para>Layout lives in <c>CustomShellEditForm.Designer.cs</c> and is editable in Visual Studio.</para>
/// </summary>
public sealed partial class CustomShellEditForm : ThemedForm
{
    private readonly CustomShellDefinition _draft;

    /// <summary>The edited definition - valid only after <see cref="Form.ShowDialog()"/> returned
    /// <see cref="DialogResult.OK"/>.</summary>
    public CustomShellDefinition Result => _draft;

    public CustomShellEditForm(CustomShellDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _draft = new CustomShellDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Command = definition.Command,
            Arguments = definition.Arguments,
        };

        _nameBox.Text = _draft.Name;
        _commandBox.Text = _draft.Command;
        _argumentsBox.Text = _draft.Arguments;

        _browseBtn.Click += (_, _) => BrowseForCommand();
        _okBtn.Click += OnOk;
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
    }

    private void BrowseForCommand()
    {
        var L = LocalizationService.Current;
        using var dlg = new OpenFileDialog
        {
            Filter = L.GetString("Settings.Terminal.CustomShells.ExeFilter"),
            CheckFileExists = true,
        };
        if (!string.IsNullOrWhiteSpace(_commandBox.Text) && File.Exists(_commandBox.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(_commandBox.Text);

        if (dlg.ShowDialog(this) == DialogResult.OK)
            _commandBox.Text = dlg.FileName;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        var L = LocalizationService.Current;

        var name = _nameBox.Text.Trim();
        var command = _commandBox.Text.Trim();

        if (name.Length == 0)
        {
            Reject(L.GetString("Settings.Terminal.CustomShells.Invalid.Name"), _nameBox);
            return;
        }
        if (command.Length == 0)
        {
            Reject(L.GetString("Settings.Terminal.CustomShells.Invalid.Command"), _commandBox);
            return;
        }

        _draft.Name = name;
        _draft.Command = command;
        _draft.Arguments = _argumentsBox.Text.Trim();

        DialogResult = DialogResult.OK;
        Close();
    }

    private void Reject(string message, Control focus)
    {
        StyledMessageBox.Show(message, Text, MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
        focus.Focus();
    }
}
