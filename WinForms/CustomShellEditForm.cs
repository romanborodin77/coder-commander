using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Terminal.Shells;

namespace CoderCommander.WinForms;

/// <summary>
/// Editor for one <see cref="CustomShellDefinition"/>. Works on a copy of the caller's definition
/// (same reasoning as <see cref="ConnectionEditForm"/>: Cancel must leave the original untouched),
/// but unlike <see cref="ConnectionEditForm"/>'s URL field, <see cref="Command"/> is not required
/// to resolve successfully right now - a portable or not-yet-installed executable is still a valid
/// entry to save, and <see cref="ShellCatalog"/> already skips (with a logged warning) whatever
/// fails to resolve when the shell list is actually built.
/// </summary>
public sealed class CustomShellEditForm : ThemedForm
{
    private readonly CustomShellDefinition _draft;

    private readonly TextBox _nameBox;
    private readonly TextBox _commandBox;
    private readonly TextBox _argumentsBox;

    /// <summary>The edited definition - valid only after <see cref="Form.ShowDialog()"/> returned
    /// <see cref="DialogResult.OK"/>.</summary>
    public CustomShellDefinition Result => _draft;

    public CustomShellEditForm(CustomShellDefinition definition)
    {
        _draft = new CustomShellDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Command = definition.Command,
            Arguments = definition.Arguments,
        };

        var L = LocalizationService.Current;
        Text = L.GetString("Settings.Terminal.CustomShells.EditTitle");
        ClientSize = new Size(520, 220);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(20, 16, 20, 8),
        };
        layout.SetRole(ThemeRole.Background);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var row = 0;
        _nameBox = AddTextRow(layout, ref row, L.GetString("Settings.Terminal.CustomShells.Name"), _draft.Name);

        var commandRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
        };
        commandRow.SetRole(ThemeRole.Background);
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        _commandBox = UiHelpers.CreateTextBox();
        _commandBox.Dock = DockStyle.Fill;
        _commandBox.Text = _draft.Command;
        var browseBtn = CreateThemedButton(L.GetString("Common.Browse"));
        browseBtn.Margin = new Padding(6, 0, 0, 0);
        browseBtn.Click += (_, _) => BrowseForCommand();
        commandRow.Controls.Add(_commandBox, 0, 0);
        commandRow.Controls.Add(browseBtn, 1, 0);
        AddRow(layout, ref row, L.GetString("Settings.Terminal.CustomShells.Command"), commandRow);

        var commandHint = UiHelpers.CreateLabel(L.GetString("Settings.Terminal.CustomShells.CommandHint"));
        commandHint.Dock = DockStyle.Fill;
        commandHint.SetRole(ThemeRole.Hint);
        layout.Controls.Add(commandHint, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        row++;

        _argumentsBox = AddTextRow(layout, ref row, L.GetString("Settings.Terminal.CustomShells.Arguments"), _draft.Arguments);

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var okBtn = CreateThemedButton(L.GetString("Common.OK"), accent: true);
        okBtn.Click += OnOk;
        var cancelBtn = CreateThemedButton(L.GetString("Common.Cancel"));
        cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameBox?.Dispose();
            _commandBox?.Dispose();
            _argumentsBox?.Dispose();
        }
        base.Dispose(disposing);
    }
}
