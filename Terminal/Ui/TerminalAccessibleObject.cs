namespace CoderCommander.Terminal.Ui;

/// <summary>
/// UIA/MSAA accessibility for <see cref="TerminalCanvas"/>. A bare owner-drawn <c>Control</c>
/// exposes nothing but its bounds to automation/screen readers - the pre-rewrite terminal got
/// text accessibility for free from hosting a real <c>RichTextBox</c>, and this restores the
/// baseline (the control's current screen content is readable as its accessible Value) rather than
/// silently regressing it. Deliberately not a full UIA <c>ITextProvider</c> implementation (no
/// per-line navigation, no live-region change announcements) - restoring "a screen reader can read
/// what's on screen" is the goal here, not building out a complete custom text-pattern provider.
/// </summary>
internal sealed class TerminalAccessibleObject : Control.ControlAccessibleObject
{
    private readonly TerminalCanvas _owner;

    public TerminalAccessibleObject(TerminalCanvas owner) : base(owner) => _owner = owner;

    public override AccessibleRole Role => AccessibleRole.Text;

    public override string Value => _owner.GetVisibleScreenText();

    public override AccessibleStates State
    {
        get
        {
            var state = base.State | AccessibleStates.Focusable | AccessibleStates.Selectable;
            if (_owner.Focused) state |= AccessibleStates.Focused;
            return state;
        }
    }
}
