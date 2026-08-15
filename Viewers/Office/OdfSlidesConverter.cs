using System.Globalization;
using System.Xml.Linq;

namespace CoderCommander.Viewers.Office;

/// <summary>
/// Converts an <c>.odp</c> package's <c>content.xml</c> <c>office:presentation</c> to one HTML
/// page per <c>draw:page</c> (slide). Unlike OOXML's <c>.pptx</c>, ODF's <c>svg:x</c>/<c>svg:y</c>/
/// <c>svg:width</c>/<c>svg:height</c> carry an explicit unit suffix (<c>cm</c>/<c>mm</c>/<c>in</c>/
/// <c>pt</c>/<c>pc</c>, or bare numbers meaning px) rather than OOXML's fixed EMU - see
/// <see cref="ParseLength"/> for the conversion table, all normalized to px at 96 px/inch.
/// </summary>
internal static class OdfSlidesConverter
{
    private static readonly XNamespace Office = OdfNamespaces.Office;
    private static readonly XNamespace Text = OdfNamespaces.Text;
    private static readonly XNamespace Draw = OdfNamespaces.Draw;
    private static readonly XNamespace Svg = OdfNamespaces.Svg;
    private static readonly XNamespace XLink = OdfNamespaces.XLink;

    public static async Task<List<OfficeDocumentPage>> ConvertAsync(OfficePackage pkg, CancellationToken ct)
    {
        var doc = pkg.ReadXml(OdfNamespaces.ContentPart) ?? throw new InvalidDataException("content.xml not found.");
        var presentation = doc.Root?.Element(Office + "body")?.Element(Office + "presentation")
                            ?? throw new InvalidDataException("Presentation body not found.");

        var pages = new List<OfficeDocumentPage>();
        // Shared across every page - see OfficeHtmlWriter.TryEmbedImage's doc comment.
        var imageBudget = new OfficeImageBudget();
        var index = 0;
        foreach (var page in presentation.Elements(Draw + "page"))
        {
            ct.ThrowIfCancellationRequested();
            index++;
            var name = page.Attribute(Draw + "name")?.Value ?? index.ToString(CultureInfo.InvariantCulture);
            pages.Add(new OfficeDocumentPage(name, await RenderPageAsync(page, pkg, imageBudget, ct).ConfigureAwait(false)));
        }

        if (pages.Count == 0) throw new InvalidDataException("Presentation has no readable slides.");
        return pages;
    }

    private static async Task<string> RenderPageAsync(XElement page, OfficePackage pkg, OfficeImageBudget imageBudget, CancellationToken ct)
    {
        var writer = new OfficeHtmlWriter(imageBudget);
        writer.RawLine("<div style=\"position:relative;min-height:400px;\">");

        foreach (var frame in page.Elements(Draw + "frame"))
        {
            ct.ThrowIfCancellationRequested();
            await RenderFrameAsync(frame, pkg, writer, ct).ConfigureAwait(false);
        }

        writer.RawLine("</div>");
        return writer.Build();
    }

    private static async Task RenderFrameAsync(XElement frame, OfficePackage pkg, OfficeHtmlWriter writer, CancellationToken ct)
    {
        var style = PositionStyle(frame);

        var textBox = frame.Element(Draw + "text-box");
        if (textBox != null)
        {
            var text = string.Concat(textBox.Elements(Text + "p").Select(p => p.Value + "\n"));
            if (string.IsNullOrWhiteSpace(text)) return;
            writer.Raw($"<div style=\"{style}white-space:pre-wrap;\">");
            writer.Text(text.TrimEnd('\n'));
            writer.RawLine("</div>");
            return;
        }

        var href = frame.Element(Draw + "image")?.Attribute(XLink + "href")?.Value;
        if (href == null) return;

        var resolved = OfficePackage.ResolveRelationshipTarget("", href);
        if (resolved == null) return;

        var bytes = await pkg.ReadBytesAsync(resolved, OfficeLimits.MaxImageBytes, ct).ConfigureAwait(false);
        var dataUri = writer.TryEmbedImage(bytes, resolved);
        writer.RawLine(dataUri != null
            ? $"<img src=\"{dataUri}\" style=\"{style}max-width:100%;\">"
            : $"<div style=\"{style}opacity:.6;font-style:italic;\">[image]</div>");
    }

    private static string PositionStyle(XElement frame)
    {
        var x = ParseLength(frame.Attribute(Svg + "x")?.Value);
        var y = ParseLength(frame.Attribute(Svg + "y")?.Value);
        var w = ParseLength(frame.Attribute(Svg + "width")?.Value);
        var h = ParseLength(frame.Attribute(Svg + "height")?.Value);
        if (x == 0 && y == 0 && w == 0 && h == 0) return "";

        return string.Create(CultureInfo.InvariantCulture,
            $"position:absolute;left:{x:F0}px;top:{y:F0}px;width:{w:F0}px;height:{h:F0}px;");
    }

    /// <summary>Splits a leading numeric value from its trailing unit suffix and converts to px
    /// at 96 px/inch - the same fixed ratio <c>OoxmlSlidesConverter.EmuToPx</c> uses, just starting
    /// from a different native unit per ODF's own length syntax instead of EMU.</summary>
    private static double ParseLength(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var span = value.AsSpan().Trim();
        var i = 0;
        while (i < span.Length && (char.IsAsciiDigit(span[i]) || span[i] is '.' or '-' or '+')) i++;
        if (i == 0 || !double.TryParse(span[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return 0;
        // double.TryParse with NumberStyles.Float accepts a value like a 400-digit literal as
        // double.PositiveInfinity on .NET Core 3.0+ - left unchecked, PositionStyle's "{x:F0}"
        // formats that as the literal string "Infinity", producing invalid CSS ("left:Infinitypx")
        // that silently drops the whole declaration rather than just misplacing the shape.
        if (!double.IsFinite(number)) return 0;

        return span[i..].Trim().ToString().ToUpperInvariant() switch
        {
            "CM" => number * 96.0 / 2.54,
            "MM" => number * 96.0 / 25.4,
            "IN" => number * 96.0,
            "PT" => number * 96.0 / 72.0,
            "PC" => number * 96.0 / 6.0,
            _ => number, // "px", empty, or unrecognized - treated as already-px
        };
    }
}
