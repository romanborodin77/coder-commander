using System.Globalization;
using System.Xml.Linq;

namespace CoderCommander.Viewers.Office;

/// <summary>
/// Converts a <c>.pptx</c> presentation to one HTML page per slide - each shape/picture
/// absolutely positioned via its own <c>a:xfrm</c> offset/extent, converted from EMU
/// (English Metric Units, OOXML's native drawing unit) to pixels at <c>px = emu / 9525</c>
/// (914400 EMU/inch ÷ 96 px/inch).
///
/// <para><b>Deliberately not resolved:</b> <c>slideLayouts</c>/<c>slideMasters</c> - a shape that
/// inherits its position from the layout rather than declaring its own <c>a:xfrm</c> renders in
/// normal document flow instead (see <see cref="PositionStyle"/>), so nothing is silently dropped,
/// it just loses its intended position. Resolving the full layout/master inheritance chain is a
/// second and third document parse per slide for a detail most previews don't need.</para>
/// </summary>
internal static class OoxmlSlidesConverter
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = OoxmlNamespaces.Drawing;
    private static readonly XNamespace R = OoxmlNamespaces.Relationships;

    private const string PresentationPart = "ppt/presentation.xml";
    private const string PresentationRelsPart = "ppt/_rels/presentation.xml.rels";

    public static async Task<List<OfficeDocumentPage>> ConvertAsync(OfficePackage pkg, CancellationToken ct)
    {
        var presentation = pkg.ReadXml(PresentationPart) ?? throw new InvalidDataException("ppt/presentation.xml not found.");
        var rels = OoxmlWordConverter.LoadRelationships(pkg, PresentationRelsPart, PresentationPart);

        var sldSz = presentation.Root?.Element(P + "sldSz");
        var slideWidthPx = EmuToPx(sldSz?.Attribute("cx")?.Value);
        var slideHeightPx = EmuToPx(sldSz?.Attribute("cy")?.Value);

        var pages = new List<OfficeDocumentPage>();
        // Shared across every slide - see OfficeHtmlWriter.TryEmbedImage's doc comment. Without
        // this, each slide's own fresh OfficeHtmlWriter got its own 64MB image budget, so the
        // effective per-document ceiling was 64MB times the slide count.
        var imageBudget = new OfficeImageBudget();
        var index = 0;
        foreach (var sldId in presentation.Root?.Element(P + "sldIdLst")?.Elements(P + "sldId") ?? [])
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var relId = sldId.Attribute(R + "id")?.Value;
            if (relId == null || !rels.TryGetValue(relId, out var slidePart)) continue;

            var html = await RenderSlideAsync(pkg, slidePart, slideWidthPx, slideHeightPx, imageBudget, ct).ConfigureAwait(false);
            pages.Add(new OfficeDocumentPage(index.ToString(CultureInfo.InvariantCulture), html));
        }

        if (pages.Count == 0) throw new InvalidDataException("Presentation has no readable slides.");
        return pages;
    }

    private static async Task<string> RenderSlideAsync(OfficePackage pkg, string slidePart, int widthPx, int heightPx,
        OfficeImageBudget imageBudget, CancellationToken ct)
    {
        var slideDoc = pkg.ReadXml(slidePart);
        var spTree = slideDoc?.Root?.Element(P + "cSld")?.Element(P + "spTree");
        var rels = OoxmlWordConverter.LoadRelationships(pkg, RelsPathFor(slidePart), slidePart);

        var writer = new OfficeHtmlWriter(imageBudget);
        var containerStyle = widthPx > 0 && heightPx > 0
            ? $"position:relative;width:{widthPx}px;height:{heightPx}px;margin:0 auto;overflow:hidden;"
            : "position:relative;";
        writer.RawLine($"<div style=\"{containerStyle}\">");

        foreach (var shape in spTree?.Elements() ?? Enumerable.Empty<XElement>())
        {
            ct.ThrowIfCancellationRequested();
            if (shape.Name == P + "sp")
                RenderShape(shape, writer);
            else if (shape.Name == P + "pic")
                await RenderPictureAsync(shape, pkg, rels, writer, ct).ConfigureAwait(false);
        }

        writer.RawLine("</div>");
        return writer.Build();
    }

    private static void RenderShape(XElement sp, OfficeHtmlWriter writer)
    {
        var text = string.Concat(
            (sp.Element(P + "txBody")?.Elements(A + "p") ?? Enumerable.Empty<XElement>())
            .Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value)) + "\n"));
        if (string.IsNullOrWhiteSpace(text)) return;

        var style = PositionStyle(sp.Element(P + "spPr")?.Element(A + "xfrm"));
        writer.Raw($"<div style=\"{style}white-space:pre-wrap;\">");
        writer.Text(text.TrimEnd('\n'));
        writer.RawLine("</div>");
    }

    private static async Task RenderPictureAsync(XElement pic, OfficePackage pkg, Dictionary<string, string> rels,
        OfficeHtmlWriter writer, CancellationToken ct)
    {
        var embedId = pic.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed")?.Value;
        if (embedId == null || !rels.TryGetValue(embedId, out var partName)) return;

        var bytes = await pkg.ReadBytesAsync(partName, OfficeLimits.MaxImageBytes, ct).ConfigureAwait(false);
        var dataUri = writer.TryEmbedImage(bytes, partName);
        var style = PositionStyle(pic.Element(P + "spPr")?.Element(A + "xfrm"));
        writer.RawLine(dataUri != null
            ? $"<img src=\"{dataUri}\" style=\"{style}max-width:100%;\">"
            : $"<div style=\"{style}opacity:.6;font-style:italic;\">[image]</div>");
    }

    private static string PositionStyle(XElement? xfrm)
    {
        if (xfrm == null) return "";
        var off = xfrm.Element(A + "off");
        var ext = xfrm.Element(A + "ext");
        var x = EmuToPx(off?.Attribute("x")?.Value);
        var y = EmuToPx(off?.Attribute("y")?.Value);
        var cx = EmuToPx(ext?.Attribute("cx")?.Value);
        var cy = EmuToPx(ext?.Attribute("cy")?.Value);
        return $"position:absolute;left:{x}px;top:{y}px;width:{cx}px;height:{cy}px;";
    }

    // OOXML's ST_PositiveCoordinate is a 64-bit quantity (up to ~27,273,042,316,900 EMU) - int
    // silently overflows for any legitimately large offset/extent well before that, collapsing it
    // to "left:0px" instead of clamping to something visible. long.TryParse plus an explicit clamp
    // to int's range keeps the result usable as a CSS pixel value either way.
    private static int EmuToPx(string? emu) =>
        long.TryParse(emu, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? (int)Math.Clamp(v / 9525, int.MinValue, int.MaxValue)
            : 0;

    private static string RelsPathFor(string partName)
    {
        var slash = partName.LastIndexOf('/');
        var dir = slash >= 0 ? partName[..slash] : "";
        var fileName = slash >= 0 ? partName[(slash + 1)..] : partName;
        return dir.Length == 0 ? $"_rels/{fileName}.rels" : $"{dir}/_rels/{fileName}.rels";
    }
}
