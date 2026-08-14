using System.Xml.Linq;

namespace CoderCommander.Viewers.Office;

/// <summary>XML namespaces shared by every OOXML converter (Word/Sheet/Slides) - the
/// officeDocument relationships namespace (<c>r:id</c>/<c>r:embed</c> attributes) and the OPC
/// package-relationships namespace (<c>_rels/*.rels</c> parts' own root element) are identical
/// across all three formats; each converter's document-specific namespace (<c>w:</c>/<c>s:</c>/
/// <c>p:</c>) stays declared locally in that converter's own file.</summary>
internal static class OoxmlNamespaces
{
    public static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    public static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
}
