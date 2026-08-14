namespace CoderCommander.Viewers;

/// <summary>
/// What the viewer's find bar needs from a content in order to search it - generalizes the four
/// things it used to reach straight into a <c>RichTextBox</c> for (<c>.Text</c>,
/// <c>.SelectionStart</c>, <c>.Select</c>+<c>.ScrollToCaret</c>, <c>.Focus</c>), so a future
/// non-<c>RichTextBox</c> content (Markdown/HTML source view, phase 2+) can participate without
/// the find bar knowing its concrete control type.
/// </summary>
public interface IViewerSearchTarget
{
    /// <summary>The full searchable text.</summary>
    string GetSearchText();

    /// <summary>Anchor offset a re-run resumes from, so re-searching after the user moved the
    /// caret manually continues from where they are rather than always restarting at the top.</summary>
    int CurrentOffset { get; }

    void SelectRange(int start, int length);

    void FocusContent();
}
