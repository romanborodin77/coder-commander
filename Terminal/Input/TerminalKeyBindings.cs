namespace CoderCommander.Terminal.Input;

/// <summary>
/// Chord -&gt; <see cref="TerminalAction"/> table for the terminal panel, independent from the
/// rest of the app's <c>HotkeyManager</c>. Three presets per the approved plan: <see cref="WindowsTerminalPreset"/>
/// (the default), <see cref="ClassicPreset"/> (mirrors this app's pre-rewrite Ctrl+T/Ctrl+W/Ctrl+Tab
/// layout), and a user-customized table built from persisted chord strings via <see cref="TryParseChord"/>/
/// <see cref="FormatChord"/> (wired up by the settings UI in a later phase).
/// <para>
/// A chord NOT present in the table is not necessarily unhandled - <see cref="VtKeyEncoder"/> gets
/// first refusal for anything this table doesn't claim, so e.g. plain Ctrl+V (unbound by the
/// Windows Terminal preset on purpose) still reaches the shell as a literal control byte instead of
/// silently vanishing.
/// </para>
/// </summary>
internal sealed class TerminalKeyBindings
{
    private readonly Dictionary<Keys, TerminalAction> _bindings;

    public TerminalKeyBindings(IReadOnlyDictionary<Keys, TerminalAction> bindings) =>
        _bindings = new Dictionary<Keys, TerminalAction>(bindings);

    public IReadOnlyDictionary<Keys, TerminalAction> Bindings => _bindings;

    public TerminalAction Resolve(Keys keyData) =>
        _bindings.TryGetValue(keyData, out var action) ? action : TerminalAction.None;

    /// <summary>Builds the active table from <c>AppSettings.TerminalKeyBindingPreset</c>/
    /// <c>TerminalCustomKeyBindings</c> - a plain function of its inputs rather than reading
    /// settings itself, so it stays unit-testable and the "what settings say" vs. "what table
    /// results" mapping is explicit at the call site. An unparseable custom chord or unknown
    /// action name is skipped rather than failing the whole table (a hand-edited or
    /// version-skewed settings.json shouldn't be able to break every terminal tab).</summary>
    public static TerminalKeyBindings FromSettings(string preset, IReadOnlyDictionary<string, string> customBindings)
    {
        if (preset == "Classic")
            return ClassicPreset();

        if (preset == "Custom")
        {
            var bindings = new Dictionary<Keys, TerminalAction>();
            foreach (var (actionName, chordText) in customBindings)
            {
                if (Enum.TryParse<TerminalAction>(actionName, out var action) &&
                    action != TerminalAction.None &&
                    TryParseChord(chordText, out var chord))
                    bindings[chord] = action;
            }
            return new TerminalKeyBindings(bindings);
        }

        return WindowsTerminalPreset();
    }

    /// <summary>Windows-Terminal-style chords - the default preset. Ctrl+C copies the selection if
    /// one exists and otherwise falls through to <see cref="VtKeyEncoder"/> as the interrupt byte
    /// (see <see cref="TerminalAction.CopyOrInterrupt"/>); Ctrl+Shift+C/V are the unambiguous
    /// copy/paste chords. Plain Ctrl+V is deliberately left unbound so it still reaches the shell as
    /// a literal control byte (e.g. readline's "insert next literal character").</summary>
    public static TerminalKeyBindings WindowsTerminalPreset() => new(new Dictionary<Keys, TerminalAction>
    {
        [Keys.Control | Keys.C] = TerminalAction.CopyOrInterrupt,
        [Keys.Control | Keys.Shift | Keys.C] = TerminalAction.Copy,
        [Keys.Control | Keys.Shift | Keys.V] = TerminalAction.Paste,
        [Keys.Control | Keys.Shift | Keys.F] = TerminalAction.Find,
        [Keys.Control | Keys.Shift | Keys.K] = TerminalAction.ClearBuffer,

        [Keys.Control | Keys.Shift | Keys.T] = TerminalAction.NewTab,
        [Keys.Control | Keys.Shift | Keys.W] = TerminalAction.CloseTab,
        [Keys.Control | Keys.Tab] = TerminalAction.NextTab,
        [Keys.Control | Keys.Shift | Keys.Tab] = TerminalAction.PrevTab,

        [Keys.Control | Keys.Shift | Keys.Up] = TerminalAction.ScrollLineUp,
        [Keys.Control | Keys.Shift | Keys.Down] = TerminalAction.ScrollLineDown,
        [Keys.Control | Keys.Shift | Keys.Prior] = TerminalAction.ScrollPageUp,
        [Keys.Control | Keys.Shift | Keys.Next] = TerminalAction.ScrollPageDown,
        [Keys.Control | Keys.Shift | Keys.Home] = TerminalAction.ScrollToTop,
        [Keys.Control | Keys.Shift | Keys.End] = TerminalAction.ScrollToBottom,

        [Keys.Control | Keys.Oemplus] = TerminalAction.IncreaseFont,
        [Keys.Control | Keys.OemMinus] = TerminalAction.DecreaseFont,
        [Keys.Control | Keys.D0] = TerminalAction.ResetFont,
    });

