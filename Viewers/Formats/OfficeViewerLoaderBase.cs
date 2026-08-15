using System.Threading;
using System.Xml;
using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.Viewers.Office;

namespace CoderCommander.Viewers.Formats;

/// <summary>
/// Shared shape for the Word/Sheet/Slides loaders: materialize a non-local source to a real path
/// (<see cref="Office.OfficePackage"/> needs one - it opens the package via the same
/// <c>ZipArchiveFormat.OpenRead(path)</c> every archive reader in this app already requires a
/// path for), open it, convert, and turn any failure - a corrupted package, a password-protected
/// one (OOXML encryption wraps the whole ZIP in an OLE compound file that never parses as one, so
/// this is indistinguishable from "corrupted" without decrypting it, which this app doesn't
/// attempt) - into one shared, non-alarming error message rather than a raw exception.
///
/// <para>Unlike Html/Pdf/Media's materialization (done on the content side, into the per-window
/// <c>ViewerTempSession</c>, because <c>WebViewHost</c> needs to navigate <em>at</em> the
/// materialized file), this materializes into a short-lived system-temp scratch file that's
/// deleted before <see cref="LoadAsync"/> even returns - nothing downstream of this loader ever
/// touches the original package again, only the HTML string <see cref="ConvertAsync"/> already
/// extracted from it.</para>
/// </summary>
internal abstract class OfficeViewerLoaderBase : IViewerLoader
{
    public async Task<ViewerPayload> LoadAsync(ViewerSource source, CancellationToken ct)
    {
        var L = LocalizationService.Current;
        var localPath = source.Path;
        string? scratchPath = null;
        try
        {
            if (!source.IsNative)
            {
                var size = await source.GetSizeAsync(ct).ConfigureAwait(false);
                if (size > ViewerLimits.MaterializeMaxBytes)
                {
                    return new ViewerErrorPayload(
                        L.GetString("View.TooBigToMaterialize",
                            FormatUtils.FormatSize(size), FormatUtils.FormatSize(ViewerLimits.MaterializeMaxBytes)),
                        Modal: false);
                }

                var bytes = await source.ReadAllBytesAsync(ct).ConfigureAwait(false);
                // TempFileNaming.InSystemTemp - the project's one naming convention (see its own
                // doc comment) - rather than a hand-built "cc-office-{guid}{ext}". No functional
                // difference: ZipArchiveReader.OpenRead (what OfficePackage.OpenAsync ultimately
                // calls) opens by path via System.IO.Compression.ZipFile.OpenRead, which never
                // inspects the extension, so preserving it here was never load-bearing.
                scratchPath = TempFileNaming.InSystemTemp("office");
                await File.WriteAllBytesAsync(scratchPath, bytes, ct).ConfigureAwait(false);
                localPath = scratchPath;
            }

            using var pkg = await OfficePackage.OpenAsync(localPath, ct).ConfigureAwait(false);
            var extension = FileEntry.GetExtension(source.Path);
            var pages = await ConvertAsync(pkg, extension, ct).ConfigureAwait(false);
            return new OfficeDocumentPayload(pages, L.GetString(StatusKey, pages.Count));
        }
        catch (OperationCanceledException) { throw; }
        // Caught before the general InvalidDataException branch below - OfficePackageRejectedException
        // is a subclass, and a safety-limit rejection (too many parts, too large, a suspicious
        // compression ratio, an unsafe entry name) is not the same failure as a genuinely corrupt or
        // password-protected package, so it gets its own, accurate message instead of being told
        // "this looks encrypted" for a perfectly ordinary file that was simply too big.
        catch (OfficePackageRejectedException ex)
        {
            LogService.Warning($"Office document rejected by a safety limit: {source.Path}: {ex.Message}");
            return new ViewerErrorPayload(L.GetString("View.Office.Rejected"), Modal: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or XmlException)
        {
            LogService.Error($"Office document failed to open: {source.Path}", ex);
            return new ViewerErrorPayload(L.GetString("View.Office.Encrypted"), Modal: true);
        }
        finally
        {
            if (scratchPath != null)
            {
                try { File.Delete(scratchPath); }
                catch { /* best-effort - a leaked few-KB scratch file in %TEMP% is not worth failing the load over */ }
            }
        }
    }

    /// <summary>Localization key for this format's status-bar label, formatted with the page
    /// count (e.g. "Document — 3 sheets").</summary>
    protected abstract string StatusKey { get; }

    /// <summary>Dispatches to the OOXML or ODF converter based on <paramref name="extension"/>
    /// (already lower-cased, leading dot included, e.g. ".docx") and returns one page per
    /// sheet/slide (or a single page for a Word/Text document).</summary>
    protected abstract Task<List<OfficeDocumentPage>> ConvertAsync(OfficePackage pkg, string extension, CancellationToken ct);
}
