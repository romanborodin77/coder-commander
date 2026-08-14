using System.Threading;
using CoderCommander.Viewers.Office;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Matched format for spreadsheets - <c>.xlsx</c> (OOXML) and <c>.ods</c> (ODF), one HTML
/// page per sheet via <see cref="OoxmlSheetConverter"/>/<see cref="OdfSheetConverter"/>.</summary>
public sealed class OfficeSheetViewerFormat : IViewerFormat
{
    public static readonly OfficeSheetViewerFormat Instance = new();
    private OfficeSheetViewerFormat() { }

    public string Id => "office.sheet";
    public string DisplayNameKey => "View.Office.Sheet";
    public string IconKey => "view_office";
    public IReadOnlyList<string> Extensions => [".xlsx", ".ods"];
    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.NeedsWebView;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new OfficeSheetViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new OfficeViewerContent(ctx);
}

internal sealed class OfficeSheetViewerLoader : OfficeViewerLoaderBase
{
    protected override string StatusKey => "View.Office.SheetMode";

    protected override Task<List<OfficeDocumentPage>> ConvertAsync(OfficePackage pkg, string extension, CancellationToken ct) =>
        extension == ".ods"
            ? OdfSheetConverter.ConvertAsync(pkg, ct)
            : OoxmlSheetConverter.ConvertAsync(pkg, ct);
}
