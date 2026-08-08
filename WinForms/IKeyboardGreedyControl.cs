namespace CoderCommander.WinForms;

/// <summary>
/// Capability for a focused control that wants to swallow almost all keyboard input rather than
/// let it reach app-level hotkeys - e.g. <see cref="Terminal.Ui.TerminalCanvas"/>, which forwards
/// nearly every keystroke to the shell it hosts. <c>MainForm.OnFormKeyDown</c>'s focus-walk guard
/// normally only lets <c>TextBox</c>/<c>ComboBox</c>/<c>NumericUpDown</c>/<c>DomainUpDown</c>
/// suppress app-level hotkeys by type; this interface lets a custom owner-drawn control opt into
/// the same protection without MainForm needing to know about it by concrete type.
/// <para>
/// This is defense in depth, not the primary mechanism: the control's own
/// <c>ProcessCmdKey</c> override is what actually keeps a keystroke from ever reaching this far
/// (see <c>TerminalCanvas.ProcessCmdKey</c>'s doc comment). This interface exists in case some key
/// combination slips past that first line of defense (Alt-only chords in particular can be
/// intercepted by the OS menu/accelerator system before <c>ProcessCmdKey</c> ever runs).
/// </para>
/// </summary>
public interface IKeyboardGreedyControl
{
    /// <summary>True if <paramref name="keyCode"/> should be allowed to reach app-level hotkey
    /// handling despite this control having focus (e.g. F9 to toggle the terminal panel closed).
    /// False for everything this control wants to keep for itself.</summary>
    bool AllowsAppHotkey(Keys keyCode);
}
