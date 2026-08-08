using CoderCommander.Services;
using CoderCommander.Terminal.Shells;

namespace CoderCommander.WinForms;

/// <summary>
/// Dialog for selecting which shell to run when creating a new terminal tab. Populated from
/// <see cref="ShellCatalog"/>'s autodetected list (cmd, Windows PowerShell, pwsh, Git Bash, one
/// entry per installed WSL distribution) rather than a fixed two-value enum.
/// </summary>
public sealed class SelectShellDialog : ThemedForm
{
    private ThemedComboBox _shellComboBox = null!;
    private Button _okButton = null!;
    private Button _cancelButton = null!;

    /// <summary>Gets the shell selected by the user.</summary>
    public ShellDescriptor SelectedShell { get; private set; }

    /// <param name="availableShells">Shells to present, as discovered by <see cref="ShellCatalog.DiscoverAsync"/>.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="availableShells"/> is empty.</exception>
    public SelectShellDialog(IReadOnlyList<ShellDescriptor> availableShells, string? preferredShellId = null)
    {
        if (availableShells.Count == 0)
            throw new ArgumentException("No shells available", nameof(availableShells));

        InitializeComponents(availableShells, preferredShellId);
        SelectedShell = availableShells[0];
    }

    private void InitializeComponents(IReadOnlyList<ShellDescriptor> availableShells, string? preferredShellId)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        Text = L.GetString("Terminal.SelectType");
        ClientSize = new Size(360, 170);
        BackColor = p.Background;
        ForeColor = p.Foreground;

        // Content area - Padding on a Panel is respected by its Dock-ed children, unlike
        // Margin on the children themselves (only Flow/TableLayoutPanel honor that).
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 14, 16, 8),
            BackColor = p.Background
        };

        // ComboBox (themed) - added first so it docks below the label once both are Top-docked
        _shellComboBox = new ThemedComboBox
        {
            Dock = DockStyle.Top,
            Height = 30,
            AutoSize = false,
            BackColor = p.Background,
            ForeColor = p.Foreground
        };

        var selectedIndex = 0;
        for (var i = 0; i < availableShells.Count; i++)
        {
            var shell = availableShells[i];
            _shellComboBox.AddItem(DisplayNameFor(shell, L));
            if (shell.Id == preferredShellId)
                selectedIndex = i;
        }

        _shellComboBox.SelectedIndex = selectedIndex;
        contentPanel.Controls.Add(_shellComboBox);

        var label = new Label
        {
            Text = L.GetString("Terminal.SelectType"),
            Dock = DockStyle.Top,
            Height = 28,
            AutoSize = false,
            BackColor = p.Background,
            ForeColor = p.Foreground,
            Font = p.GridFont
        };
        contentPanel.Controls.Add(label);

        Controls.Add(contentPanel);

        _okButton = ThemedForm.CreateThemedButton(L.GetString("Common.OK"), accent: true);
        _cancelButton = ThemedForm.CreateThemedButton(L.GetString("Common.Cancel"));
        Controls.Add(CreateBottomPanel(_okButton, _cancelButton));

        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        _okButton.Click += (_, _) =>
        {
            if (_shellComboBox.SelectedIndex >= 0)
                SelectedShell = availableShells[_shellComboBox.SelectedIndex];
            DialogResult = DialogResult.OK;
            Close();
        };

        _cancelButton.DialogResult = DialogResult.Cancel;
    }

    private static string DisplayNameFor(ShellDescriptor shell, LocalizationService l) =>
        shell.DisplayNameArg != null
            ? l.GetString(shell.DisplayNameKey, shell.DisplayNameArg)
            : l.GetString(shell.DisplayNameKey);
}
