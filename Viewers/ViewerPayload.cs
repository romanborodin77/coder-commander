namespace CoderCommander.Viewers;

/// <summary>
/// What a loader produced. Provider-defined - <c>ViewerForm</c> never inspects a concrete
/// subtype, only the <see cref="IViewerContent"/> that requested the load does (via a
/// pattern-match switch in its own <c>RenderAsync</c>).
/// </summary>
public abstract record ViewerPayload
{
    /// <summary>Called when this payload is discarded without ever reaching a content - a newer
    /// load superseded it while this one was finishing on the pool thread. Default no-op;
    /// <see cref="ImagePayload"/> disposes its <see cref="Image"/>. This is what closes the leak
    /// the previous staleness guard had: a decoded <see cref="System.Drawing.Image"/> discarded by
    /// <c>if (ct != _loadCts?.Token) return;</c> with nothing ever disposing it.</summary>
    public virtual void ReleaseUnapplied() { }
}

/// <summary>Text-family payload - used by Text/ASCII/Binary/Hex alike, which all render into the
/// same kind of content. <paramref name="StatusText"/> is the fully-localized, already-formatted
/// status bar mode label (e.g. "Text mode — 12.4 KB") - built by the loader, which is the one
/// side that knows the file size and which specific format produced this text.</summary>
public sealed record TextPayload(string Text, string StatusText) : ViewerPayload;

/// <summary>Decoded image payload. <see cref="Image"/> is a detached <see cref="Bitmap"/> - it
/// does not reference whatever stream/byte buffer it was decoded from, so it outlives that
/// buffer safely.</summary>
public sealed record ImagePayload(Image Image) : ViewerPayload
{
    public override void ReleaseUnapplied() => Image.Dispose();
}

/// <summary>Parsed table payload for the CSV format. <paramref name="Rows"/> includes every row
/// (no header/data distinction here - that split is a display-time choice the content makes, so
/// toggling "first row is header" doesn't need a reload).</summary>
public sealed record CsvPayload(IReadOnlyList<string[]> Rows, char Delimiter, string StatusText) : ViewerPayload;

/// <summary>A load failure. <paramref name="Modal"/> distinguishes "show this as an inline
/// message in whatever surface would have shown the content" (text-family: too-big-for-text,
/// file-not-found) from "this format can't proceed at all, tell the user explicitly"
/// (image: bad/corrupt file, decode failure) - collapsing the old <c>TextError</c>/<c>ImageError</c>
/// duality from a type distinction into a single flag.</summary>
public sealed record ViewerErrorPayload(string Message, bool Modal) : ViewerPayload;

/// <summary>A file WebView2 should navigate to directly - used by Html (browser mode), Pdf, and
/// Media, none of which need any template: Edge's own native rendering (embedded PDF viewer,
/// HTML5 media element for a direct video/audio URL, or just the HTML page itself) does the rest.
/// Exactly one of <see cref="Directory"/> or <see cref="Bytes"/> is populated:
/// <paramref name="IsOwnDirectory"/> true means <see cref="Directory"/> is the file's own real,
/// already-on-disk containing folder (a local file - mapped read-only so relative neighbors/links
/// resolve, the whole point of HTML browser mode) and <see cref="Bytes"/> is null; false means
/// <see cref="Bytes"/> holds the file's full content (read through <see cref="ViewerSource"/>,
/// so this also works on an archive/remote file) and the content materializes it into its own
/// isolated temp folder before mapping - see <see cref="Viewers.ViewerLimits.MaterializeMaxBytes"/>
/// for the size this is capped at.</summary>
public sealed record MaterializedFilePayload(
    bool IsOwnDirectory, string? Directory, byte[]? Bytes, string FileName, string StatusText) : ViewerPayload;

/// <summary>Markdig-rendered HTML document plus its original source text - Markdown is the one
/// WebView format that needs actual content transformation before WebView2 has anything to show;
/// <paramref name="SourceText"/> is what the "show source" toolbar toggle displays and what the
/// find bar searches (searching rendered HTML markup would be useless to a user).</summary>
public sealed record MarkdownPayload(string RenderedHtml, string SourceText, string StatusText) : ViewerPayload;

/// <summary>One renderable page of an Office document - a whole (single-page) Word/OpenDocument
/// Text document, or one sheet/slide of a multi-page Sheet/Slides document. <paramref name="Title"/>
/// is the sheet or slide's own name/number, shown by the page-navigation toolbar.</summary>
public sealed record OfficeDocumentPage(string Title, string Html);

/// <summary>Converted Office-document payload, shared by the Word/Sheet/Slides formats
/// (<c>Viewers.Office</c>) - a Word document is always exactly one page; Sheet/Slides can be many,
/// which is what the shared <c>OfficeViewerContent</c>'s page-navigation toolbar (hidden when
/// <see cref="Pages"/> has only one entry) is for.</summary>
public sealed record OfficeDocumentPayload(IReadOnlyList<OfficeDocumentPage> Pages, string StatusText) : ViewerPayload;
