namespace CoderCommander.Viewers.Csv;

/// <summary>
/// RFC 4180 parsing (quoted fields, embedded delimiter/newline, <c>""</c>-escaped quotes) plus
/// delimiter auto-detection. A plain static function over a string - no file I/O, no dependency
/// on <see cref="Viewers.ViewerSource"/> - so it can be unit-tested directly and reused by
/// <c>CsvViewerLoader</c> without any indirection.
/// </summary>
internal static class CsvParser
{
    private static readonly char[] DelimiterCandidates = [',', ';', '\t', '|'];

    /// <summary>
    /// Picks the delimiter whose per-line occurrence count (outside quoted fields) is both non-zero
    /// and most consistent across the sample's first lines - consistency (low variance) is what
    /// actually indicates a real column structure, not just raw frequency (a semicolon inside prose
    /// text would win on frequency alone but vary wildly line to line).
    ///
    /// <para>Quote-aware: delimiters inside quoted fields are not counted, and newlines inside
    /// quoted fields do not split lines — both are legal under RFC 4180 and would otherwise skew
    /// the per-line counts and variance.</para>
    /// </summary>
    public static char DetectDelimiter(string sample)
    {
        // Split into logical lines respecting quoted newlines (RFC 4180 allows embedded \n in
        // quoted fields). A naive Split('\n') breaks a multi-line quoted field into separate
        // "lines" with wildly inconsistent delimiter counts, throwing off the variance score.
        var lines = new List<string>();
        var inQuotes = false;
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < sample.Length && lines.Count < 20; i++)
        {
            var c = sample[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
            }
            else if ((c == '\n' || c == '\r') && !inQuotes)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
                if (c == '\r' && i + 1 < sample.Length && sample[i + 1] == '\n') i++;
            }
            else
            {
                current.Append(c);
            }
        }
        if (current.Length > 0 && lines.Count < 20)
            lines.Add(current.ToString());
        if (lines.Count == 0) return ',';

        var best = ',';
        var bestScore = -1.0;

        foreach (var candidate in DelimiterCandidates)
        {
            var counts = new int[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                var count = 0;
                var lineInQuotes = false;
                foreach (var ch in lines[i])
                {
                    if (ch == '"')
                        lineInQuotes = !lineInQuotes;
                    else if (ch == candidate && !lineInQuotes)
                        count++;
                }
                counts[i] = count;
            }

            var sum = 0;
            foreach (var c in counts) sum += c;
            if (sum == 0) continue;
            var avg = sum / (double)counts.Length;

            var varianceSum = 0.0;
            foreach (var c in counts) varianceSum += (c - avg) * (c - avg);
            var variance = varianceSum / counts.Length;

            // Rewards both a higher average count and a more consistent (lower-variance) one.
            var score = avg / (1 + variance);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Parses <paramref name="text"/> into rows of fields. A trailing newline at end-of-input does
    /// not produce a spurious empty final row; a trailing delimiter with no following newline does
    /// still produce a final empty field (matching how e.g. Excel round-trips a CSV ending in
    /// <c>...,</c>).
    /// </summary>
    public static List<string[]> Parse(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;
        var i = 0;
        var length = text.Length;

        while (i < length)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                    }
                    else
                    {
                        inQuotes = false;
                        i++;
                    }
                }
                else
                {
                    field.Append(c);
                    i++;
                }
                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                i++;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                i++;
            }
            else if (c == '\r' || c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
                i++;
                if (c == '\r' && i < length && text[i] == '\n') i++;
            }
            else
            {
                field.Append(c);
                i++;
            }
        }

        // Flush a trailing field/row that wasn't terminated by a final newline.
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }
}
