using System.Globalization;
using System.IO.Hashing;
using System.Text.RegularExpressions;
using CoderCommander.FileSystem;

namespace CoderCommander.Operations;

/// <summary>
/// Reassembles a file previously split by <see cref="SplitOperation"/> (or any other tool using
/// the same <c>name.ext.001</c>/<c>.002</c>/... convention) back into a single file. Discovers
/// sibling parts by directory listing rather than trusting the caller to enumerate them, refuses
/// to proceed if any part in the numeric sequence is missing, and streams the reassembly through
/// <see cref="ConcatenatingReadStream"/> - a single <see cref="IFileSystem.CopyFromStreamAsync"/>
/// call, so it works on a destination with no <c>Seek</c>/<c>OpenWriteAsync</c> too.
/// </summary>
public sealed partial class CombineOperation : FileOperation
{
    public override OperationType Type => OperationType.Combine;
    public override string Title => "Combine";

    private static readonly Regex PartNameRegex = BuildPartNameRegex();

    [GeneratedRegex(@"^(?<base>.+)\.(?<num>\d{3,})$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildPartNameRegex();

    private readonly IFileSystem _fs;
    private readonly string _firstPartPath;
    private readonly string _destPath;
    private readonly bool _verifyCrc;
    private readonly bool _deleteSourceAfter;

    private long _bytesProcessed;
    private long _bytesTotal;

    /// <summary>Result of the CRC check, if <see cref="_verifyCrc"/> was requested and a
    /// <c>.crc</c> sidecar was found. Null when verification wasn't requested or no sidecar
    /// existed - not a failure either way, just "nothing to compare against".</summary>
    public bool? CrcVerified { get; private set; }

    /// <summary>Creates a combine operation that reassembles the part-sequence starting at
    /// <paramref name="firstPartPath"/> into <paramref name="destPath"/>.</summary>
    public CombineOperation(IFileSystem fs, string firstPartPath, string destPath, bool verifyCrc, bool deleteSourceAfter)
    {
        _fs = fs;
        _firstPartPath = firstPartPath;
        _destPath = destPath;
        _verifyCrc = verifyCrc;
        _deleteSourceAfter = deleteSourceAfter;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var (baseName, parts) = await DiscoverPartsAsync(ct).ConfigureAwait(false);

        foreach (var part in parts)
            _bytesTotal += (await _fs.GetFileInfoAsync(part, ct).ConfigureAwait(false))?.Size ?? 0;

        var crc = _verifyCrc ? new Crc32() : null;
        try
        {
            using (var combined = new ConcatenatingReadStream(_fs, parts, chunk =>
            {
                crc?.Append(chunk.Span);
                _bytesProcessed += chunk.Length;
                ReportThrottled(() => ReportProgress(baseName));
            }, ct))
            {
                await _fs.CopyFromStreamAsync(_destPath, combined, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            try { await _fs.DeleteAsync(_destPath, false, ct).ConfigureAwait(false); }
            catch { /* best-effort cleanup of partial output */ }
            throw;
        }

        ReportProgress(baseName);

        if (crc != null)
            CrcVerified = await VerifyCrcAsync(baseName, crc.GetCurrentHash(), ct).ConfigureAwait(false);

        if (_deleteSourceAfter)
        {
            var failures = new List<string>();
            foreach (var part in parts)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    await _fs.DeleteAsync(part, recursive: false, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(VfsPath.GetName(part));
                }
            }
            if (failures.Count > 0)
                throw new IOException(
                    $"Combined successfully, but {failures.Count} part(s) could not be removed: {string.Join(", ", failures.Take(5))}" +
                    (failures.Count > 5 ? $" and {failures.Count - 5} more" : ""));
        }
    }

    /// <summary>Parses <see cref="_firstPartPath"/>'s name, finds every sibling
    /// <c>&lt;base&gt;.NNN</c> in the same directory, and verifies the numeric sequence has no
    /// gaps starting from the first part's own number (not necessarily 1 - the user may have
    /// selected a later part).</summary>
    private async Task<(string BaseName, List<string> Parts)> DiscoverPartsAsync(CancellationToken ct)
    {
        var firstName = VfsPath.GetName(_firstPartPath);
        var match = PartNameRegex.Match(firstName);
        if (!match.Success)
            throw new IOException($"\"{firstName}\" doesn't look like a split part (expected a name ending in \".NNN\").");

        var baseName = match.Groups["base"].Value;
        var parentDir = VfsPath.GetParent(_firstPartPath);
        var siblings = await _fs.EnumerateAsync(parentDir, includeHidden: true, ct).ConfigureAwait(false);

        var numbered = new SortedDictionary<int, string>();
        foreach (var entry in siblings)
        {
            if (entry.IsDirectory) continue;
            var m = PartNameRegex.Match(entry.Name);
            if (!m.Success) continue;
            if (!string.Equals(m.Groups["base"].Value, baseName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(m.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var num)) continue;
            numbered[num] = entry.FullPath;
        }

        if (numbered.Count == 0)
            throw new IOException($"No part files found for \"{baseName}\".");

        var startNum = int.Parse(match.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        var parts = new List<string>();
        var expected = startNum;
        foreach (var (num, path) in numbered)
        {
            if (num < startNum) continue; // parts before the selected first one are not this file's
            if (num != expected)
                throw new IOException($"Part .{expected:D3} is missing - cannot combine \"{baseName}\" reliably.");
            parts.Add(path);
            expected++;
        }

        return (baseName, parts);
    }

    /// <summary>Looks for a <c>&lt;baseName&gt;.crc</c> sidecar written by <see cref="SplitOperation"/>
    /// next to the parts and, if found, compares its recorded CRC32 to <paramref name="actualHash"/>.
    /// Returns null (not false) when no sidecar exists - "couldn't verify" is not the same claim as
    /// "verification failed", and the combine itself has already succeeded either way.</summary>
    private async Task<bool?> VerifyCrcAsync(string baseName, byte[] actualHash, CancellationToken ct)
    {
        var parentDir = VfsPath.GetParent(_firstPartPath);
        var crcPath = VfsPath.Combine(parentDir, baseName + ".crc");
        if (!await _fs.ExistsAsync(crcPath, ct).ConfigureAwait(false))
            return null;

        string firstLine;
        using (var stream = await _fs.OpenReadAsync(crcPath, ct).ConfigureAwait(false))
        using (var reader = new StreamReader(stream))
            firstLine = await reader.ReadLineAsync(ct).ConfigureAwait(false) ?? "";

        var tokens = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return null;

        var expectedHex = tokens[^1];
        var actualHex = Convert.ToHexString(actualHash);
        return string.Equals(expectedHex, actualHex, StringComparison.OrdinalIgnoreCase);
    }

    private void ReportProgress(string currentFile)
    {
        Report(new OperationProgress
        {
            Percent = _bytesTotal > 0 ? (int)Math.Min(100, _bytesProcessed * 100 / _bytesTotal) : 0,
            CurrentFile = currentFile,
            BytesProcessed = _bytesProcessed,
            BytesTotal = _bytesTotal,
            FilesProcessed = _bytesProcessed >= _bytesTotal ? 1 : 0,
            FilesTotal = 1
        });
    }
}