    /// <summary>Mirrors this app's pre-rewrite terminal chords (Ctrl+T/Ctrl+W/Ctrl+Tab/Ctrl+Shift+Tab
    /// for tabs; plain Ctrl+C/Ctrl+V for copy/paste) for users who want the old layout back. Ctrl+C
    /// keeps the same copy-or-interrupt duality as the Windows Terminal preset - a bare Ctrl+C that
    /// always interrupted, with no way to copy via keyboard, would be a regression, not fidelity.</summary>
    public static TerminalKeyBindings ClassicPreset() => new(new Dictionary<Keys, TerminalAction>
    {
        [Keys.Control | Keys.C] = TerminalAction.CopyOrInterrupt,
        [Keys.Control | Keys.V] = TerminalAction.Paste,
        [Keys.Control | Keys.Shift | Keys.F] = TerminalAction.Find,

        [Keys.Control | Keys.T] = TerminalAction.NewTab,
        [Keys.Control | Keys.W] = TerminalAction.CloseTab,
        [Keys.Control | Keys.Tab] = TerminalAction.NextTab,
        [Keys.Control | Keys.Shift | Keys.Tab] = TerminalAction.PrevTab,

        [Keys.Control | Keys.Shift | Keys.Up] = TerminalAction.ScrollLineUp,
        [Keys.Control | Keys.Shift | Keys.Down] = TerminalAction.ScrollLineDown,
        [Keys.Control | Keys.Prior] = TerminalAction.ScrollPageUp,
        [Keys.Control | Keys.Next] = TerminalAction.ScrollPageDown,
        [Keys.Control | Keys.Home] = TerminalAction.ScrollToTop,
        [Keys.Control | Keys.End] = TerminalAction.ScrollToBottom,

        [Keys.Control | Keys.Oemplus] = TerminalAction.IncreaseFont,
        [Keys.Control | Keys.OemMinus] = TerminalAction.DecreaseFont,
        [Keys.Control | Keys.D0] = TerminalAction.ResetFont,
    });

    /// <summary>Formats a chord for display/persistence, e.g. <c>Keys.Control | Keys.Shift | Keys.T</c>
    /// -&gt; <c>"Ctrl+Shift+T"</c>. Modifier order is always Ctrl, Alt, Shift.</summary>
    public static string FormatChord(Keys chord)
    {
        var parts = new List<string>(4);
        if (chord.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (chord.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (chord.HasFlag(Keys.Shift)) parts.Add("Shift");

        var keyCode = chord & Keys.KeyCode;
        if (keyCode != Keys.None) parts.Add(keyCode.ToString());

        return string.Join("+", parts);
    }

    /// <summary>Parses a chord previously produced by <see cref="FormatChord"/>. Returns false
    /// (rather than throwing) for anything malformed, since the source is always persisted user
    /// input from a settings file that could have been hand-edited or come from an older version.</summary>
    public static bool TryParseChord(string text, out Keys chord)
    {
        chord = Keys.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var segments = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
            return false;

        var result = Keys.None;
        var haveKeyCode = false;
        foreach (var segment in segments)
        {
            if (segment.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                result |= Keys.Control;
            }
            else if (segment.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                result |= Keys.Alt;
            }
            else if (segment.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                result |= Keys.Shift;
            }
            else if (!haveKeyCode && Enum.TryParse<Keys>(segment, ignoreCase: true, out var keyCode))
            {
                result |= keyCode;
                haveKeyCode = true;
            }
            else
            {
                return false;
            }
        }

        if (!haveKeyCode)
            return false;

        // Reject chords without at least one modifier — a bare letter key binding would hijack
        // normal typing (e.g. binding "T" to CloseTab makes it impossible to type the letter T).
        if ((result & Keys.Modifiers) == Keys.None)
            return false;

        chord = result;
        return true;
    }
}
