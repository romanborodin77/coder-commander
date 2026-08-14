using CoderCommander.Viewers;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// PDF content - navigates the shared <see cref="WebViewHost"/> straight at the (materialized, if
/// not local) file; Edge's own embedded PDF viewer does the rendering, including its own paging/
/// zoom/print chrome. No toolbar items of our own: a from-scratch page-tracking toolbar would
/// need <c>#page=N</c> URL-fragment navigation to work reliably against WebView2, which is not a
/// verified behavior (see the plan's own R9 risk entry) - deferring to the built-in viewer's
/// chrome is the documented fallback, not a placeholder for one that never got built.
/// </summary>
internal sealed class PdfViewerContent : WebFileViewerContentBase
{
    public PdfViewerContent(ViewerContentContext ctx) : base(ctx) { }
}
