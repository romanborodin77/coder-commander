using System.Net;
using System.Xml.Linq;

namespace CoderCommander.Viewers.Office;

/// <summary>
/// Converts an <c>.odt</c> package's <c>content.xml</c> <c>office:text</c> body to HTML:
/// paragraphs, headings (<c>text:h</c>'s own <c>text:outline-level</c>), lists, tables, and
/// embedded images (<c>draw:frame</c>/<c>draw:image</c> - the target is a package-relative path
/// directly, no relationship indirection the way OOXML needs, so
/// <see cref="OfficePackage.ResolveRelationshipTarget"/> is called with an empty referencing part
/// to mean "relative to the package root").
///
/// <para>Same scope cuts as <c>OoxmlWordConverter</c>: list markers are always a plain bullet
/// regardless of the list style's real numbering, and nested tables aren't unwrapped.</para>
/// </summary>
internal static class OdfTextConverter
{
    private static readonly XNamespace Office = OdfNamespaces.Office;
    private static readonly XNamespace Text = OdfNamespaces.Text;
    private static readonly XNamespace Table = OdfNamespaces.Table;
    private static readonly XNamespace Draw = OdfNamespaces.Draw;
    private static readonly XNamespace XLink = OdfNamespaces.XLink;

    public static async Task<string> ConvertAsync(OfficePackage pkg, CancellationToken ct)
    {
        var doc = pkg.ReadXml(OdfNamespaces.ContentPart) ?? throw new InvalidDataException("content.xml not found.");
        var body = doc.Root?.Element(Office + "body")?.Element(Office + "text")
                   ?? throw new InvalidDataException("Document text body not found.");

        var writer = new OfficeHtmlWriter();
        foreach (var element in body.Elements())
        {
            ct.ThrowIfCancellationRequested();
            await RenderBlockAsync(element, pkg, writer, 0, ct).ConfigureAwait(false);
        }
        return writer.Build();
    }

    /// <summary>Schemes a rendered <c>&lt;a href&gt;</c> is allowed to carry. HTML-escaping (already
    /// applied via <see cref="WebUtility.HtmlEncode"/>) prevents attribute breakout, but says
    /// nothing about the scheme itself - an untrusted document could otherwise get
    /// <c>javascript:</c>/<c>vbscript:</c>/<c>data:text/html</c> rendered as a clickable link.
    /// Anything outside this list is still shown, just as plain text rather than a link.</summary>
    private static bool IsSafeLinkScheme(string href) =>
        href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        href.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
        href.StartsWith('#');

    private static async Task RenderBlockAsync(XElement element, OfficePackage pkg, OfficeHtmlWriter writer, int depth, CancellationToken ct)
    {
        // See OfficeLimits.MaxNestingDepth: this recursion is real stack depth, and a
        // StackOverflowException cannot be caught - stop descending well before that, rendering the
        // remainder as nothing rather than crashing the whole process on a hostile document.
        if (depth > OfficeLimits.MaxNestingDepth) return;

        if (element.Name == Text + "h")
        {
            var level = int.TryParse(element.Attribute(Text + "outline-level")?.Value, out var l) ? Math.Clamp(l, 1, 6) : 1;
            writer.Raw($"<h{level}>");
            await RenderInlineAsync(element, pkg, writer, depth + 1, ct).ConfigureAwait(false);
            writer.RawLine($"</h{level}>");
        }
        else if (element.Name == Text + "p")
        {
            writer.Raw("<p>");
            await RenderInlineAsync(element, pkg, writer, depth + 1, ct).ConfigureAwait(false);
            writer.RawLine("</p>");
        }
        else if (element.Name == Text + "list")
        {
            writer.RawLine("<ul>");
            foreach (var item in element.Elements(Text + "list-item"))
            {
                writer.Raw("<li>");
                foreach (var child in item.Elements())
                    await RenderBlockAsync(child, pkg, writer, depth + 1, ct).ConfigureAwait(false);
                writer.RawLine("</li>");
            }
            writer.RawLine("</ul>");
        }
        else if (element.Name == Table + "table")
        {
            await RenderTableAsync(element, pkg, writer, ct).ConfigureAwait(false);
        }
    }

    private static async Task RenderInlineAsync(XElement container, OfficePackage pkg, OfficeHtmlWriter writer, int depth, CancellationToken ct)
    {
        if (depth > OfficeLimits.MaxNestingDepth) return;

        foreach (var node in container.Nodes())
        {
            ct.ThrowIfCancellationRequested();
            if (node is XText textNode)
            {
                writer.Text(textNode.Value);
            }
            else if (node is XElement el)
            {
                if (el.Name == Text + "span")
                {
                    await RenderInlineAsync(el, pkg, writer, depth + 1, ct).ConfigureAwait(false);
                }
                else if (el.Name == Text + "a")
                {
                    var href = el.Attribute(XLink + "href")?.Value;
                    var linkable = href != null && IsSafeLinkScheme(href);
                    if (linkable) writer.Raw($"<a href=\"{WebUtility.HtmlEncode(href)}\">");
                    await RenderInlineAsync(el, pkg, writer, depth + 1, ct).ConfigureAwait(false);
                    if (linkable) writer.Raw("</a>");
                }
                else if (el.Name == Text + "line-break")
                {
                    writer.Raw("<br>");
                }
                else if (el.Name == Text + "tab")
                {
                    writer.Raw("&emsp;");
                }
                else if (el.Name == Draw + "frame")
                {
                    await RenderImageFrameAsync(el, pkg, writer, ct).ConfigureAwait(false);
                }
            }
        }
    }

    internal static async Task RenderImageFrameAsync(XElement frame, OfficePackage pkg, OfficeHtmlWriter writer, CancellationToken ct)
    {
        var href = frame.Element(Draw + "image")?.Attribute(XLink + "href")?.Value;
        if (href == null) return;

        var resolved = OfficePackage.ResolveRelationshipTarget("", href);
        if (resolved == null) return;

        var bytes = await pkg.ReadBytesAsync(resolved, OfficeLimits.MaxImageBytes, ct).ConfigureAwait(false);
        var dataUri = writer.TryEmbedImage(bytes, resolved);
        writer.Raw(dataUri != null
            ? $"<img src=\"{dataUri}\" style=\"max-width:100%;\">"
            : "<span style=\"opacity:.6;font-style:italic;\">[image]</span>");
    }

    private static async Task RenderTableAsync(XElement table, OfficePackage pkg, OfficeHtmlWriter writer, CancellationToken ct)
    {
        writer.RawLine("<table>");
        foreach (var row in table.Elements(Table + "table-row"))
        {
            writer.Raw("<tr>");
            foreach (var cell in row.Elements(Table + "table-cell"))
            {
                writer.Raw("<td>");
                foreach (var p in cell.Elements(Text + "p"))
                {
                    await RenderInlineAsync(p, pkg, writer, 0, ct).ConfigureAwait(false);
                    writer.Raw("<br>");
                }
                writer.Raw("</td>");
            }
            writer.RawLine("</tr>");
        }
        writer.RawLine("</table>");
    }
}
