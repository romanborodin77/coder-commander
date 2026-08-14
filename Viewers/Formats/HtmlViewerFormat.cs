using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Matched format for HTML - "browser mode": navigates WebView2 directly at the file, so
/// relative links/images/CSS resolve exactly as they would double-clicking the file in Explorer.
/// For a local file this maps the file's own real containing directory (not an isolated copy),
/// deliberately exposing sibling files under the same virtual host - the whole point of browser
/// mode; see <see cref="MaterializedFilePayload"/>'s own doc comment for the local/non-local
/// split this relies on.</summary>
public sealed class HtmlViewerFormat : IViewerFormat
{
    public static readonly HtmlViewerFormat Instance = new();
    private HtmlViewerFormat() { }

    public string Id => "html";
    public string DisplayNameKey => "View.Html";
    public string IconKey => "view_html";
    public IReadOnlyList<string> Extensions => [".html", ".htm"];
    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.NeedsWebView;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new HtmlViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new HtmlViewerContent(ctx);
}

internal sealed class HtmlViewerLoader : MaterializingViewerLoader
{
    protected override string BuildStatus(LocalizationService localization, long size) =>
        localization.GetString("View.HtmlMode", FormatUtils.FormatSize(size));
}
