using System.Threading;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Universal ASCII mode - forces strict printable-ASCII interpretation regardless of
/// the file's real encoding, for when autodetection guesses wrong. See
/// <see cref="RawByteText.ToAsciiPrintable"/> for the exact mapping.</summary>
public sealed class AsciiViewerFormat : IViewerFormat
{
    public static readonly AsciiViewerFormat Instance = new();
    private AsciiViewerFormat() { }

    public string Id => "ascii";
    public string DisplayNameKey => "View.Ascii";
    public string IconKey => "view_text";
    public IReadOnlyList<string> Extensions => [];
    public ViewerAvailability Availability => ViewerAvailability.Universal;
    public ViewerCapabilities Capabilities => ViewerCapabilities.TextLike;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new AsciiViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new TextViewerContent(ctx);
}

public sealed class AsciiViewerLoader : IViewerLoader
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

        var text = RawByteText.ToAsciiPrintable(raw);
        return new TextPayload(text, L.GetString("View.AsciiMode", FormatUtils.FormatSize(size)));
    }
}
