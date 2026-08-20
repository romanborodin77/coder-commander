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
            // MemoryStream is NOT disposed here — Image.FromStream keeps a reference to its
            // source stream for the image's lifetime (GDI+ lazy decode). The Image (and the
            // underlying byte[] via MemoryStream) stays alive until the caller disposes the
            // returned ImagePayload. Previously, a `using` + `new Bitmap(decoded)` clone
            // detached from the stream, but cloning loses all but the first frame of
            // multi-page TIFF and animated GIF.
            var ms = new MemoryStream(raw);
            var decoded = Image.FromStream(ms);

            // GDI+ decode has no cancellation-aware overload - it runs to completion regardless
            // of ct. This is the earliest point cancellation can be honored.
            ct.ThrowIfCancellationRequested();

            if ((long)decoded.Width * decoded.Height > ViewerLimits.ImageMaxPixels)
            {
                decoded.Dispose();
                ms.Dispose();
                return new ViewerErrorPayload(
                    L.GetString("View.ImageTooBig", $"{decoded.Width}x{decoded.Height}px", $"{ViewerLimits.ImageMaxPixels:N0}px"),
                    Modal: true);
            }

            // Apply EXIF orientation: modern phones/cameras store portrait images right-side-up
            // only via the EXIF orientation tag (0x0112), expecting the viewer to rotate. Without
            // this, JPEGs from phones appear sideways or upside-down.
            var oriented = ApplyExifOrientation(decoded);

            // Ownership of both `oriented` and `ms` transfers to the ImagePayload: the caller
            // disposes it via ViewerPayload.ReleaseUnapplied() if the load is superseded before
            // ever reaching a content, or ImageViewerContent.Dispose does once it's no longer needed.
            return new ImagePayload(oriented);
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

    /// <summary>Applies the EXIF orientation tag (0x0112) to the decoded image so photos from
    /// phones/cameras display right-side-up. Returns the original image unchanged if no EXIF
    /// orientation is present or the property cannot be read.</summary>
    private static Image ApplyExifOrientation(Image img)
    {
        try
        {
            const int PropertyTagOrientation = 0x0112;
            if (img.PropertyIdList?.Contains(PropertyTagOrientation) != true)
                return img;

            var val = img.GetPropertyItem(PropertyTagOrientation);
            if (val?.Value is null || val.Value.Length < 2)
                return img;

            var orientation = (ushort)(val.Value[0] | (val.Value[1] << 8));
            var flipType = orientation switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };

            if (flipType != RotateFlipType.RotateNoneFlipNone)
                img.RotateFlip(flipType);
            return img;
        }
        catch
        {
            return img;
        }
    }
}
