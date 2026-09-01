using CoderCommander.Services;
using CoderCommander.Terminal.Shells;

namespace CoderCommander.WinForms;

/// <summary>
/// Dialog for selecting which shell to run when creating a new terminal tab. Populated from
/// <see cref="ShellCatalog"/>'s autodetected list (cmd, Windows PowerShell, pwsh, Git Bash, one
/// entry per installed WSL distribution) rather than a fixed two-value enum.
///
/// <para>Layout lives in <c>SelectShellDialog.Designer.cs</c> and is editable in Visual Studio.</para>
/// </summary>
public sealed partial class SelectShellDialog : ThemedForm
{
    /// <summary>Gets the shell selected by the user.</summary>
    public ShellDescriptor SelectedShell { get; private set; }

    /// <param name="availableShells">Shells to present, as discovered by <see cref="ShellCatalog.DiscoverAsync"/>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="availableShells"/> is empty.</exception>
    public SelectShellDialog(IReadOnlyList<ShellDescriptor> availableShells, string? preferredShellId = null)
    {
        ArgumentNullException.ThrowIfNull(availableShells);
        if (availableShells.Count == 0)
            throw new ArgumentException("No shells available", nameof(availableShells));

        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        PopulateShells(availableShells, preferredShellId);
        SelectedShell = availableShells[0];

        _okButton.Click += (_, _) =>
        {
            if (_shellComboBox.SelectedIndex >= 0)
                SelectedShell = availableShells[_shellComboBox.SelectedIndex];
            DialogResult = DialogResult.OK;
            Close();
        };
    }

    private void PopulateShells(IReadOnlyList<ShellDescriptor> availableShells, string? preferredShellId)
    {
        var L = LocalizationService.Current;
        var selectedIndex = 0;
        for (var i = 0; i < availableShells.Count; i++)
        {
            var shell = availableShells[i];
            _shellComboBox.AddItem(DisplayNameFor(shell, L));
            if (shell.Id == preferredShellId)
                selectedIndex = i;
        }
        _shellComboBox.SelectedIndex = selectedIndex;
    }

    private static string DisplayNameFor(ShellDescriptor shell, LocalizationService l) =>
        shell.DisplayNameArg != null
            ? l.GetString(shell.DisplayNameKey, shell.DisplayNameArg)
            : l.GetString(shell.DisplayNameKey);
}
