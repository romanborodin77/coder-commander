using System.Net;
using System.Xml.Linq;

namespace CoderCommander.Viewers.Office;

/// <summary>
/// Converts a <c>.docx</c> package's <c>word/document.xml</c> body to HTML: paragraphs (heading
/// styles mapped to <c>h1</c>-<c>h6</c>), bold/italic/underline runs, hyperlinks, tables, and
/// embedded images (via <c>a:blip r:embed</c> resolved through the part's own relationships).
///
/// <para>Deliberately not attempted: real list numbering (a <c>w:numPr</c> paragraph always
/// renders as a plain bulleted <c>&lt;li&gt;</c> regardless of whether <c>numbering.xml</c> says
/// it's actually numbered/lettered/roman - resolving the full numbering definition graph is a
/// second document parse for a detail most previews don't need); nested tables inside a table
/// cell; and page headers/footers/footnotes (no page concept exists once this is one continuously
/// scrolling HTML document).</para>
/// </summary>
internal static class OoxmlWordConverter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = OoxmlNamespaces.Relationships;
    private static readonly XNamespace A = OoxmlNamespaces.Drawing;
    private static readonly XNamespace PackageRel = OoxmlNamespaces.PackageRelationships;

    private const string DocumentPart = "word/document.xml";
    private const string RelsPart = "word/_rels/document.xml.rels";

    public static async Task<string> ConvertAsync(OfficePackage pkg, CancellationToken ct)
    {
        var doc = pkg.ReadXml(DocumentPart) ?? throw new InvalidDataException("word/document.xml not found.");
        var body = doc.Root?.Element(W + "body") ?? throw new InvalidDataException("Document body not found.");
        var rels = LoadRelationships(pkg, RelsPart, DocumentPart);

        var writer = new OfficeHtmlWriter();
        var inList = false;

        foreach (var element in body.Elements())
        {
            ct.ThrowIfCancellationRequested();
            if (element.Name == W + "p")
            {
                var isListItem = element.Element(W + "pPr")?.Element(W + "numPr") != null;
                if (isListItem != inList)
                {
                    writer.RawLine(inList ? "</ul>" : "<ul>");
                    inList = isListItem;
                }
                await RenderParagraphAsync(element, pkg, rels, writer, isListItem, ct).ConfigureAwait(false);
            }
            else if (element.Name == W + "tbl")
            {
                if (inList) { writer.RawLine("</ul>"); inList = false; }
                await RenderTableAsync(element, pkg, rels, writer, ct).ConfigureAwait(false);
            }
        }
        if (inList) writer.RawLine("</ul>");

        return writer.Build();
    }

    private static async Task RenderParagraphAsync(XElement p, OfficePackage pkg, Dictionary<string, string> rels,
        OfficeHtmlWriter writer, bool isListItem, CancellationToken ct)
    {
        var styleId = p.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
        var tag = isListItem ? "li" : HeadingTag(styleId);

        writer.Raw($"<{tag}>");
        await RenderInlineContentAsync(p.Elements(), pkg, rels, writer, ct).ConfigureAwait(false);
        writer.RawLine($"</{tag}>");
    }

    private static string HeadingTag(string? styleId) => styleId switch
    {
        "Title" => "h1",
        "Heading1" => "h1",
        "Heading2" => "h2",
        "Heading3" => "h3",
        "Heading4" => "h4",
        "Heading5" => "h5",
        "Heading6" or "Heading7" or "Heading8" or "Heading9" => "h6",
        _ => "p",
    };

    private static async Task RenderInlineContentAsync(IEnumerable<XElement> nodes, OfficePackage pkg,
        Dictionary<string, string> rels, OfficeHtmlWriter writer, CancellationToken ct)
    {
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            if (node.Name == W + "r")
            {
                await RenderRunAsync(node, pkg, rels, writer, ct).ConfigureAwait(false);
            }
            else if (node.Name == W + "hyperlink")
            {
                var relId = node.Attribute(R + "id")?.Value;
                var href = relId != null && rels.TryGetValue(relId, out var target) ? target : null;
                if (href != null) writer.Raw($"<a href=\"{WebUtility.HtmlEncode(href)}\">");
                await RenderInlineContentAsync(node.Elements(), pkg, rels, writer, ct).ConfigureAwait(false);
                if (href != null) writer.Raw("</a>");
            }
        }
    }

    private static async Task RenderRunAsync(XElement run, OfficePackage pkg, Dictionary<string, string> rels,
        OfficeHtmlWriter writer, CancellationToken ct)
    {
        var rPr = run.Element(W + "rPr");
        var openTags = new List<string>();
        if (rPr?.Element(W + "b") != null) openTags.Add("b");
        if (rPr?.Element(W + "i") != null) openTags.Add("i");
        if (rPr?.Element(W + "u") != null) openTags.Add("u");
        foreach (var t in openTags) writer.Raw($"<{t}>");

        foreach (var node in run.Elements())
        {
            ct.ThrowIfCancellationRequested();
            if (node.Name == W + "t") writer.Text(node.Value);
            else if (node.Name == W + "br" || node.Name == W + "cr") writer.Raw("<br>");
            else if (node.Name == W + "tab") writer.Raw("&emsp;");
            else if (node.Name == W + "drawing") await RenderDrawingAsync(node, pkg, rels, writer, ct).ConfigureAwait(false);
        }

        openTags.Reverse();
        foreach (var t in openTags) writer.Raw($"</{t}>");
    }

    private static async Task RenderDrawingAsync(XElement drawing, OfficePackage pkg, Dictionary<string, string> rels,
        OfficeHtmlWriter writer, CancellationToken ct)
    {
        var embedId = drawing.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed")?.Value;
        if (embedId == null || !rels.TryGetValue(embedId, out var partName)) return;

        var bytes = await pkg.ReadBytesAsync(partName, OfficeLimits.MaxImageBytes, ct).ConfigureAwait(false);
        var dataUri = writer.TryEmbedImage(bytes, partName);
        writer.Raw(dataUri != null
            ? $"<img src=\"{dataUri}\" style=\"max-width:100%;\">"
            : "<span style=\"opacity:.6;font-style:italic;\">[image]</span>");
    }

    private static async Task RenderTableAsync(XElement tbl, OfficePackage pkg, Dictionary<string, string> rels,
        OfficeHtmlWriter writer, CancellationToken ct)
    {
        writer.RawLine("<table>");
        foreach (var row in tbl.Elements(W + "tr"))
        {
            writer.RawLine("<tr>");
            foreach (var cell in row.Elements(W + "tc"))
            {
                writer.Raw("<td>");
                foreach (var p in cell.Elements(W + "p"))
                {
                    await RenderInlineContentAsync(p.Elements(), pkg, rels, writer, ct).ConfigureAwait(false);
                    writer.Raw("<br>");
                }
                writer.Raw("</td>");
            }
            writer.RawLine("</tr>");
        }
        writer.RawLine("</table>");
    }

    /// <summary>Reads a <c>_rels/*.rels</c> part and returns relationship id → resolved target.
    /// External targets (hyperlinks with <c>TargetMode="External"</c>) are kept as literal URLs;
    /// internal targets are resolved and safety-checked via
    /// <see cref="OfficePackage.ResolveRelationshipTarget"/> - an id whose target fails that check
    /// is simply absent from the result, so anything referencing it (an image, a hyperlink) is
    /// silently skipped rather than followed.</summary>
    internal static Dictionary<string, string> LoadRelationships(OfficePackage pkg, string relsPart, string referencingPart)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var doc = pkg.ReadXml(relsPart);
        if (doc?.Root == null) return result;

        foreach (var r in doc.Root.Elements(PackageRel + "Relationship"))
        {
            var id = r.Attribute("Id")?.Value;
            var target = r.Attribute("Target")?.Value;
            if (id == null || target == null) continue;

            if (string.Equals(r.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            {
                result[id] = target;
            }
            else
            {
                var resolved = OfficePackage.ResolveRelationshipTarget(referencingPart, target);
                if (resolved != null) result[id] = resolved;
            }
        }
        return result;
    }
}
