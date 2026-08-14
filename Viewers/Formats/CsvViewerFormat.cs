using System.Threading;
using CoderCommander.Services;
using CoderCommander.Utils;
using CoderCommander.Viewers.Csv;
using CoderCommander.WinForms.Viewers;

namespace CoderCommander.Viewers.Formats;

/// <summary>Matched format for delimiter-separated tables. Reuses the same size limit and
/// encoding-detection path as Text mode - a CSV file is text first, table second.</summary>
public sealed class CsvViewerFormat : IViewerFormat
{
    public static readonly CsvViewerFormat Instance = new();
    private CsvViewerFormat() { }

    public string Id => "csv";
    public string DisplayNameKey => "View.Csv";
    public string IconKey => "view_csv";
    public IReadOnlyList<string> Extensions => [".csv", ".tsv"];
    public ViewerAvailability Availability => ViewerAvailability.Matched;
    public ViewerCapabilities Capabilities => ViewerCapabilities.None;
    public bool MatchesSignature(ReadOnlySpan<byte> header) => false;
    public IViewerLoader CreateLoader() => new CsvViewerLoader();
    public IViewerContent CreateContent(ViewerContentContext ctx) => new CsvViewerContent(ctx);
}

public sealed class CsvViewerLoader : IViewerLoader
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

        var encoding = TextEncodingDetector.Detect(raw, out var preambleLength);
        var text = encoding.GetString(raw, preambleLength, raw.Length - preambleLength);
        ct.ThrowIfCancellationRequested();

        var settings = SettingsService.Load();
        var delimiter = settings.ViewerCsvDelimiter is { Length: 1 } d ? d[0] : CsvParser.DetectDelimiter(text);
        var rows = CsvParser.Parse(text, delimiter);
        ct.ThrowIfCancellationRequested();

        var columnCount = 0;
        foreach (var row in rows)
            if (row.Length > columnCount) columnCount = row.Length;

        var status = L.GetString("View.CsvMode", rows.Count, columnCount);
        return new CsvPayload(rows, delimiter, status);
    }
}
