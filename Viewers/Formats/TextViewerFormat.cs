using System.Text;
using System.Threading;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Universal text mode - full encoding autodetection (BOM, valid-UTF-8 probe, system
/// ANSI fallback), the "smart" default. Directly ported from the pre-rewrite
/// <c>ViewerForm.LoadFileCore</c>'s text branch, just reading through <see cref="ViewerSource"/>
/// instead of <c>File.ReadAllBytes</c>.</summary>
public sealed class TextViewerFormat : IViewerFormat
{
    public static readonly TextViewerFormat Instance = new();
    private TextViewerFormat() { }

    public string Id => "text";
    public string DisplayNameKey => "View.Text";
    public string IconKey => "view_text";
    public IReadOnlyList<string> Extensions => [];
    public ViewerAvailability Availability => ViewerAvailability.Universal;
    public ViewerCapabilities Capabilities => ViewerCapabilities.TextLike;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new TextViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new TextViewerContent(ctx, supportsEncoding: true);
}

public sealed class TextViewerLoader : IViewerLoader
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

        // Some providers (FTP in particular) report an unreliable or absent size, so the up-front
        // check above may pass even for a large file. Guard against OOM by checking the actual size.
        if (raw.Length > ViewerLimits.TextSizeLimit)
            return new ViewerErrorPayload(
                L.GetString("View.TooBigForText", FormatUtils.FormatSize(raw.Length), FormatUtils.FormatSize(ViewerLimits.TextSizeLimit)),
                Modal: false);

        Encoding encoding;
        int preambleLength;
        var overrideEncoding = EncodingCatalog.TryResolve(SettingsService.Load().ViewerEncodingOverride);
        if (overrideEncoding != null)
        {
            encoding = overrideEncoding;
            var preamble = encoding.GetPreamble();
            preambleLength = preamble.Length > 0 && raw.Length >= preamble.Length && raw.AsSpan(0, preamble.Length).SequenceEqual(preamble)
                ? preamble.Length
                : 0;
        }
        else
        {
            encoding = TextEncodingDetector.Detect(raw, out preambleLength);
        }

        var text = encoding.GetString(raw, preambleLength, raw.Length - preambleLength);
        return new TextPayload(text, L.GetString("View.TextMode", FormatUtils.FormatSize(size), encoding.EncodingName));
    }
}
