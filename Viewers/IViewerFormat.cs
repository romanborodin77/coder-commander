namespace CoderCommander.Viewers;

/// <summary>
/// Descriptor + factory for one viewer format. Registered once at startup with
/// <see cref="ViewerFormatRegistry.Register"/>, mirroring <c>Archives.IArchiveFormat</c> →
/// <c>ArchiveFormatRegistry</c> - everything that needs to go from a file to "what can display
/// this and how" goes through the registry rather than a hardcoded mode enum, which is exactly
/// what let the pre-rewrite <c>ViewerForm</c> only ever know about Text/Hex/Image.
/// </summary>
public interface IViewerFormat
{
    /// <summary>Stable identifier, also the <c>AppSettings.ViewerLastMode</c> value for
    /// <see cref="ViewerAvailability.Universal"/> formats ("text", "ascii", "binary", "hex").
    /// Not localized, never shown to the user directly.</summary>
    string Id { get; }

    /// <summary>Localization key for the format's toolbar button label.</summary>
    string DisplayNameKey { get; }

    /// <summary><c>ToolbarIcons</c> key for the format's toolbar button.</summary>
    string IconKey { get; }

    /// <summary>Recognized extensions, longest-match first (e.g. ".tar.gz" before ".gz") - same
    /// convention as <c>IArchiveFormat.Extensions</c>. Empty for a <see cref="ViewerAvailability.Universal"/>
    /// format, which every file offers regardless of extension.</summary>
    IReadOnlyList<string> Extensions { get; }

    ViewerAvailability Availability { get; }

    ViewerCapabilities Capabilities { get; }

    /// <summary>Sniffs a leading chunk of the file for this format's signature - the fallback
    /// when extension-based detection doesn't resolve a format. Always <c>false</c> for a
    /// <see cref="ViewerAvailability.Universal"/> format (nothing to sniff for; extension
    /// matching alone decides <see cref="ViewerAvailability.Matched"/> formats today).</summary>
    bool MatchesSignature(ReadOnlySpan<byte> header);

    /// <summary>Creates a fresh, stateless loader for one load. Called once per
    /// <c>LoadFileAsync</c> run, never cached - unlike <see cref="CreateContent"/>, there is
    /// nothing worth keeping alive between loads on the pool-thread side.</summary>
    IViewerLoader CreateLoader();

    /// <summary>Creates this format's content. Called at most once per <c>ViewerForm</c> window
    /// (cached thereafter by <see cref="Id"/>) - see <see cref="IViewerContent"/>'s own doc
    /// comment for why formats don't share content instances in this phase.</summary>
    IViewerContent CreateContent(ViewerContentContext ctx);
}
