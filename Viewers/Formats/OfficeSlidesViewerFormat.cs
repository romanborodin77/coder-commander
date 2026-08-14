using System.Threading;
using CoderCommander.Viewers.Office;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Matched format for presentations - <c>.pptx</c> (OOXML) and <c>.odp</c> (ODF), one
/// HTML page per slide via <see cref="OoxmlSlidesConverter"/>/<see cref="OdfSlidesConverter"/>.</summary>
public sealed class OfficeSlidesViewerFormat : IViewerFormat
{
    public static readonly OfficeSlidesViewerFormat Instance = new();
    private OfficeSlidesViewerFormat() { }

    public string Id => "office.slides";
    public string DisplayNameKey => "View.Office.Slides";
    public string IconKey => "view_office";
    public IReadOnlyList<string> Extensions => [".pptx", ".odp"];
    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.NeedsWebView;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new OfficeSlidesViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new OfficeViewerContent(ctx);
}

internal sealed class OfficeSlidesViewerLoader : OfficeViewerLoaderBase
{
    protected override string StatusKey => "View.Office.SlidesMode";

    protected override Task<List<OfficeDocumentPage>> ConvertAsync(OfficePackage pkg, string extension, CancellationToken ct) =>
        extension == ".odp"
            ? OdfSlidesConverter.ConvertAsync(pkg, ct)
            : OoxmlSlidesConverter.ConvertAsync(pkg, ct);
}
