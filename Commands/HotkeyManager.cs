namespace CoderCommander.Commands;

/// <summary>
/// Defines a keyboard shortcut mapped to a command identifier with an optional parameter.
/// </summary>
public sealed class HotkeyDef
{
    /// <summary>The key combination that triggers this hotkey (e.g. <c>Keys.F5</c>, <c>Keys.Control | Keys.A</c>).</summary>
    public Keys Shortcut { get; init; }
    /// <summary>The command identifier from <see cref="CommandIds"/> to execute.</summary>
    public string CommandId { get; init; } = "";
    /// <summary>Optional parameter passed to the command handler.</summary>
    public string? Param { get; init; }
    /// <summary>Human-readable description for display in hotkey configuration UIs.</summary>
    public string Description { get; init; } = "";
}

/// <summary>
/// Manages keyboard shortcut registration and dispatch to <see cref="CommandEngine"/>.
/// </summary>
public sealed class HotkeyManager
{
    private readonly CommandEngine _engine;
    private readonly List<HotkeyDef> _hotkeys = new();

    /// <summary>Initializes a new hotkey manager backed by the given command engine.</summary>
    /// <param name="engine">The <see cref="CommandEngine"/> that will handle dispatched commands.</param>
    public HotkeyManager(CommandEngine engine)
    {
        _engine = engine;
    }

    /// <summary>Returns all registered hotkey definitions.</summary>
    public IReadOnlyList<HotkeyDef> Hotkeys => _hotkeys;

    /// <summary>Registers a hotkey definition.</summary>
    /// <param name="def">The <see cref="HotkeyDef"/> to add.</param>
    public void Register(HotkeyDef def) => _hotkeys.Add(def);

    /// <summary>Registers a hotkey by specifying its key combination, command, and optional parameters.</summary>
    /// <param name="keys">The key combination (e.g. <c>Keys.F5</c>).</param>
    /// <param name="commandId">The command identifier from <see cref="CommandIds"/>.</param>
    /// <param name="param">Optional parameter string for the command.</param>
    /// <param name="description">Human-readable description of the hotkey.</param>
    public void Register(Keys keys, string commandId, string? param = null, string description = "")
        => Register(new HotkeyDef { Shortcut = keys, CommandId = commandId, Param = param, Description = description });

    /// <summary>
    /// Attempts to handle a key press. Returns true if a hotkey matched.
    /// </summary>
    public bool HandleKey(KeyEventArgs e)
    {
        // Match exact key combination (KeyData includes modifiers)
        foreach (var hk in _hotkeys)
        {
            if (hk.Shortcut == e.KeyData)
            {
                if (_engine.Execute(hk.CommandId, hk.Param))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Registers the default hotkeys.
    /// </summary>
    public void RegisterDefaults()
    {
        // F-keys layout
        Register(Keys.F3, CommandIds.View);
        Register(Keys.F4, CommandIds.Edit);
        Register(Keys.F5, CommandIds.Copy);
        Register(Keys.F6, CommandIds.Move);
        Register(Keys.F2, CommandIds.Rename);
        Register(Keys.F7, CommandIds.MakeDir);
        Register(Keys.F8, CommandIds.Delete);
        Register(Keys.Shift | Keys.F8, CommandIds.Wipe);
        Register(Keys.F9, CommandIds.ToggleTerminal);
        Register(Keys.F10, CommandIds.Exit);
        Register(Keys.Alt | Keys.X, CommandIds.Exit);

        // Navigation
        Register(Keys.Back, CommandIds.GoToParent);
        Register(Keys.Control | Keys.Back, CommandIds.GoToParent);
        Register(Keys.Control | Keys.R, CommandIds.Refresh);

        // Selection
        Register(Keys.Control | Keys.A, CommandIds.SelectAll);
        Register(Keys.Control | Keys.D, CommandIds.DeselectAll);
        Register(Keys.Add, CommandIds.InvertSelection); // Num+
        Register(Keys.Control | Keys.Add, CommandIds.SelectGroup);
        Register(Keys.Control | Keys.Subtract, CommandIds.DeselectGroup);

        // Navigation extras
        Register(Keys.Control | Keys.OemBackslash, CommandIds.GoToRoot); // Ctrl+\
        Register(Keys.Control | Keys.Home, CommandIds.GoToHome); // Ctrl+Home
        Register(Keys.Control | Keys.G, CommandIds.ChangeDir); // Ctrl+G

        // Panel
        Register(Keys.Control | Keys.U, CommandIds.SwapPanels);
        Register(Keys.Control | Keys.OemPeriod, CommandIds.ToggleHidden); // Ctrl+.
        Register(Keys.Control | Keys.P, CommandIds.ToggleFlatView); // Ctrl+P placeholder
        Register(Keys.Alt | Keys.Enter, CommandIds.ShowProperties);

        // Batch operations
        Register(Keys.Control | Keys.M, CommandIds.MultiRename);

        // File extras
        Register(Keys.Shift | Keys.F4, CommandIds.EditNew);
        Register(Keys.Alt | Keys.F5, CommandIds.PackFiles);
        Register(Keys.Alt | Keys.F9, CommandIds.UnpackFiles);

        // View
        Register(Keys.Control | Keys.D1, CommandIds.SetTheme, "Dark");
        Register(Keys.Control | Keys.D2, CommandIds.SetTheme, "Light");

        // Terminal tabs
        Register(Keys.Control | Keys.T, CommandIds.CreateTerminalTab);
        Register(Keys.Control | Keys.W, CommandIds.CloseTerminalTab);
        Register(Keys.Control | Keys.Tab, CommandIds.NextTerminalTab);
        Register(Keys.Control | Keys.Shift | Keys.Tab, CommandIds.PreviousTerminalTab);
    }
}
