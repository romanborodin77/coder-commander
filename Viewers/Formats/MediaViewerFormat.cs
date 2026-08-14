using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Matched format for common audio/video containers - navigates WebView2 directly at
/// the (materialized, if not local) file; Chromium shows its own built-in minimal media player
/// for a direct navigation to a video/audio URL, so no template and no custom player chrome of
/// our own are needed. Signature sniffing is deliberately not implemented (unlike Image/Pdf) -
/// container formats like MP4/WebM/MKV don't have a single fixed magic-byte offset simple enough
/// to be worth it here; extension matching alone is the detection path.</summary>
public sealed class MediaViewerFormat : IViewerFormat
{
    public static readonly MediaViewerFormat Instance = new();
    private MediaViewerFormat() { }

    public string Id => "media";
    public string DisplayNameKey => "View.Media";
    public string IconKey => "view_media";

    public IReadOnlyList<string> Extensions =>
        [".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v", ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac"];

    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.NeedsWebView;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new MediaViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new MediaViewerContent(ctx);
}

internal sealed class MediaViewerLoader : MaterializingViewerLoader
{
    protected override string BuildStatus(LocalizationService localization, long size) =>
        localization.GetString("View.MediaMode", FormatUtils.FormatSize(size));
}
