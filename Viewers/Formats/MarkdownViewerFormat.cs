using System.Threading;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;
using Markdig;

namespace CoderCommander.Viewers.Formats;

/// <summary>Matched format for Markdown - the one WebView format that actually transforms
/// content rather than just navigating WebView2 at a file directly (see
/// <see cref="MaterializingViewerLoader"/> for the other three). Reuses the same size limit and
/// encoding-detection path as Text mode, same reasoning as <c>CsvViewerFormat</c>'s own doc
/// comment: a Markdown file is text first.</summary>
public sealed class MarkdownViewerFormat : IViewerFormat
{
    public static readonly MarkdownViewerFormat Instance = new();
    private MarkdownViewerFormat() { }

    public string Id => "markdown";
    public string DisplayNameKey => "View.Markdown";
    public string IconKey => "view_markdown";
    public IReadOnlyList<string> Extensions => [".md", ".markdown"];
    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.NeedsWebView;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new MarkdownViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new MarkdownViewerContent(ctx);
}

internal sealed class MarkdownViewerLoader : IViewerLoader
{
    // .DisableHtml() rejects raw HTML embedded in the Markdown source (e.g. a <script> or
    // <iframe> block) rather than passing it through - the security baseline WebViewHost applies
    // (scripts off, same-origin navigation lock) already covers most of that surface, but Markdig
    // shouldn't hand a script tag to the DOM at all when the app itself controls what "rendering
    // Markdown" is supposed to mean.
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();

    public async Task<ViewerPayload> LoadAsync(ViewerSource source, CancellationToken ct)
    {
        var L = LocalizationService.Current;
        var size = await source.GetSizeAsync(ct).ConfigureAwait(false);

        if (size > ViewerLimits.TextSizeLimit)
            return new ViewerErrorPayload(
                L.GetString("View.TooBigForText", FormatUtils.FormatSize(size), FormatUtils.FormatSize(ViewerLimits.TextSizeLimit)),
                Modal: false);

        var raw = await source.ReadAllBytesAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var encoding = TextEncodingDetector.Detect(raw, out var preambleLength);
        var sourceText = encoding.GetString(raw, preambleLength, raw.Length - preambleLength);
        ct.ThrowIfCancellationRequested();

        var body = Markdown.ToHtml(sourceText, Pipeline);
        ct.ThrowIfCancellationRequested();

        var renderedHtml = ViewerHtmlTemplate.WrapDocument(body);
        var status = L.GetString("View.MarkdownMode", FormatUtils.FormatSize(size));
        return new MarkdownPayload(renderedHtml, sourceText, status);
    }
}
