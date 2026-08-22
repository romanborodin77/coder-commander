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

    /// <summary>Stable identity for this specific binding, independent of its current
    /// <see cref="Shortcut"/> - what <see cref="AppSettings.CustomHotkeys"/> keys an override by,
    /// and how <see cref="HotkeyManager.ApplyOverrides"/> finds the right entry to rebind.
    /// Auto-derived by <see cref="HotkeyManager.Register(Keys,string,string?,string)"/> as
    /// <c>"{CommandId}[:{Param}]#{N}"</c>, where N counts repeated registrations of the same
    /// command+param (several commands, e.g. GoToParent, legitimately have two default shortcuts -
    /// this is what tells those two rows apart without depending on either one's own key value,
    /// which is exactly the thing a rebind changes).</summary>
    public string Id { get; init; } = "";
}

/// <summary>
/// Manages keyboard shortcut registration and dispatch to <see cref="CommandEngine"/>.
/// </summary>
public sealed class HotkeyManager
{
    private readonly CommandEngine _engine;
    private readonly List<HotkeyDef> _hotkeys = new();
    private readonly Dictionary<string, int> _idOccurrences = new(StringComparer.Ordinal);

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
    {
        var baseKey = param is null ? commandId : commandId + ":" + param;
        _idOccurrences.TryGetValue(baseKey, out var count);
        count++;
        _idOccurrences[baseKey] = count;
        var id = baseKey + "#" + count;
        Register(new HotkeyDef { Shortcut = keys, CommandId = commandId, Param = param, Description = description, Id = id });
    }

