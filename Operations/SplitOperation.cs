using System.Globalization;
using System.IO.Hashing;
using System.Text;
using CoderCommander.FileSystem;
using CoderCommander.Services;

#pragma warning disable CA1308 // CRC hash hex string is written to .crc file in conventional lowercase
namespace CoderCommander.Operations;

/// <summary>
/// Splits one or more files into numbered <c>.NNN</c> parts (TC-style: <c>name.ext.001</c>,
/// <c>.002</c>, ...) of a fixed size, streaming the source once per file rather than re-opening/
/// seeking per part - see <see cref="BoundedReadStream"/>. Optionally writes a companion
/// <c>.crc</c> file next to the parts with the whole (unsplit) file's CRC32, which
/// <see cref="CombineOperation"/> can verify against after reassembly.
/// </summary>
public sealed class SplitOperation : FileOperation
{
    public override OperationType Type => OperationType.Split;
    public override string Title => "Split";

    private readonly IFileSystem _fs;
    private readonly IReadOnlyList<FileEntry> _files;
    private readonly string _destDir;
    private readonly long _partSizeBytes;
    private readonly bool _writeCrc;
    private readonly bool _deleteSourceAfter;
    private readonly TransferOptions _options;

    private int _filesProcessed;
    private int _filesTotal;
    private long _bytesProcessed;
    private long _bytesTotal;

    /// <summary>Creates a split operation writing parts of <paramref name="partSizeBytes"/> bytes
    /// into <paramref name="destDir"/> for every non-directory entry in <paramref name="files"/>.</summary>
    public SplitOperation(
        IFileSystem fs,
        IReadOnlyList<FileEntry> files,
        string destDir,
        long partSizeBytes,
        bool writeCrc,
        bool deleteSourceAfter,
        TransferOptions? options = null)
    {
        if (partSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(partSizeBytes), "Part size must be positive.");

        _fs = fs;
        _files = files;
        _destDir = destDir;
        _partSizeBytes = partSizeBytes;
        _writeCrc = writeCrc;
        _deleteSourceAfter = deleteSourceAfter;
        _options = options ?? new TransferOptions();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var targets = _files.Where(f => !f.IsDirectory).ToList();
        if (targets.Count == 0)
            return;

        _filesTotal = targets.Count;
        _bytesTotal = targets.Sum(f => f.Size);

        // Collected, not thrown-per-file, so one bad file doesn't abort the rest of a multi-select
        // split - same idiom as PackOperation.RemoveSourcesAsync / CopyOperation's failure list.
        var failures = new List<string>();
        var splitOk = new List<FileEntry>();

        foreach (var file in targets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await SplitFileAsync(file, ct).ConfigureAwait(false);
                splitOk.Add(file);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogService.Warning($"Split: cannot split {file.FullPath}: {ex.Message}");
                failures.Add(file.Name);
            }
            _filesProcessed++;
        }

