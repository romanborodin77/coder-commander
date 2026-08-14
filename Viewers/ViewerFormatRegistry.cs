namespace CoderCommander.Viewers;

/// <summary>
/// Process-wide registry of known viewer formats, populated once at startup (see
/// <c>Program.cs</c>) - mirrors <c>Archives.ArchiveFormatRegistry</c> in shape and in the
/// extension/signature detection order.
/// </summary>
public static class ViewerFormatRegistry
{
    private static readonly List<IViewerFormat> _formats = new();

    /// <summary>Every registered format, in registration order.</summary>
    public static IEnumerable<IViewerFormat> Registered => _formats;

    public static void Register(IViewerFormat format) => _formats.Add(format);

    public static IViewerFormat? ById(string id) =>
        _formats.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every <see cref="ViewerAvailability.Universal"/> format, in registration order -
    /// the fixed Text/ASCII/Binary/Hex group the toolbar always shows.</summary>
    public static IReadOnlyList<IViewerFormat> Universal =>
        _formats.Where(f => f.Availability == ViewerAvailability.Universal).ToList();

    /// <summary>Matches by longest registered extension suffix among
    /// <see cref="ViewerAvailability.Matched"/> formats only - same longest-match-wins rule as
    /// <c>ArchiveFormatRegistry.FromExtension</c>. A format that declares
    /// <see cref="ViewerCapabilities.NeedsWebView"/> is skipped entirely when the WebView2 Runtime
    /// isn't installed - see that flag's own doc comment.</summary>
    public static IViewerFormat? FromExtension(string path)
    {
        IViewerFormat? best = null;
        var bestLength = 0;

        foreach (var format in _formats)
        {
            if (format.Availability != ViewerAvailability.Matched) continue;
            if (format.Capabilities.HasFlag(ViewerCapabilities.NeedsWebView) && !WebViewAvailability.IsAvailable) continue;

            foreach (var extension in format.Extensions)
            {
                if (extension.Length > bestLength && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    best = format;
                    bestLength = extension.Length;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Matches against an already-read header prefix. Unlike
    /// <c>ArchiveFormatRegistry.FromSignature</c>, this does NOT open the file itself - the
    /// caller supplies the prefix (via <see cref="ViewerSource.ReadPrefixAsync"/>), because the
    /// file may live on a filesystem where <c>File.OpenRead</c> is not an option at all.
    /// </summary>
    public static IViewerFormat? FromSignature(ReadOnlySpan<byte> header)
    {
        foreach (var format in _formats)
        {
            if (format.Availability != ViewerAvailability.Matched) continue;
            if (format.Capabilities.HasFlag(ViewerCapabilities.NeedsWebView) && !WebViewAvailability.IsAvailable) continue;
            if (format.MatchesSignature(header)) return format;
        }
        return null;
    }

    /// <summary>Extension first, signature as fallback, matching <c>ArchiveFormatRegistry.Detect</c>'s
    /// own "extension first, signature only when extension didn't resolve" order. Returns null
    /// when nothing matched - the caller falls back to the last-used universal format.</summary>
    public static IViewerFormat? Detect(string path, ReadOnlySpan<byte> header) =>
        FromExtension(path) ?? FromSignature(header);
}
