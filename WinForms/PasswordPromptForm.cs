using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Prompts for an archive password (audit finding G053) - shown when an unpack selection contains
/// an encrypted entry the archive format can actually decrypt given one (see
/// <see cref="Archives.ArchiveCapabilities.PasswordProtectedRead"/>). The entered text is exposed
/// only via <see cref="Password"/> for the caller to read once and pass straight into
/// <see cref="Archives.IArchiveFormat.OpenRead"/> - it is never written to settings or logged.
///
/// <para>Layout lives in <c>PasswordPromptForm.Designer.cs</c> and is editable in Visual Studio.</para>
/// </summary>
public sealed partial class PasswordPromptForm : ThemedForm
{
    /// <summary>The password entered by the user. Only meaningful when the dialog closed with
    /// <see cref="DialogResult.OK"/>.</summary>
    public string Password => _textBox.Text;

    /// <param name="archiveName">Display name of the archive being unlocked (not a full path -
    /// callers pass <c>VfsPath.GetName(...)</c>, matching every other archive-related dialog's
    /// message formatting).</param>
    public PasswordPromptForm(string archiveName)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        // Not a plain LocalizationKey: this string takes a format argument, which the key-driven
        // path deliberately does not model - it would need the provider to carry per-control
        // arguments too, for the handful of labels that interpolate something.
        _promptLabel.Text = LocalizationService.Current.GetString("Archive.PasswordPrompt", archiveName);

        _textBox.KeyDown += OnTextBoxKeyDown;
        _showCheck.CheckedChanged += (_, _) => _textBox.UseSystemPasswordChar = !_showCheck.Checked;
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); e.Handled = true; }
        if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.Handled = true; }
    }
}
