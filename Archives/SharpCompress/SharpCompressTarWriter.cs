using SharpCompress.Common;
using SharpCompress.Writers.Tar;

namespace CoderCommander.Archives.SharpCompress;

/// <summary>
/// Writes TAR entries through SharpCompress's own <see cref="TarWriter"/> instead of
/// <c>System.Formats.Tar</c> - the primitive <see cref="RewritingArchiveWriter"/> uses for both the
/// staging file and the final rewritten archive, mirroring what <c>TarSequentialWriter</c> does for
/// the built-in TAR/TAR.GZ formats.
/// <para>
/// Only for <see cref="CompressionType.BZip2"/>: confirmed by hand against SharpCompress 0.50.3 that
/// its <see cref="TarWriter"/> explicitly rejects <c>Xz</c>/<c>LZMA</c>/<c>LZMA2</c> with an
/// <c>InvalidFormatException</c> ("Tar does not support compression: Xz") - there is no XZ encoder
/// in this version of the library (<c>SharpCompress.Compressors.Xz</c> only has read-side types), so
/// TAR.XZ has no writable path today and stays read-only via <see cref="SharpCompressReader"/>.
/// </para>
/// <para>
/// The overload that takes an explicit size writes straight from whatever <see cref="Stream"/> it's
/// given without needing to seek it first (confirmed against a genuinely non-seekable stream), so
/// unlike <c>TarSequentialWriter</c> this doesn't need to buffer through a temp file to learn the
/// entry's length.
/// </para>
/// </summary>
internal sealed class SharpCompressTarWriter : ISequentialArchiveWriter
{
    private readonly TarWriter _writer;

    public SharpCompressTarWriter(Stream output, CompressionType compression)
    {
        var options = new TarWriterOptions(compression, finalizeArchiveOnClose: true) { LeaveStreamOpen = true };
        _writer = new TarWriter(output, options);
    }

    public void WriteDirectory(string entryName, DateTime lastWriteTimeUtc) =>
        _writer.WriteDirectory(NormalizeName(entryName, isDirectory: true), ToTimestamp(lastWriteTimeUtc));

    public Task WriteFileAsync(
        string entryName,
        Stream content,
        long size,
        DateTime lastWriteTimeUtc,
        ArchiveCompressionSpec compression,
        CancellationToken ct)
    {
        _writer.Write(NormalizeName(entryName, isDirectory: false), content, ToTimestamp(lastWriteTimeUtc), size);
        return Task.CompletedTask;
    }

    private static string NormalizeName(string name, bool isDirectory)
    {
        var normalized = name.Replace('\\', '/').Trim('/');
        return isDirectory ? normalized + "/" : normalized;
    }

    private static DateTime? ToTimestamp(DateTime value) => value == default ? null : value;

    public void Dispose() => _writer.Dispose();
}