        if (_deleteSourceAfter)
        {
            foreach (var file in splitOk)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await _fs.DeleteAsync(file.FullPath, recursive: false, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogService.Warning($"Split: cannot remove source {file.FullPath}: {ex.Message}");
                    failures.Add($"{file.Name} (source not removed)");
                }
            }
        }

        if (failures.Count > 0)
            throw new IOException(
                $"Split finished, but {failures.Count} item(s) had problems: {string.Join(", ", failures.Take(5))}" +
                (failures.Count > 5 ? $" and {failures.Count - 5} more" : ""));
    }

    private async Task SplitFileAsync(FileEntry file, CancellationToken ct)
    {
        var partCountLong = Math.Max(1, (file.Size + _partSizeBytes - 1) / _partSizeBytes);
        if (partCountLong > int.MaxValue)
            throw new IOException("Too many parts for this file/part-size combination.");
        var partCount = (int)partCountLong;
        var digits = Math.Max(3, partCount.ToString(CultureInfo.InvariantCulture).Length);
        var crc = _writeCrc ? new Crc32() : null;

        using var source = await _fs.OpenReadAsync(file.FullPath, ct).ConfigureAwait(false);

        var writtenParts = new List<string>();

        try
        {
            for (var partNum = 1; partNum <= partCount; partNum++)
            {
                ct.ThrowIfCancellationRequested();

                var thisPartSize = Math.Min(_partSizeBytes, file.Size - (long)(partNum - 1) * _partSizeBytes);
                var partName = $"{file.Name}.{partNum.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0')}";
                var destPath = VfsPath.Combine(_destDir, partName);

                var resolution = await ConflictResolver.ResolveAsync(_fs, file.FullPath, destPath, file, _options, ct).ConfigureAwait(false);
                if (!resolution.Proceed)
                    throw new IOException($"Part \"{partName}\" was skipped - split aborted (a partial split has no value).");

                using var bounded = new BoundedReadStream(source, thisPartSize);
                using var tracking = new TrackingReadStream(bounded, crc, chunk =>
                {
                    _bytesProcessed += chunk;
                    ReportThrottled(() => ReportProgress(file.Name));
                });

                await _fs.CopyFromStreamAsync(resolution.TargetPath, tracking, ct).ConfigureAwait(false);
                writtenParts.Add(resolution.TargetPath);
            }

            if (crc != null)
                await WriteCrcFileAsync(file, crc.GetCurrentHash(), ct).ConfigureAwait(false);

            ReportProgress(file.Name);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            foreach (var p in writtenParts)
            {
                try { await _fs.DeleteAsync(p, false, ct).ConfigureAwait(false); }
                catch { /* best-effort cleanup of partial parts */ }
            }
            throw;
        }
    }

    private async Task WriteCrcFileAsync(FileEntry file, byte[] hash, CancellationToken ct)
    {
        var crcHex = Convert.ToHexString(hash).ToLowerInvariant();
        var line = $"{file.Name} {file.Size.ToString(CultureInfo.InvariantCulture)} {crcHex}\n";
        var crcPath = VfsPath.Combine(_destDir, file.Name + ".crc");
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(line));
        await _fs.CopyFromStreamAsync(crcPath, ms, ct).ConfigureAwait(false);
    }

    private void ReportProgress(string currentFile)
    {
        Report(new OperationProgress
        {
            Percent = _bytesTotal > 0
                ? (int)Math.Min(100, _bytesProcessed * 100 / _bytesTotal)
                : (_filesTotal > 0 ? _filesProcessed * 100 / _filesTotal : 0),
            CurrentFile = currentFile,
            BytesProcessed = _bytesProcessed,
            BytesTotal = _bytesTotal,
            FilesProcessed = _filesProcessed,
            FilesTotal = _filesTotal
        });
    }

    /// <summary>Read-only pass-through that both feeds a running <see cref="Crc32"/> (whole
    /// original file, spanning every part - not reset per part) and reports byte counts, without
    /// disposing the inner <see cref="BoundedReadStream"/> slice itself (that's the caller's job,
    /// same non-ownership contract <see cref="BoundedReadStream"/> has over its own inner stream).</summary>
    // CA2213: _inner is intentionally not disposed — the caller owns the BoundedReadStream
    // slice, same non-ownership contract BoundedReadStream has over its own inner stream.
#pragma warning disable CA2213
    private sealed class TrackingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly Crc32? _crc;
        private readonly Action<int> _onRead;

        public TrackingReadStream(Stream inner, Crc32? crc, Action<int> onRead)
        {
            _inner = inner;
            _crc = crc;
            _onRead = onRead;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            if (read > 0)
            {
                _crc?.Append(buffer.AsSpan(offset, read));
                _onRead(read);
            }
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read > 0)
            {
                _crc?.Append(buffer.Span[..read]);
                _onRead(read);
            }
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
