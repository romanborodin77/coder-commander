using System.Threading;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>The one <see cref="ViewerAvailability.Matched"/> format in this phase - everything
/// else (CSV, Markdown, HTML, PDF, media, Office documents) arrives in later phases.</summary>
public sealed class ImageViewerFormat : IViewerFormat
{
    public static readonly ImageViewerFormat Instance = new();
    private ImageViewerFormat() { }

    public string Id => "image";
    public string DisplayNameKey => "View.Image";
    public string IconKey => "view";

    // Same extension list as the pre-rewrite ViewerForm.IsImageFile - SVG deliberately excluded,
    // GDI+ has never decoded it, and it's genuinely readable as plain-text XML instead.
    public IReadOnlyList<string> Extensions =>
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".webp", ".tiff"];

    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.Zoom | ViewerCapabilities.Rotate;

    public bool MatchesSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return true; // PNG

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return true; // JPEG

        if (header.Length >= 4 && header[0] == 'G' && header[1] == 'I' && header[2] == 'F' && header[3] == '8')
            return true; // GIF87a/GIF89a

        if (header.Length >= 2 && header[0] == 'B' && header[1] == 'M')
            return true; // BMP

        return false;
    }

    public IViewerLoader CreateLoader() => new ImageViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new ImageViewerContent(ctx);
}

public sealed class ImageViewerLoader : IViewerLoader
{
    public async Task<ViewerPayload> LoadAsync(ViewerSource source, CancellationToken ct)
    {
        var L = LocalizationService.Current;
        var size = await source.GetSizeAsync(ct).ConfigureAwait(false);

        if (size > ViewerLimits.ImageMaxFileBytes)
            return new ViewerErrorPayload(
                L.GetString("View.ImageTooBig", FormatUtils.FormatSize(size), FormatUtils.FormatSize(ViewerLimits.ImageMaxFileBytes)),
                Modal: true);

        byte[] raw;
        try
        {
            raw = await source.ReadAllBytesAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException)
        {
            return new ViewerErrorPayload(L.GetString("View.FileNotFound"), Modal: true);
        }

        ct.ThrowIfCancellationRequested();

        try
        {
            using var ms = new MemoryStream(raw);
            using var decoded = Image.FromStream(ms);

            // GDI+ decode has no cancellation-aware overload - it runs to completion regardless
            // of ct. This is the earliest point cancellation can be honored.
            ct.ThrowIfCancellationRequested();

            if ((long)decoded.Width * decoded.Height > ViewerLimits.ImageMaxPixels)
            {
                return new ViewerErrorPayload(
                    L.GetString("View.ImageTooBig", $"{decoded.Width}x{decoded.Height}px", $"{ViewerLimits.ImageMaxPixels:N0}px"),
                    Modal: true);
            }

            // Detach from the MemoryStream (which is about to be disposed) by cloning into a
            // fresh Bitmap - GDI+ decode can be lazy, and Image.FromStream keeps a reference to
            // its source stream for the image's lifetime otherwise.
            //
            // CA2000's escape analysis can't trace disposal through "wrapped in a record and
            // returned" - ownership genuinely transfers to the ImagePayload here: the caller
            // disposes it via ViewerPayload.ReleaseUnapplied() if the load is superseded before
            // ever reaching a content, or the content (ImageViewerContent.Dispose) does once it's
            // no longer needed. Same class of false positive already documented at
            // MainForm.OpenDirectoryTree() for a different escape shape (an event subscription).
#pragma warning disable CA2000
            var detached = new Bitmap(decoded);
#pragma warning restore CA2000
            return new ImagePayload(detached);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Exception)
        {
            // WebP support varies by Windows version/WIC codec availability - a decode failure
            // here is a real "this system can't preview this format" case, not a bug.
            return new ViewerErrorPayload(L.GetString("View.PreviewNotAvailable"), Modal: true);
        }
    }
}
