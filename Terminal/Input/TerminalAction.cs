namespace CoderCommander.Terminal.Input;

/// <summary>
/// Every app-level action a key chord can be bound to inside the terminal panel. Deliberately
/// separate from <see cref="Commands.CommandIds"/> - these are terminal-local (tab management,
/// scrollback, clipboard) and never routed through <c>CommandEngine</c>, since the terminal must
/// keep working the same way regardless of what the rest of the app's hotkeys are configured to.
/// </summary>
internal enum TerminalAction
{
    /// <summary>Not bound to anything - the chord falls through to <see cref="VtKeyEncoder"/> (or,
    /// failing that, is swallowed and dropped).</summary>
    None,

    Copy,
    Paste,
    /// <summary>Ctrl+C's classic dual role: copy the selection if one exists, otherwise send the
    /// interrupt byte (0x03) to the pty.</summary>
    CopyOrInterrupt,
    SelectAll,
    Find,
    ClearBuffer,
    ResetTerminal,

    NewTab,
    CloseTab,
    NextTab,
    PrevTab,
    RenameTab,

    ScrollLineUp,
    ScrollLineDown,
    ScrollPageUp,
    ScrollPageDown,
    ScrollToTop,
    ScrollToBottom,

    IncreaseFont,
    DecreaseFont,
    ResetFont,

    ToggleTerminalPanel,
}
