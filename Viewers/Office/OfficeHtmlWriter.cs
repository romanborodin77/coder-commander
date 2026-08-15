using System.Net;
using System.Text;

namespace CoderCommander.Viewers.Office;

/// <summary>
/// Accumulates one converter's output as an HTML fragment and turns embedded images into
/// <c>data:</c> URIs - no temp files, no second virtual-host mapping, works with
/// <c>NavigateToString</c>-free navigation the same way <c>MarkdownViewerContent</c> already
/// writes its rendered file to the package's own isolated temp folder. Shared by every
/// OOXML/ODF converter so the <see cref="OfficeLimits.MaxImageBytes"/>/
/// <see cref="OfficeLimits.MaxTotalImageBytes"/> budget is enforced in one place rather than once
/// per format.
/// </summary>
internal sealed class OfficeHtmlWriter
{
    private readonly StringBuilder _body = new();
    private readonly OfficeImageBudget _imageBudget;
    private bool _truncated;

    public OfficeHtmlWriter(OfficeImageBudget? sharedImageBudget = null)
    {
        _imageBudget = sharedImageBudget ?? new OfficeImageBudget();
    }

    public void Raw(string html)
    {
        if (_truncated) return;
        if (_body.Length + html.Length > OfficeLimits.MaxOutputChars) { _truncated = true; return; }
        _body.Append(html);
    }

    public void Text(string text)
    {
        if (_truncated) return;
        // Encoded length can exceed text.Length (e.g. every char becomes "&amp;"), but text.Length
        // is a safe/cheap pre-check - a caller feeding megabytes of "&" is still capped by the next
        // Raw()/Text() call once _body.Length itself crosses the ceiling.
        if (_body.Length + text.Length > OfficeLimits.MaxOutputChars) { _truncated = true; return; }
        _body.Append(WebUtility.HtmlEncode(text));
    }

    public void RawLine(string html) => Raw(html + "\n");

    /// <summary>Converts <paramref name="bytes"/> into a <c>data:</c> URI, or null when the image
    /// should be skipped: too large individually, over the running image budget (shared across
    /// every page of a multi-page document via <paramref name="sharedImageBudget"/> passed to the
    /// constructor - a single writer-per-slide would otherwise reset to a fresh 64MB budget on
    /// every slide, multiplying the effective ceiling by the slide count), or an unsupported format
    /// (EMF/WMF - GDI-family vector formats no browser decodes; callers render a labeled
    /// placeholder instead of a broken &lt;img&gt;, per the plan's own decision). The same part
    /// referenced from multiple pages is charged to the budget once and reused from cache after.</summary>
    public string? TryEmbedImage(byte[]? bytes, string partName)
    {
        if (_imageBudget.Cache.TryGetValue(partName, out var cached)) return cached;
        if (bytes == null || bytes.Length == 0) return _imageBudget.Cache[partName] = null;
        if (bytes.LongLength > OfficeLimits.MaxImageBytes) return _imageBudget.Cache[partName] = null;
        if (_imageBudget.TotalBytes + bytes.LongLength > OfficeLimits.MaxTotalImageBytes) return _imageBudget.Cache[partName] = null;

        var mime = MimeFromExtension(partName);
        if (mime == null) return _imageBudget.Cache[partName] = null;

        _imageBudget.TotalBytes += bytes.LongLength;
        return _imageBudget.Cache[partName] = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string? MimeFromExtension(string partName) =>
        Path.GetExtension(partName).ToUpperInvariant() switch
        {
            ".PNG" => "image/png",
            ".JPG" or ".JPEG" => "image/jpeg",
            ".GIF" => "image/gif",
            ".BMP" => "image/bmp",
            ".TIFF" or ".TIF" => "image/tiff",
            _ => null, // EMF/WMF and anything else - unsupported, see TryEmbedImage's doc comment
        };

    public string Build()
    {
        if (_truncated) _body.Append("\n<p style=\"opacity:.6;font-style:italic;\">[…document truncated - too large to display in full…]</p>");
        return ViewerHtmlTemplate.WrapDocument(_body.ToString());
    }
}

/// <summary>Image-embedding state shared across every <see cref="OfficeHtmlWriter"/> instance
/// rendering the same logical document - see <see cref="OfficeHtmlWriter.TryEmbedImage"/>. A
/// single-page format (Word/Sheet) just lets each writer default to its own budget; a multi-page
/// format (Slides) constructs one of these per document and passes it to every per-slide
/// writer.</summary>
internal sealed class OfficeImageBudget
{
    public long TotalBytes;
    public readonly Dictionary<string, string?> Cache = new(StringComparer.Ordinal);
}