    /// <summary>Applies user-chosen overrides (<see cref="AppSettings.CustomHotkeys"/>: binding
    /// <see cref="HotkeyDef.Id"/> → chord string in <c>TerminalKeyBindings.FormatChord</c>'s
    /// format, or <c>""</c> for "explicitly unbound") on top of whatever <see cref="RegisterDefaults"/>
    /// already registered. An id with no entry in <paramref name="overrides"/> keeps its built-in
    /// default untouched - this is a partial-override model (only what the user actually changed
    /// is stored), not a full replacement table like <c>TerminalCustomKeyBindings</c>'s "Custom"
    /// preset, since forcing the user to configure all ~30 app hotkeys just to change one would be
    /// a much worse editing experience for a list this size. An unparseable chord string (hand-
    /// edited settings.json) is treated the same as "no override" - the default wins rather than
    /// silently dropping the binding.</summary>
    public void ApplyOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0) return;

        for (var i = _hotkeys.Count - 1; i >= 0; i--)
        {
            var def = _hotkeys[i];
            if (!overrides.TryGetValue(def.Id, out var chordText)) continue;

            if (chordText.Length == 0)
            {
                _hotkeys.RemoveAt(i);
                continue;
            }

            if (Terminal.Input.TerminalKeyBindings.TryParseChord(chordText, out var chord))
                _hotkeys[i] = new HotkeyDef { Shortcut = chord, CommandId = def.CommandId, Param = def.Param, Description = def.Description, Id = def.Id };
        }
    }

    /// <summary>
    /// Attempts to handle a key press. Returns true if a hotkey matched.
    /// </summary>
    public bool HandleKey(KeyEventArgs e)
    {
        // Match exact key combination (KeyData includes modifiers)
        // Snapshot before iteration — a command handler could trigger Reload (which clears
        // _hotkeys), and foreach over a modified List throws InvalidOperationException.
        foreach (var hk in _hotkeys.ToArray())
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

    /// <summary>Rebuilds the hotkey table from scratch: clears everything registered so far, calls
    /// <see cref="RegisterDefaults"/>, then applies <paramref name="overrides"/>. Unlike calling
    /// <see cref="ApplyOverrides"/> alone, this correctly reverts an id that <em>used</em> to have
    /// an override but no longer does (the user reset it in the editor) - <see cref="ApplyOverrides"/>
    /// only ever touches ids present in the dictionary it's given, so without a full rebuild first,
    /// a removed override would leave the previous (now-stale) shortcut in place instead of
    /// reverting to the built-in default. Safe to call again on a live <see cref="HotkeyManager"/>
    /// after Settings saves a new set of overrides - see <c>MainForm.OpenSettings</c>'s
    /// <c>SettingsSaved</c> handler.</summary>
    public void Reload(IReadOnlyDictionary<string, string> overrides)
    {
        _hotkeys.Clear();
        _idOccurrences.Clear();
        RegisterDefaults();
        ApplyOverrides(overrides);
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
        Register(Keys.Alt | Keys.Left, CommandIds.GoBack);
        Register(Keys.Alt | Keys.Right, CommandIds.GoForward);
        Register(Keys.Back, CommandIds.GoToParent);
        Register(Keys.Control | Keys.Back, CommandIds.GoToParent);
        Register(Keys.Control | Keys.R, CommandIds.Refresh);
        Register(Keys.Control | Keys.Shift | Keys.R, CommandIds.RefreshDrives);
        Register(Keys.Alt | Keys.F7, CommandIds.FindFiles);

        // Selection
        Register(Keys.Control | Keys.A, CommandIds.SelectAll);
        Register(Keys.Control | Keys.D, CommandIds.DeselectAll);
        Register(Keys.Add, CommandIds.InvertSelection); // Num+
        Register(Keys.Control | Keys.Add, CommandIds.SelectGroup);
        Register(Keys.Control | Keys.Subtract, CommandIds.DeselectGroup);

        // Navigation extras
        // Ctrl+\ - registered on Keys.OemPipe (VK_OEM_5, the "\|" key on a standard ANSI/RU
        // 104-key keyboard), not Keys.OemBackslash (VK_OEM_102, only present on ISO 102-key
        // layouts). HandleKey below matches by exact KeyData, so binding only OemBackslash made
        // this command entirely unreachable on the far more common 104-key layout - caught by
        // reading the actual VK codes, not by trusting the "Ctrl+\" comment that used to be here.
        // OemBackslash stays registered as a second binding for ISO keyboards that do have it.
        Register(Keys.Control | Keys.OemPipe, CommandIds.GoToRoot); // Ctrl+\
        Register(Keys.Control | Keys.OemBackslash, CommandIds.GoToRoot); // Ctrl+\ on ISO layouts
        Register(Keys.Control | Keys.Home, CommandIds.GoToHome); // Ctrl+Home
        Register(Keys.Control | Keys.G, CommandIds.ChangeDir); // Ctrl+G

        // Panel tabs - plain Ctrl+T/Ctrl+W are free at the app level (the embedded terminal keeps
        // Ctrl+Shift+T/Ctrl+Shift+W for its own tabs specifically so a shell in focus keeps plain
        // Ctrl+T/Ctrl+W for its own use, e.g. readline's Ctrl+T transpose-chars - see the terminal
        // tab registrations below). Next/Previous use Ctrl+PageDown/PageUp, not Ctrl+Tab - that's
        // already NextTerminalTab.
        Register(Keys.Control | Keys.T, CommandIds.NewTab);
        Register(Keys.Control | Keys.W, CommandIds.CloseTab);
        Register(Keys.Control | Keys.Next, CommandIds.NextTab); // Ctrl+PageDown
        Register(Keys.Control | Keys.Prior, CommandIds.PreviousTab); // Ctrl+PageUp

        Register(Keys.Control | Keys.Q, CommandIds.ToggleQuickView);

        // Panel
        Register(Keys.Control | Keys.U, CommandIds.SwapPanels);
        Register(Keys.Control | Keys.OemPeriod, CommandIds.ToggleHidden); // Ctrl+.
        Register(Keys.Control | Keys.P, CommandIds.ToggleFlatView); // Ctrl+P - also in the View menu (Menu.View.FlatView)
        Register(Keys.Control | Keys.F, CommandIds.ToggleQuickFilter); // Ctrl+F
        Register(Keys.Alt | Keys.Enter, CommandIds.ShowProperties);
        Register(Keys.Control | Keys.Alt | Keys.Space, CommandIds.CalculateFolderSize);

        // Batch operations
        Register(Keys.Control | Keys.M, CommandIds.MultiRename);

        // File extras
        Register(Keys.Shift | Keys.F4, CommandIds.EditNew);
        Register(Keys.Alt | Keys.F5, CommandIds.PackFiles);
        Register(Keys.Alt | Keys.F9, CommandIds.UnpackFiles);

        // View
        Register(Keys.Control | Keys.D1, CommandIds.SetTheme, "Dark");
        Register(Keys.Control | Keys.D2, CommandIds.SetTheme, "Light");

        // Terminal tabs - matches TerminalKeyBindings.WindowsTerminalPreset's own tab chords, so
        // the same shortcut does the same thing whether or not the terminal canvas has focus.
        // Plain Ctrl+T/Ctrl+W are deliberately NOT bound here (unlike before the ConPTY rewrite):
        // a real shell needs those free for its own use (e.g. readline's Ctrl+T transpose-chars).
        Register(Keys.Control | Keys.Shift | Keys.T, CommandIds.CreateTerminalTab);
        Register(Keys.Control | Keys.Shift | Keys.W, CommandIds.CloseTerminalTab);
        Register(Keys.Control | Keys.Tab, CommandIds.NextTerminalTab);
        Register(Keys.Control | Keys.Shift | Keys.Tab, CommandIds.PreviousTerminalTab);
    }
}
