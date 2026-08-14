using System.Xml;
using System.Xml.Linq;

namespace CoderCommander.Viewers.Xml;

/// <summary>
/// The one place every Office-document converter parses XML from - never
/// <c>XDocument.Load(stream)</c> directly, which uses a permissive default <see cref="XmlReader"/>.
/// A <c>.docx</c>/<c>.xlsx</c>/<c>.pptx</c>/<c>.odt</c>/<c>.ods</c>/<c>.odp</c> is an untrusted file
/// by construction (it arrived via F3 on whatever the user is browsing, same trust level as any
/// other file this app previews), and <c>AnalysisModeSecurity=All</c> flags exactly this class of
/// XXE/billion-laughs surface (CA3075) if it's ever bypassed.
/// </summary>
internal static class SafeXml
{
    /// <summary>A reader that refuses DOCTYPE declarations outright (<see cref="DtdProcessing.Prohibit"/>
    /// throws <see cref="XmlException"/> the moment one appears, before any entity inside it could
    /// be defined), never resolves an external entity or DTD (<see cref="XmlReaderSettings.XmlResolver"/>
    /// = null), and caps entity-expansion output at zero characters as a second, independent layer -
    /// belt and suspenders, since DTD prohibition alone already removes the only place OOXML/ODF XML
    /// could declare a custom entity.</summary>
    public static XmlReader CreateReader(Stream stream) => XmlReader.Create(stream, new XmlReaderSettings
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersFromEntities = 0,
        CloseInput = false,
    });

    /// <summary>Loads a full <see cref="XDocument"/> through <see cref="CreateReader"/> - safe
    /// despite building an in-memory DOM, because the hardening lives in the reader underneath it,
    /// not in how the result is subsequently consumed.</summary>
    public static XDocument LoadSafe(Stream stream)
    {
        using var reader = CreateReader(stream);
        return XDocument.Load(reader, LoadOptions.None);
    }
}
