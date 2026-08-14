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
    private long _totalImageBytes;

    public void Raw(string html) => _body.Append(html);

    public void Text(string text) => _body.Append(WebUtility.HtmlEncode(text));

    public void RawLine(string html) => _body.Append(html).Append('\n');

    /// <summary>Converts <paramref name="bytes"/> into a <c>data:</c> URI, or null when the image
    /// should be skipped: too large individually, over the document's running image budget, or an
    /// unsupported format (EMF/WMF - GDI-family vector formats no browser decodes; callers render
    /// a labeled placeholder instead of a broken &lt;img&gt;, per the plan's own decision).</summary>
    public string? TryEmbedImage(byte[]? bytes, string partName)
    {
        if (bytes == null || bytes.Length == 0) return null;
        if (bytes.LongLength > OfficeLimits.MaxImageBytes) return null;
        if (_totalImageBytes + bytes.LongLength > OfficeLimits.MaxTotalImageBytes) return null;

        var mime = MimeFromExtension(partName);
        if (mime == null) return null;

        _totalImageBytes += bytes.LongLength;
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
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

    public string Build() => ViewerHtmlTemplate.WrapDocument(_body.ToString());
}
