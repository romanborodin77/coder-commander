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

    /// <summary>Delegates to the app's Copy command (file panel) - see
    /// <see cref="WinForms.EmbeddedTerminalPanel.AppCommandRequested"/>. F5, its default binding
    /// (like the other five App* actions below), never actually reaches this dispatch path: F5-F8/
    /// Ctrl+R/Ctrl+L are separately hardcoded as pass-through keys in
    /// <c>TerminalCanvas.IsAppPassthroughKey</c>/<c>ProcessCmdKey</c>, bypassing terminal chord
    /// resolution entirely so the app's own <c>HotkeyManager</c> (which already binds those same
    /// keys to the same commands) handles them directly - this is what makes F5-F8 work in the
    /// terminal today, not this enum. Where this action DOES matter is a user rebinding it (via
    /// TerminalKeyBindingsForm) to some other chord: that chord isn't in the hardcoded pass-through
    /// list, so it resolves normally and reaches <c>AppCommandRequested</c>.</summary>
    AppCopy,
    /// <summary>F6 by default (see <see cref="AppCopy"/>'s doc comment for why that default binding
    /// itself never reaches this path) - delegates to the app's Move/Rename command.</summary>
    AppMove,
    /// <summary>F7 by default (see <see cref="AppCopy"/>) - delegates to the app's MakeDir command.</summary>
    AppMakeDir,
    /// <summary>F8 by default (see <see cref="AppCopy"/>) - delegates to the app's Delete command.</summary>
    AppDelete,
    /// <summary>Ctrl+R by default (see <see cref="AppCopy"/>) - delegates to the app's Refresh command.</summary>
    AppRefresh,
    /// <summary>Ctrl+L by default (see <see cref="AppCopy"/>) - delegates to the app's ChangeDir command.</summary>
    AppChangeDir,
}
