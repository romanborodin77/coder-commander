using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Matched format for PDF - navigates WebView2 directly at the (materialized, if not
/// local) file and lets Edge's own embedded PDF viewer render it; no template, no page-tracking
/// toolbar of our own (see <see cref="PdfViewerContent"/>'s own doc comment for why - the built-in
/// viewer's chrome already covers paging/zoom/print). Offered only when the runtime is present -
/// see <see cref="WebViewAvailability"/>.</summary>
public sealed class PdfViewerFormat : IViewerFormat
{
    public static readonly PdfViewerFormat Instance = new();
    private PdfViewerFormat() { }

    public string Id => "pdf";
    public string DisplayNameKey => "View.Pdf";
    public string IconKey => "view_pdf";
    public IReadOnlyList<string> Extensions => [".pdf"];
    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.NeedsWebView;

    public bool MatchesSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 5 &&
        header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F' && header[4] == '-';

    public IViewerLoader CreateLoader() => new PdfViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new PdfViewerContent(ctx);
}

internal sealed class PdfViewerLoader : MaterializingViewerLoader
{
    protected override string BuildStatus(LocalizationService localization, long size) =>
        localization.GetString("View.PdfMode", FormatUtils.FormatSize(size));
}
