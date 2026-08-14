using CoderCommander.Viewers;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// Audio/video content - navigates the shared <see cref="WebViewHost"/> straight at the
/// (materialized, if not local) file; Chromium's own built-in media player (volume, seek,
/// play/pause) renders for a direct navigation to a video/audio URL, so no custom player chrome
/// of our own is needed - see <c>MediaViewerFormat</c>'s own doc comment.
/// </summary>
internal sealed class MediaViewerContent : WebFileViewerContentBase
{
    public MediaViewerContent(ViewerContentContext ctx) : base(ctx) { }
}
