using System.Text;

namespace CoderCommander.Terminal.Vt;

/// <summary>Callback shape for decoded/parsed char spans - a plain <c>Action&lt;ReadOnlySpan&lt;char&gt;&gt;</c>
/// isn't legal since <see cref="ReadOnlySpan{T}"/> is a ref struct and can't be a generic type argument.</summary>
internal delegate void CharSpanAction(ReadOnlySpan<char> chars);

/// <summary>
/// Decodes a stream of UTF-8 byte chunks into <see cref="char"/>s, correctly across arbitrary
/// chunk boundaries. Wraps a single, persistent <see cref="Decoder"/> instance - which is
/// SPECIFIED (System.Text.Decoder's whole contract) to retain a partial multi-byte sequence
/// across <see cref="Decoder.Convert"/> calls, unlike the common but wrong shortcut of calling
/// <c>Encoding.UTF8.GetString(buffer, 0, n)</c> once per chunk (which turns a sequence split
/// across a chunk boundary into a U+FFFD replacement character and silently loses the tail).
/// </summary>
internal sealed class Utf8ChunkDecoder
{
    // throwOnInvalidBytes: false - invalid input becomes U+FFFD via the replacement fallback
    // rather than throwing. A pty stream is not something we get to reject; every byte on the
    // wire must produce *something* on screen instead of tearing down the session.
    private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();

    /// <summary>
    /// Decodes <paramref name="bytes"/>, invoking <paramref name="sink"/> with each produced
    /// batch of chars. <paramref name="scratch"/> is caller-owned reusable char storage (no
    /// allocation here). Safe to call repeatedly with successive chunks of the same logical
    /// stream - a multi-byte sequence split across two calls decodes correctly.
    /// </summary>
    public void Decode(ReadOnlySpan<byte> bytes, Span<char> scratch, CharSpanAction sink)
    {
        while (!bytes.IsEmpty)
        {
            _decoder.Convert(bytes, scratch, flush: false, out var bytesUsed, out var charsProduced, out _);
            if (charsProduced > 0)
                sink(scratch[..charsProduced]);

            if (bytesUsed == 0)
            {
                // Convert can legitimately consume 0 bytes and produce 0 chars when the only
                // remaining bytes are the start of an incomplete multi-byte sequence and the
                // scratch buffer has room - it's waiting for more input. Stop here; the
                // unconsumed bytes are retained internally by the Decoder for the next call.
                break;
            }

            bytes = bytes[bytesUsed..];
        }
    }

    /// <summary>Flushes any pending partial sequence at end-of-stream, emitting a final U+FFFD
    /// for a truncated multi-byte sequence that will never be completed.</summary>
    public void Flush(Span<char> scratch, CharSpanAction sink)
    {
        _decoder.Convert(ReadOnlySpan<byte>.Empty, scratch, flush: true, out _, out var charsProduced, out _);
        if (charsProduced > 0)
            sink(scratch[..charsProduced]);
    }
}
