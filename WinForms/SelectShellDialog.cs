using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Dialog for selecting shell type when creating a new terminal tab.
/// Themed to match application appearance.
/// </summary>
public sealed class SelectShellDialog : ThemedForm
{
    private ThemedComboBox _shellComboBox = null!;
    private Button _okButton = null!;
    private Button _cancelButton = null!;
    /// <summary>Gets the shell type selected by the user.</summary>
    public ShellType SelectedShell { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectShellDialog"/> class, populating the
    /// combo box with the available shell types.
    /// </summary>
    /// <param name="availableShells">List of shell types to present in the dialog.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="availableShells"/> is empty.</exception>
    public SelectShellDialog(List<ShellType> availableShells)
    {
        if (!availableShells.Any())
            throw new ArgumentException("No shells available", nameof(availableShells));

        InitializeComponents(availableShells);
        SelectedShell = availableShells.First();
    }

    private void InitializeComponents(List<ShellType> availableShells)
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

        // Try to select default shell from settings
        var settings = SettingsService.Load();
        var defaultShellType = settings.DefaultShellType == "PowerShell"
            ? ShellType.PowerShell
            : ShellType.Cmd;

        int selectedIndex = 0;
        int indexCounter = 0;

        foreach (var shell in availableShells)
        {
            var displayName = $"{shell.GetDisplayName()} ({shell.GetExecutableName()})";
            _shellComboBox.AddItem(displayName);

            if (shell == defaultShellType)
                selectedIndex = indexCounter;

            indexCounter++;
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
            if (_shellComboBox.SelectedItem is string displayText && displayText.Length > 0)
            {
                // Extract shell type from display name
                SelectedShell = availableShells[_shellComboBox.SelectedIndex];
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        _cancelButton.DialogResult = DialogResult.Cancel;
    }
}
