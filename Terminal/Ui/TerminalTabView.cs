using CoderCommander.Terminal.Input;

namespace CoderCommander.Terminal.Ui;

/// <summary>
/// One terminal tab's visible content: a <see cref="TerminalCanvas"/> filling the tab, with an
/// inline <see cref="TerminalFindBar"/> docked above it, shown on <see cref="TerminalAction.Find"/>.
/// This is what becomes a <c>WinForms.ThemedTabPage</c>'s Content - <see cref="Canvas"/> is what
/// should actually receive keyboard focus (see <c>EmbeddedTerminalPanel</c>'s focus-routing calls),
/// not this wrapper.
/// </summary>
internal sealed class TerminalTabView : Panel
{
    public TerminalCanvas Canvas { get; }
    public TerminalFindBar FindBar { get; }

    public TerminalTabView(TerminalSession session, TerminalKeyBindings keyBindings)
    {
        Canvas = new TerminalCanvas(session, keyBindings) { Dock = DockStyle.Fill };
        FindBar = new TerminalFindBar(Canvas);
        Canvas.ActionRequested += OnCanvasActionRequested;

        // Fill must be added first (WinForms docking order) - see CodeEditorControl for the same
        // pattern with FindReplaceBar.
        Controls.Add(Canvas);
        Controls.Add(FindBar);
    }

    private void OnCanvasActionRequested(object? sender, TerminalAction action)
    {
        if (action == TerminalAction.Find)
            FindBar.ShowBar();
    }
}
