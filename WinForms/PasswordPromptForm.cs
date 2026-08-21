using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Prompts for an archive password (audit finding G053) - shown when an unpack selection contains
/// an encrypted entry the archive format can actually decrypt given one (see
/// <see cref="Archives.ArchiveCapabilities.PasswordProtectedRead"/>). The entered text is exposed
/// only via <see cref="Password"/> for the caller to read once and pass straight into
/// <see cref="Archives.IArchiveFormat.OpenRead"/> - it is never written to settings or logged.
/// </summary>
public sealed class PasswordPromptForm : ThemedForm
{
    private readonly TextBox _textBox;
    private readonly ThemedCheckBox _showCheck;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;

    /// <summary>The password entered by the user. Only meaningful when the dialog closed with
    /// <see cref="DialogResult.OK"/>.</summary>
    public string Password => _textBox.Text;

    /// <param name="archiveName">Display name of the archive being unlocked (not a full path -
    /// callers pass <c>VfsPath.GetName(...)</c>, matching every other archive-related dialog's
    /// message formatting).</param>
    public PasswordPromptForm(string archiveName)
    {
        var L = LocalizationService.Current;

        Text = L.GetString("Archive.PasswordTitle");
        ClientSize = new Size(420, 190);
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = ThemeService.Current.Background,
            Padding = new Padding(24, 20, 24, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var promptLabel = UiHelpers.CreateLabel(L.GetString("Archive.PasswordPrompt", archiveName));
        promptLabel.Dock = DockStyle.Fill;
        promptLabel.AutoEllipsis = true;
        promptLabel.TextAlign = ContentAlignment.BottomLeft;

        _textBox = UiHelpers.CreateTextBox();
        _textBox.Dock = DockStyle.Fill;
        _textBox.UseSystemPasswordChar = true;
        _textBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); e.Handled = true; }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.Handled = true; }
        };

        _showCheck = UiHelpers.CreateCheckBox(L.GetString("Archive.PasswordShow"), name: "PasswordShowCheck");
        _showCheck.Dock = DockStyle.Fill;
        _showCheck.CheckedChanged += (_, _) => _textBox.UseSystemPasswordChar = !_showCheck.Checked;

        _okBtn = ThemedForm.CreateThemedButton(L.GetString("Common.OK"), accent: true);
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.Width = 100;

        _cancelBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Cancel"), accent: false);
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Width = 100;

        var bottomPanel = CreateBottomPanel(_okBtn, _cancelBtn);

        layout.Controls.Add(promptLabel, 0, 0);
        layout.Controls.Add(_textBox, 0, 1);
        layout.Controls.Add(_showCheck, 0, 2);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(layout);
        Controls.Add(bottomPanel);

        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _textBox?.Dispose();
            _showCheck?.Dispose();
            _okBtn?.Dispose();
            _cancelBtn?.Dispose();
        }
        base.Dispose(disposing);
    }
}
