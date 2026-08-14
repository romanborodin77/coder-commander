using System.Globalization;
using System.Xml.Linq;

namespace CoderCommander.Viewers.Office;

/// <summary>
/// Converts an <c>.ods</c> package's <c>content.xml</c> <c>office:spreadsheet</c> to one HTML
/// table per sheet.
///
/// <para><b>The trap this exists to avoid:</b> ODF compresses empty runs with
/// <c>table:number-rows-repeated</c>/<c>table:number-columns-repeated</c> - a spreadsheet app
/// habitually pads every sheet out to its full 1,048,576×16,384 grid with exactly one trailing
/// row/cell element carrying that huge a repeat count. Naively expanding every repeat is an
/// immediate OOM on a file that's otherwise a few KB. <see cref="RenderSheet"/> defends in two
/// layers: every repeat count is clamped against the remaining <see cref="OfficeLimits.MaxRows"/>/
/// <see cref="OfficeLimits.MaxColumns"/> budget <b>while parsing</b> (a hard ceiling regardless of
/// what the file claims - this is the actual OOM defense), then <see cref="TrimTrailingEmpty"/>
/// drops whatever blank tail remains within that budget so real spreadsheets don't render as a
/// wall of empty cells.</para>
/// </summary>
internal static class OdfSheetConverter
{
    private static readonly XNamespace Office = OdfNamespaces.Office;
    private static readonly XNamespace Text = OdfNamespaces.Text;
    private static readonly XNamespace Table = OdfNamespaces.Table;

    public static Task<List<OfficeDocumentPage>> ConvertAsync(OfficePackage pkg, CancellationToken ct)
    {
        var doc = pkg.ReadXml(OdfNamespaces.ContentPart) ?? throw new InvalidDataException("content.xml not found.");
        var spreadsheet = doc.Root?.Element(Office + "body")?.Element(Office + "spreadsheet")
                          ?? throw new InvalidDataException("Spreadsheet body not found.");

        var pages = new List<OfficeDocumentPage>();
        foreach (var table in spreadsheet.Elements(Table + "table"))
        {
            ct.ThrowIfCancellationRequested();
            var name = table.Attribute(Table + "name")?.Value ?? $"Sheet{pages.Count + 1}";
            pages.Add(new OfficeDocumentPage(name, RenderSheet(table)));
        }

        if (pages.Count == 0) throw new InvalidDataException("Spreadsheet has no readable sheets.");
        return Task.FromResult(pages);
    }

    private static string RenderSheet(XElement table)
    {
        var rows = new List<List<string>>();
        foreach (var rowEl in table.Elements(Table + "table-row"))
        {
            if (rows.Count >= OfficeLimits.MaxRows) break;
            var rowRepeat = Math.Min(ParseRepeat(rowEl, "number-rows-repeated"), OfficeLimits.MaxRows - rows.Count);

            var cells = new List<string>();
            foreach (var cellEl in rowEl.Elements(Table + "table-cell"))
            {
                if (cells.Count >= OfficeLimits.MaxColumns) break;
                var cellRepeat = Math.Min(ParseRepeat(cellEl, "number-columns-repeated"), OfficeLimits.MaxColumns - cells.Count);
                var text = ExtractCellText(cellEl);
                for (var i = 0; i < cellRepeat; i++) cells.Add(text);
            }

            // The same List<string> instance is shared across every repeated row - none of them
            // are ever mutated after this point, so sharing is safe and avoids rowRepeat copies.
            for (var i = 0; i < rowRepeat; i++) rows.Add(cells);
        }

        TrimTrailingEmpty(rows);

        var writer = new OfficeHtmlWriter();
        writer.RawLine("<table>");
        foreach (var row in rows)
        {
            writer.Raw("<tr>");
            foreach (var cellText in row)
            {
                writer.Raw("<td>");
                writer.Text(cellText);
                writer.Raw("</td>");
            }
            writer.RawLine("</tr>");
        }
        writer.RawLine("</table>");
        return writer.Build();
    }

    /// <summary>Drops fully-blank trailing rows, then fully-blank trailing columns - a single
    /// linear pass over the already-bounded (≤ MaxRows×MaxColumns) grid, not a nested "does every
    /// row's Nth cell", which would be quadratic against the DoS numbers this exists to defuse.</summary>
    private static void TrimTrailingEmpty(List<List<string>> rows)
    {
        while (rows.Count > 0 && rows[^1].TrueForAll(string.IsNullOrWhiteSpace))
            rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0) return;

        var lastNonEmptyCol = -1;
        foreach (var row in rows)
        {
            for (var i = row.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrWhiteSpace(row[i])) continue;
                if (i > lastNonEmptyCol) lastNonEmptyCol = i;
                break;
            }
        }
        if (lastNonEmptyCol < 0) { rows.Clear(); return; }

        var keep = lastNonEmptyCol + 1;
        foreach (var row in rows)
            if (row.Count > keep) row.RemoveRange(keep, row.Count - keep);
    }

    private static int ParseRepeat(XElement el, string attrName)
    {
        var v = el.Attribute(Table + attrName)?.Value;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;
    }

    private static string ExtractCellText(XElement cell)
    {
        var text = string.Concat(cell.Elements(Text + "p").Select(p => p.Value));
        if (!string.IsNullOrEmpty(text)) return text;

        // A numeric/date/currency cell sometimes carries its value only as an office:*-value
        // attribute with no <text:p> child at all (no display text was cached when it was saved).
        return cell.Attribute(Office + "value")?.Value
            ?? cell.Attribute(Office + "date-value")?.Value
            ?? "";
    }
}
