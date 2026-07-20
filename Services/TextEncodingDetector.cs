using System.Text;

namespace CoderCommander.Services;

/// <summary>Detects a text file's encoding from its byte-order-mark. Shared by the viewer and the editor
/// so both agree on what "the file's encoding" means, and so a round-trip load/save doesn't add or
/// duplicate a BOM the original file didn't have.</summary>
public static class TextEncodingDetector
{
    /// <summary>Returns the detected encoding; <paramref name="preambleLength"/> is how many leading bytes
    /// are the BOM (0 if none was found) and must be skipped before decoding the rest as text.</summary>
    public static Encoding Detect(byte[] data, out int preambleLength)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            preambleLength = 3;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            preambleLength = 2;
            return Encoding.Unicode;
        }
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            preambleLength = 2;
            return Encoding.BigEndianUnicode;
        }
        preambleLength = 0;
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
}
