using System.Globalization;
using System.Text;
using System.Threading;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Universal hex-dump mode - offset/hex/ASCII columns, unchanged from the pre-rewrite
/// <c>ViewerForm.LoadFileCore</c>'s hex branch other than reading through
/// <see cref="ViewerSource.ReadPrefixAsync"/> instead of a direct <c>FileStream</c>.</summary>
public sealed class HexViewerFormat : IViewerFormat
{
    public static readonly HexViewerFormat Instance = new();
    private HexViewerFormat() { }

    public string Id => "hex";
    public string DisplayNameKey => "View.Hex";
    public string IconKey => "view_hex";
    public IReadOnlyList<string> Extensions => [];
    public ViewerAvailability Availability => ViewerAvailability.Universal;
    public ViewerCapabilities Capabilities => ViewerCapabilities.TextLike;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new HexViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new TextViewerContent(ctx);
}

public sealed class HexViewerLoader : IViewerLoader
{
    public async Task<ViewerPayload> LoadAsync(ViewerSource source, CancellationToken ct)
    {
        var L = LocalizationService.Current;
        var size = await source.GetSizeAsync(ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        if (size > ViewerLimits.HexMaxBytes)
        {
            sb.AppendLine(L.GetString("View.HexTruncated", FormatUtils.FormatSize(ViewerLimits.HexMaxBytes), FormatUtils.FormatSize(size)));
            sb.AppendLine();
        }

        var bytes = await source.ReadPrefixAsync((int)Math.Min(size, ViewerLimits.HexMaxBytes), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var limit = bytes.Length;

        for (var i = 0; i < limit; i += ViewerLimits.HexBytesPerRow)
        {
            if ((i & 0xFFF) == 0) ct.ThrowIfCancellationRequested(); // periodic check on a large dump

            sb.Append(CultureInfo.InvariantCulture, $"{i:X8}  ");
            for (var j = 0; j < ViewerLimits.HexBytesPerRow; j++)
            {
                if (i + j < limit)
                    sb.Append(CultureInfo.InvariantCulture, $"{bytes[i + j]:X2} ");
                else
                    sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (var j = 0; j < ViewerLimits.HexBytesPerRow && i + j < limit; j++)
            {
                var c = bytes[i + j];
                sb.Append(c >= 0x20 && c < 0x7F ? (char)c : '.');
            }
            sb.AppendLine();
        }

        if (size > ViewerLimits.HexMaxBytes)
            sb.AppendLine(CultureInfo.InvariantCulture, $"... ({FormatUtils.FormatSize(size - ViewerLimits.HexMaxBytes)} more)");

        return new TextPayload(sb.ToString(), L.GetString("View.HexMode", FormatUtils.FormatSize(size)));
    }
}
