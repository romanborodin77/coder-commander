using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CoderCommander.Viewers.Office;

/// <summary>
/// Converts an <c>.xlsx</c> workbook to one HTML table per sheet. Reads <c>xl/sharedStrings.xml</c>
/// (string cells reference it by index rather than storing text inline) and <c>xl/styles.xml</c>'s
/// <c>cellXfs</c>/<c>numFmts</c> once for the whole workbook, since every sheet needs both to render
/// dates as dates rather than as the raw serial number Excel actually stores.
///
/// <para><b>Why dates need styles.xml at all:</b> a cell holding a date has no distinct XML type -
/// it's a plain numeric cell (<c>v</c> is a serial day count from 1899-12-30) whose <c>s</c>
/// attribute points at a <c>cellXfs</c> entry whose <c>numFmtId</c> says "format this as a date".
/// Built-in ids 14-22 are ECMA-376's fixed date/time formats; anything ≥164 is a workbook-defined
/// custom format whose <c>formatCode</c> text has to be inspected instead.</para>
/// </summary>
internal static class OoxmlSheetConverter
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = OoxmlNamespaces.Relationships;
    private static readonly Regex DateFormatChars = new("[yYdD]", RegexOptions.Compiled);

    private const string WorkbookPart = "xl/workbook.xml";
    private const string WorkbookRelsPart = "xl/_rels/workbook.xml.rels";
    private const string SharedStringsPart = "xl/sharedStrings.xml";
    private const string StylesPart = "xl/styles.xml";

    /// <summary>Excel's own valid date range: serial 1 (1900-01-01) through serial 2,958,465
    /// (9999-12-31). <c>double.TryParse</c> with <see cref="NumberStyles.Float"/> accepts values
    /// far outside this on .NET Core 3.0+ (e.g. <c>"1E308"</c>, <c>"Infinity"</c>) - constructing a
    /// <see cref="DateTime"/> from one of those throws <see cref="ArgumentOutOfRangeException"/>,
    /// which used to abort the whole sheet (not caught by <c>OfficeViewerLoaderBase</c>'s filter)
    /// over a single malformed cell.</summary>
    private const double MaxExcelDateSerial = 2_958_465;

    public static Task<List<OfficeDocumentPage>> ConvertAsync(OfficePackage pkg, CancellationToken ct)
    {
        var workbook = pkg.ReadXml(WorkbookPart) ?? throw new InvalidDataException("xl/workbook.xml not found.");
        var rels = OoxmlWordConverter.LoadRelationships(pkg, WorkbookRelsPart, WorkbookPart);
        var sharedStrings = LoadSharedStrings(pkg);
        var dateStyleIndices = LoadDateStyleIndices(pkg);
        // Mac-originated workbooks can use the 1904 epoch instead of Excel's usual 1900 one -
        // <workbookPr date1904="1"/> is the one place that's declared; ignoring it renders every
        // date exactly 1462 days early.
        var date1904 = workbook.Root?.Element(S + "workbookPr")?.Attribute("date1904")?.Value is "1" or "true";

        var pages = new List<OfficeDocumentPage>();
        foreach (var sheetEl in workbook.Root?.Element(S + "sheets")?.Elements(S + "sheet") ?? [])
        {
            ct.ThrowIfCancellationRequested();
            var name = sheetEl.Attribute("name")?.Value ?? $"Sheet{pages.Count + 1}";
            var relId = sheetEl.Attribute(R + "id")?.Value;
            if (relId == null || !rels.TryGetValue(relId, out var sheetPart)) continue;

            var html = RenderSheet(pkg.ReadXml(sheetPart), sharedStrings, dateStyleIndices, date1904);
            pages.Add(new OfficeDocumentPage(name, html));
        }

        if (pages.Count == 0) throw new InvalidDataException("Workbook has no readable sheets.");
        return Task.FromResult(pages);
    }

    private static string RenderSheet(XDocument? sheet, IReadOnlyList<string> sharedStrings, HashSet<int> dateStyles, bool date1904)
    {
        var writer = new OfficeHtmlWriter();
        writer.RawLine("<table>");

        var rows = sheet?.Root?.Element(S + "sheetData")?.Elements(S + "row") ?? [];
        var rowCount = 0;
        foreach (var row in rows)
        {
            if (++rowCount > OfficeLimits.MaxRows) break;
            writer.Raw("<tr>");

            // XLSX omits cells with no content, so a row with data only in columns A and D emits
            // just two <c> elements - rendering them adjacently (the old behavior) silently shifted
            // every following cell left, making data appear under the wrong header. Cells are
            // padded up to their own r="..." column reference instead of relied-on document order.
            var nextCol = 0;
            foreach (var cell in row.Elements(S + "c"))
            {
                var colIndex = ColumnIndexFromRef(cell.Attribute("r")?.Value);
                if (colIndex < 0) colIndex = nextCol; // no/unparseable r= - fall back to positional

                if (colIndex >= OfficeLimits.MaxColumns) break; // this and every later cell in the row are past budget

                for (var pad = nextCol; pad < colIndex; pad++)
                    writer.Raw("<td></td>");

                writer.Raw("<td>");
                writer.Text(RenderCellText(cell, sharedStrings, dateStyles, date1904));
                writer.Raw("</td>");

                nextCol = colIndex + 1;
            }
            writer.RawLine("</tr>");
        }

        writer.RawLine("</table>");
        return writer.Build();
    }

    /// <summary>Parses the column letters from a cell reference like <c>"C7"</c> or <c>"AB123"</c>
    /// into a 0-based column index (<c>"A"</c> → 0, <c>"AB"</c> → 27), or -1 if
    /// <paramref name="cellRef"/> is missing/doesn't start with a letter. Per the OOXML spec cell
    /// references are always uppercase A-Z; that's relied on here rather than normalized, since a
    /// lowercase reference would already be a malformed file no real writer produces.</summary>
    private static int ColumnIndexFromRef(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef) || cellRef[0] is < 'A' or > 'Z') return -1;

        var col = 0;
        foreach (var c in cellRef)
        {
            if (c is < 'A' or > 'Z') break;
            col = col * 26 + (c - 'A' + 1);
        }
        return col - 1;
    }

    private static string RenderCellText(XElement cell, IReadOnlyList<string> sharedStrings, HashSet<int> dateStyles, bool date1904)
    {
        var type = cell.Attribute("t")?.Value;

        if (type == "inlineStr")
            return cell.Element(S + "is")?.Element(S + "t")?.Value ?? "";

        if (type == "b")
        {
            // A boolean cell's <v> is the literal digit "1"/"0", not a human-readable value -
            // rendering it verbatim (the old behavior) showed "1"/"0" instead of TRUE/FALSE.
            var raw = cell.Element(S + "v")?.Value;
            return raw switch { "1" => "TRUE", "0" => "FALSE", _ => raw ?? "" };
        }

        if (type == "str" || type == "e")
            return cell.Element(S + "v")?.Value ?? "";

        var rawValue = cell.Element(S + "v")?.Value;
        if (string.IsNullOrEmpty(rawValue)) return "";

        if (type == "s")
        {
            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) &&
                   idx >= 0 && idx < sharedStrings.Count
                ? sharedStrings[idx]
                : "";
        }

        // Numeric cell - check whether its style says "this is actually a date/time".
        if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var styleIndex = int.TryParse(cell.Attribute("s")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 0;
            if (dateStyles.Contains(styleIndex) && number >= 0 && number <= MaxExcelDateSerial)
            {
                // Standard serial-date epoch trick (1899-12-30) - the same off-by-one every XLSX
                // reader uses to absorb Excel's fictitious 1900-02-29 without special-casing it.
                // Mac-originated workbooks may instead declare the 1904 epoch (see date1904 above).
                var epoch = date1904
                    ? new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
                    : new DateTime(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);
                var date = epoch.AddDays(number);
                return number == Math.Floor(number)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return rawValue;
    }

    private static List<string> LoadSharedStrings(OfficePackage pkg)
    {
        var doc = pkg.ReadXml(SharedStringsPart);
        if (doc?.Root == null) return [];

        var result = new List<string>();
        foreach (var si in doc.Root.Elements(S + "si"))
        {
            // A shared string entry can be a single <t>, or several rich-text runs <r><t>...</t></r>
            // (formatting mid-string, e.g. one bold word) - concatenate every <t> either way.
            result.Add(string.Concat(si.Descendants(S + "t").Select(t => t.Value)));
        }
        return result;
    }

    /// <summary>Returns the set of <c>cellXfs</c> indices whose <c>numFmtId</c> is a date/time
    /// format - built-in ids 14-22, or a custom (≥164) format whose code contains a 'y' or 'd'
    /// (case-insensitive - Excel format codes aren't case-sensitive for these). Time-only custom
    /// formats without a date component (e.g. "h:mm:ss") are intentionally not matched here since
    /// 'h'/'m'/'s' alone are too ambiguous against ordinary number formats; the built-in range
    /// already covers the common ones (18-21).</summary>
    private static HashSet<int> LoadDateStyleIndices(OfficePackage pkg)
    {
        var result = new HashSet<int>();
        var doc = pkg.ReadXml(StylesPart);
        if (doc?.Root == null) return result;

        var customDateFormatIds = new HashSet<int>();
        foreach (var fmt in doc.Root.Element(S + "numFmts")?.Elements(S + "numFmt") ?? [])
        {
            if (int.TryParse(fmt.Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                DateFormatChars.IsMatch(fmt.Attribute("formatCode")?.Value ?? ""))
            {
                customDateFormatIds.Add(id);
            }
        }

        var cellXfs = doc.Root.Element(S + "cellXfs")?.Elements(S + "xf").ToList() ?? [];
        for (var i = 0; i < cellXfs.Count; i++)
        {
            if (int.TryParse(cellXfs[i].Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numFmtId) &&
                (numFmtId is >= 14 and <= 22 || customDateFormatIds.Contains(numFmtId)))
            {
                result.Add(i);
            }
        }
        return result;
    }
}
