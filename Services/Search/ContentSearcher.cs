using System.Text;

namespace CoderCommander.Services.Search;

/// <summary>What a content search found in one file.</summary>
/// <param name="Found">Whether the text occurs at all.</param>
/// <param name="LineNumber">1-based line of the first occurrence, or 0 when not found.</param>
/// <param name="Line">The line it occurs on, trimmed and length-capped for display.</param>
public readonly record struct ContentHit(bool Found, int LineNumber, string Line)
{
    public static readonly ContentHit None = new(false, 0, "");
}

/// <summary>
/// Searches a file's text for a string.
///
/// <para><b>Streamed, never buffered whole.</b> A search runs over whatever the user pointed it at,
/// which will eventually include a multi-gigabyte log or a disk image. Reading files into memory to
/// search them is how a file manager becomes the thing that has to be killed from Task Manager.</para>
///
/// <para><b>The chunk boundary is the bug this class exists to avoid.</b> Read a file in blocks and
/// search each block, and any occurrence straddling a boundary is missed - a defect that is invisible
/// on every small test file and reproducible only on real data, which is the worst combination there
/// is. Each block therefore carries the tail of the previous one, long enough that no occurrence can
/// fall between them.</para>
///
/// <para><b>Binary files are skipped, not scanned.</b> Text search over an executable produces
/// meaningless hits inside compiled data, and decoding it wastes the time of the search. The test is
/// the one every tool uses: a NUL byte early in the file.</para>
/// </summary>
public static class ContentSearcher
{
    /// <summary>How much of the start of a file is examined to decide whether it is text. Enough to
    /// cover any realistic header, small enough to cost nothing.</summary>
    private const int SniffBytes = 8192;

    /// <summary>Characters kept from the matching line for display. A minified file is one line of
    /// several megabytes, and putting that in a results grid freezes it.</summary>
    private const int MaxPreviewLength = 300;

    /// <summary>Decoded characters per block. Large enough that per-block overhead disappears,
    /// small enough that the overlap below is a rounding error next to it.</summary>
    private const int BlockChars = 128 * 1024;

    /// <summary>
    /// Whether <paramref name="stream"/> contains <paramref name="needle"/>.
    /// </summary>
    /// <param name="stream">Read forward only; the caller owns it.</param>
    /// <param name="needle">Text to find. An empty needle finds nothing rather than everything -
    /// "no content filter" is the caller's decision to make, not a degenerate search.</param>
    /// <param name="matchCase">Case-sensitive comparison.</param>
    /// <param name="wholeWord">Require non-word characters (or the file's edges) around the match.</param>
    public static async Task<ContentHit> FindAsync(
        Stream stream, string needle, bool matchCase, bool wholeWord, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(needle)) return ContentHit.None;

        var header = new byte[SniffBytes];
        var headerLength = await ReadAtLeastAsync(stream, header, ct).ConfigureAwait(false);
        if (headerLength == 0) return ContentHit.None;

        if (LooksBinary(header, headerLength)) return ContentHit.None;

        var encoding = TextEncodingDetector.Detect(header[..headerLength], out var preambleLength);
        var decoder = encoding.GetDecoder();

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // The tail carried into the next block: one character less than the needle, which is the
        // most that can straddle a boundary. Plus one for the character before it, which whole-word
        // matching has to look at.
        var overlap = Math.Max(needle.Length, 1);

        var carry = "";
        var lineOffset = 1;          // 1-based line number at which `carry` starts
        var buffer = new byte[BlockChars];
        var chars = new char[encoding.GetMaxCharCount(BlockChars)];

        // The first block is the header we already read, minus any byte-order mark - which is part
        // of the encoding, not of the text.
        var pending = header.AsMemory(preambleLength, headerLength - preambleLength);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var decoded = decoder.GetChars(pending.Span, chars, flush: false);
            var window = carry + new string(chars, 0, decoded);

            var index = IndexOf(window, needle, comparison, wholeWord);
            if (index >= 0)
                return Describe(window, index, needle.Length, lineOffset);

            // Keep only what could still be part of a match that continues into the next block.
            var keep = Math.Min(overlap, window.Length);
            var dropped = window.Length - keep;
            lineOffset += CountLines(window.AsSpan(0, dropped));
            carry = window[dropped..];

            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) return ContentHit.None;
            pending = buffer.AsMemory(0, read);
        }
    }

    /// <summary>
    /// Fills as much of <paramref name="buffer"/> as the stream will give in one pass.
    ///
    /// <para><see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> may legally return
    /// fewer bytes than asked for at any time, and over a network stream it routinely does - a
    /// single read of a remote file can come back with a few hundred bytes. Treating that as the
    /// whole header would hand the encoding detector a fragment and, worse, decide a text file was
    /// empty.</para>
    /// </summary>
    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    /// <summary>
    /// Whether these bytes look like a binary file.
    ///
    /// <para>A NUL byte is the test, with one exception that matters: UTF-16 text is full of NUL
    /// bytes by construction, so a byte-order mark saying so overrides the verdict. Without that
    /// carve-out, searching a UTF-16 file - which is what Notepad still writes when asked for
    /// "Unicode" - silently finds nothing.</para>
    /// </summary>
    private static bool LooksBinary(byte[] data, int length)
    {
        if (length >= 2 && (data[0] == 0xFF && data[1] == 0xFE || data[0] == 0xFE && data[1] == 0xFF))
            return false;

        for (var i = 0; i < length; i++)
        {
            if (data[i] == 0) return true;
        }
        return false;
    }

    /// <summary>Ordinary substring search, or one that additionally requires word boundaries.</summary>
    private static int IndexOf(string haystack, string needle, StringComparison comparison, bool wholeWord)
    {
        var index = haystack.IndexOf(needle, comparison);
        if (!wholeWord) return index;

        while (index >= 0)
        {
            var beforeOk = index == 0 || !IsWordCharacter(haystack[index - 1]);
            var afterIndex = index + needle.Length;
            var afterOk = afterIndex >= haystack.Length || !IsWordCharacter(haystack[afterIndex]);

            if (beforeOk && afterOk) return index;

            index = haystack.IndexOf(needle, index + 1, comparison);
        }
        return -1;
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Turns a match position into the line number and the line itself.</summary>
    private static ContentHit Describe(string window, int index, int needleLength, int lineOffset)
    {
        var lineNumber = lineOffset + CountLines(window.AsSpan(0, index));

        var start = window.LastIndexOfAny(['\n', '\r'], Math.Max(index - 1, 0)) + 1;
        if (index == 0) start = 0;

        var end = window.IndexOfAny(['\n', '\r'], index + Math.Max(needleLength - 1, 0));
        if (end < 0) end = window.Length;

        var line = window[start..end].Trim();
        if (line.Length > MaxPreviewLength) line = line[..MaxPreviewLength] + "…";

        return new ContentHit(true, lineNumber, line);
    }

    /// <summary>Line breaks in a span, counting CRLF as one. Used only to keep the reported line
    /// number right across block boundaries, so it must agree with itself, not with any particular
    /// editor's idea of a line.</summary>
    private static int CountLines(ReadOnlySpan<char> text)
    {
        var lines = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') lines++;
            else if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) lines++;
        }
        return lines;
    }
}
