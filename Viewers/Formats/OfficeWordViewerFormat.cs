using System.Threading;
using CoderCommander.Viewers.Office;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Matched format for word-processing documents - <c>.docx</c> (OOXML) and <c>.odt</c>
/// (ODF), converted to a single HTML page via <see cref="OoxmlWordConverter"/>/
/// <see cref="OdfTextConverter"/>. Legacy binary <c>.doc</c> is out of scope - see the plan's own
/// "Свой OOXML/ODF → HTML" decision; a binary .doc doesn't parse as a ZIP at all, so it would fail
/// closed as "corrupted" via the same path an encrypted OOXML package does, not silently.</summary>
public sealed class OfficeWordViewerFormat : IViewerFormat
{
    public static readonly OfficeWordViewerFormat Instance = new();
    private OfficeWordViewerFormat() { }

    public string Id => "office.word";
    public string DisplayNameKey => "View.Office.Word";
    public string IconKey => "view_office";
    public IReadOnlyList<string> Extensions => [".docx", ".odt"];
    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.NeedsWebView;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new OfficeWordViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new OfficeViewerContent(ctx);
}

internal sealed class OfficeWordViewerLoader : OfficeViewerLoaderBase
{
    protected override string StatusKey => "View.Office.WordMode";

    protected override async Task<List<OfficeDocumentPage>> ConvertAsync(OfficePackage pkg, string extension, CancellationToken ct)
    {
        var html = extension == ".odt"
            ? await OdfTextConverter.ConvertAsync(pkg, ct).ConfigureAwait(false)
            : await OoxmlWordConverter.ConvertAsync(pkg, ct).ConfigureAwait(false);
        return [new OfficeDocumentPage("", html)];
    }
}
