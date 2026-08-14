using CoderCommander.Services;
using CoderCommander.WinForms;

namespace CoderCommander.WinForms.Viewers;

/// <summary>Toolbar-button factories shared by <c>ViewerForm</c> itself (Prev/Next/mode buttons)
/// and every <see cref="Viewers.IViewerContent"/> implementation - promoted out of the pre-rewrite
/// <c>ViewerForm.CreateToolButton</c>/<c>CreateIconButton</c> verbatim, since content classes now
/// need the same two shapes.</summary>
internal static class ViewerToolbarFactory
{
    /// <summary>Text + icon button (mode buttons, Prev/Next, Find, Close).</summary>
    public static ToolStripButton CreateToolButton(string textKey, string iconKey, EventHandler onClick)
    {
        var L = LocalizationService.Current;
        var btn = new ToolStripButton(L.GetString(textKey), ToolbarIcons.Get(iconKey))
        {
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ToolTipText = L.GetString(textKey)
        };
        btn.Click += onClick;
        return btn;
    }

    /// <summary>Icon-only button with a localized tooltip - used for the image-mode zoom/rotate
    /// cluster, matching IrfanView's compact toolbar shape rather than every button spelling its
    /// own label out.</summary>
    public static ToolStripButton CreateIconButton(string iconKey, string tooltipKey, EventHandler onClick)
    {
        var L = LocalizationService.Current;
        var btn = new ToolStripButton(ToolbarIcons.Get(iconKey))
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = L.GetString(tooltipKey),
            // Image-only buttons have no caption to fall back on, so without an explicit
            // accessible name they show up nameless in the UIA tree (and to a screen reader).
            AccessibleName = L.GetString(tooltipKey)
        };
        btn.Click += onClick;
        return btn;
    }
}
