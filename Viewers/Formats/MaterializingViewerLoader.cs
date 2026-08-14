using System.Threading;
using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;

namespace CoderCommander.Viewers.Formats;

/// <summary>Shared loader shape for every format that hands WebView2 a file to navigate to
/// directly rather than transformed content (Html browser mode, Pdf, Media) - see
/// <see cref="MaterializedFilePayload"/>'s own doc comment for why exactly one of a real directory
/// or raw bytes comes back. A local file costs nothing to report (no read at all, just its own
/// path split into directory/name); anything else is read fully into memory, capped at
/// <see cref="ViewerLimits.MaterializeMaxBytes"/>, for the content to write out to its own
/// isolated temp folder.</summary>
internal abstract class MaterializingViewerLoader : IViewerLoader
{
    public async Task<ViewerPayload> LoadAsync(ViewerSource source, CancellationToken ct)
    {
        var L = LocalizationService.Current;
        var fileName = VfsPath.GetName(source.Path);

        if (source.IsNative)
        {
            var directory = Path.GetDirectoryName(source.Path);
            if (string.IsNullOrEmpty(directory))
                return new ViewerErrorPayload(L.GetString("View.FileNotFound"), Modal: true);

            var localSize = await source.GetSizeAsync(ct).ConfigureAwait(false);
            return new MaterializedFilePayload(true, directory, null, fileName, BuildStatus(L, localSize));
        }

        var size = await source.GetSizeAsync(ct).ConfigureAwait(false);
        if (size > ViewerLimits.MaterializeMaxBytes)
            return new ViewerErrorPayload(
                L.GetString("View.TooBigToMaterialize",
                    FormatUtils.FormatSize(size), FormatUtils.FormatSize(ViewerLimits.MaterializeMaxBytes)),
                Modal: false);

        byte[] bytes;
        try
        {
            bytes = await source.ReadAllBytesAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (IOException)
        {
            return new ViewerErrorPayload(L.GetString("View.FileNotFound"), Modal: true);
        }
        ct.ThrowIfCancellationRequested();

        return new MaterializedFilePayload(false, null, bytes, fileName, BuildStatus(L, bytes.LongLength));
    }

    /// <summary>Builds this format's status-bar label (e.g. "PDF — 2.4 MB") - the only thing that
    /// differs between Html/Pdf/Media at the loader level.</summary>
    protected abstract string BuildStatus(LocalizationService localization, long size);
}
