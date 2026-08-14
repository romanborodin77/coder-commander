using System.Threading;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Universal Binary mode - every byte shown as its Latin-1 codepoint 1:1, the closest
/// thing to a raw byte-for-byte view a <see cref="System.Windows.Forms.RichTextBox"/> can safely
/// render. See <see cref="RawByteText.ToLatin1Safe"/> for the exact mapping and why it isn't
/// truly 1:1 (RichTextBox/EDIT-control safety).</summary>
public sealed class BinaryViewerFormat : IViewerFormat
{
    public static readonly BinaryViewerFormat Instance = new();
    private BinaryViewerFormat() { }

    public string Id => "binary";
    public string DisplayNameKey => "View.Binary";
    public string IconKey => "view_binary";
    public IReadOnlyList<string> Extensions => [];
    public ViewerAvailability Availability => ViewerAvailability.Universal;
    public ViewerCapabilities Capabilities => ViewerCapabilities.TextLike;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new BinaryViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new TextViewerContent(ctx);
}

public sealed class BinaryViewerLoader : IViewerLoader
{
    public async Task<ViewerPayload> LoadAsync(ViewerSource source, CancellationToken ct)
    {
        var L = LocalizationService.Current;
        var size = await source.GetSizeAsync(ct).ConfigureAwait(false);

        if (size > ViewerLimits.TextSizeLimit)
            return new ViewerErrorPayload(
                L.GetString("View.TooBigForText", FormatUtils.FormatSize(size), FormatUtils.FormatSize(ViewerLimits.TextSizeLimit)),
                Modal: false);

        var raw = await source.ReadAllBytesAsync(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var text = RawByteText.ToLatin1Safe(raw);
        return new TextPayload(text, L.GetString("View.BinaryMode", FormatUtils.FormatSize(size)));
    }
}
