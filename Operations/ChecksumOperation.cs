using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Операция расчёта и проверки контрольных сумм (ph1.2, exp.yml):
/// MD5/SHA1/SHA256/SHA512 (BCL) + CRC32 (System.IO.Hashing), потоково.
/// Checksum calculation and verification operation (ph1.2):
/// MD5/SHA1/SHA256/SHA512 (BCL) + CRC32 (System.IO.Hashing), streaming.
/// </summary>
public sealed class ChecksumOperation : FileOperation
{
    private readonly ChecksumAlgorithm _algo;
    private readonly List<string> _files;
    private readonly string? _exportPath;
    private readonly string? _verifyPath;
    private readonly ConcurrentBag<ChecksumResult> _results = new();

    private int _total;
    private int _done;
    private long _totalBytes;
    private long _doneBytes;

    /// <summary>Результаты по каждому файлу. / Per-file results.</summary>
    public IReadOnlyCollection<ChecksumResult> Results => _results;

    /// <summary>Режим проверки (true), иначе расчёт. / Verify mode (true), otherwise calculate.</summary>
    public bool IsVerify => _verifyPath != null;

    /// <summary>Список несовпавших при проверке. / Mismatches detected during verification.</summary>
    public IReadOnlyList<ChecksumResult> Mismatches => _results.Where(r => r.IsMismatch).ToList();

    /// <summary>
    /// Создаёт операцию. / Creates the operation.
    /// </summary>
    /// <param name="algorithm">Алгоритм. / Algorithm.</param>
    /// <param name="files">Файлы для расчёта. / Files to calculate.</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    /// <param name="exportPath">Путь экспорта sum-файла (необязательно). / Export sum-file path.</param>
    /// <param name="verifyPath">Путь sum-файла для проверки (необязательно). / Sum-file path to verify.</param>
    public ChecksumOperation(
        ChecksumAlgorithm algorithm, IEnumerable<string> files,
        IProgress<OperationProgress>? progress = null, string? exportPath = null, string? verifyPath = null)
        : base(progress)
    {
        _algo = algorithm;
        _files = files.ToList();
        _exportPath = exportPath;
        _verifyPath = verifyPath;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        if (IsVerify) await VerifyAsync(ct).ConfigureAwait(false);
        else await CalcAsync(ct).ConfigureAwait(false);

        if (_exportPath != null && !IsVerify) await ExportAsync(ct).ConfigureAwait(false);
    }

    private async Task CalcAsync(CancellationToken ct)
    {
        _total = _files.Count;
        _totalBytes = _files.Sum(p => { try { return new FileInfo(p).Length; } catch { return 0L; } });

        var indexed = _files.Select((f, i) => (f, i)).ToList();
        var arr = new ChecksumResult[_files.Count];

        await Parallel.ForEachAsync(indexed,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) },
            async (item, cti) =>
            {
                var (file, idx) = item;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var size = SafeSize(file);
                long prev = 0;
                var bp = new Progress<long>(cur => { Interlocked.Add(ref _doneBytes, cur - prev); prev = cur; });
                string hash;
                try { hash = await ChecksumHelper.ComputeHashAsync(file, _algo, bp, cti).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogService.Error($"Checksum failed: {file}: {ex.Message}", nameof(ChecksumOperation), ex);
                    hash = string.Empty;
                }
                Interlocked.Add(ref _doneBytes, size - prev);
                Interlocked.Increment(ref _done);
                arr[idx] = new ChecksumResult(file, hash, size, sw.Elapsed);
                Report(new OperationProgress(file, size, size, _doneBytes, _totalBytes, _done, _total));
            }).ConfigureAwait(false);

        foreach (var r in arr) _results.Add(r);
    }

    private async Task VerifyAsync(CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(_verifyPath!, ct).ConfigureAwait(false);
        var entries = ChecksumHelper.ParseSumFile(content, _algo);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(_verifyPath!)) ?? ".";

        _total = entries.Count;
        _totalBytes = entries.Sum(e => SafeSize(Path.Combine(baseDir, e.FileName)));

        var arr = new ChecksumResult[entries.Count];
        await Parallel.ForEachAsync(entries.Select((e, i) => (e, i)),
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) },
            async (item, cti) =>
            {
                var (entry, idx) = item;
                var full = Path.Combine(baseDir, entry.FileName);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var size = SafeSize(full);
                long prev = 0;
                var bp = new Progress<long>(cur => { Interlocked.Add(ref _doneBytes, cur - prev); prev = cur; });

                string? computed = null;
                bool? match = false;
                if (File.Exists(full))
                {
                    try
                    {
                        computed = await ChecksumHelper.ComputeHashAsync(full, _algo, bp, cti).ConfigureAwait(false);
                        match = string.Equals(computed, entry.Hash, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { LogService.Error($"Verify failed: {full}: {ex.Message}", nameof(ChecksumOperation), ex); }
                }
                Interlocked.Add(ref _doneBytes, size - prev);
                Interlocked.Increment(ref _done);
                arr[idx] = new ChecksumResult(full, computed ?? string.Empty, size, sw.Elapsed, entry.Hash, match);
                Report(new OperationProgress(full, size, size, _doneBytes, _totalBytes, _done, _total));
            }).ConfigureAwait(false);

        foreach (var r in arr) _results.Add(r);
    }

    private async Task ExportAsync(CancellationToken ct)
    {
        var format = ChecksumHelper.FormatFor(_algo);
        var lines = _results
            .Where(r => !string.IsNullOrEmpty(r.Hash))
            .OrderBy(r => r.FilePath)
            .Select(r => ChecksumHelper.FormatLine(format, r.Hash, Path.GetFileName(r.FilePath)));
        await File.WriteAllLinesAsync(_exportPath!, lines, ct).ConfigureAwait(false);
    }

    private static long SafeSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0L; }
    }
}
