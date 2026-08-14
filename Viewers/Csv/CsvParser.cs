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
    /// Picks the delimiter whose per-line occurrence count is both non-zero and most consistent
    /// across the sample's first lines - consistency (low variance) is what actually indicates a
    /// real column structure, not just raw frequency (a semicolon inside prose text would win on
    /// frequency alone but vary wildly line to line).
    /// </summary>
    public static char DetectDelimiter(string sample)
    {
        var lines = sample.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 20) Array.Resize(ref lines, 20);
        if (lines.Length == 0) return ',';

        var best = ',';
        var bestScore = -1.0;

        foreach (var candidate in DelimiterCandidates)
        {
            var counts = new int[lines.Length];
            for (var i = 0; i < lines.Length; i++)
            {
                var count = 0;
                foreach (var ch in lines[i])
                    if (ch == candidate) count++;
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
