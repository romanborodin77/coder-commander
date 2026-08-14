using System.Xml.Linq;

namespace CoderCommander.Viewers.Office;

/// <summary>XML namespaces shared by every ODF converter (Text/Sheet/Slides) - unlike OOXML, all
/// three ODF document kinds keep their entire content in one <c>content.xml</c> part using the
/// same namespace set, just under a different <c>office:body</c> child element
/// (<c>office:text</c>/<c>office:spreadsheet</c>/<c>office:presentation</c>).</summary>
internal static class OdfNamespaces
{
    public static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    public static readonly XNamespace Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    public static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    public const string ContentPart = "content.xml";
}
