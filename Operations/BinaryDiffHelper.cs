using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Operations;

/// <summary>
/// Результат побайтового (offset-aligned) сравнения двух файлов.
/// Result of a byte-by-byte (offset-aligned) comparison of two files.
/// Хранит диапазоны отличающихся байт (смещение → длина) без загрузки
/// файлов в память. / Stores differing byte ranges (offset → length) without
/// loading files into memory.
/// </summary>
public sealed class BinaryDiffResult
{
    /// <summary>Диапазоны различий: (начало, конец) — полуинтервал [start, end). / Diff ranges (half-open).</summary>
    public List<(long start, long end)> Ranges { get; }

    /// <summary>Размер левого файла в байтах. / Left file size in bytes.</summary>
    public long LeftSize { get; }

    /// <summary>Размер правого файла в байтах. / Right file size in bytes.</summary>
    public long RightSize { get; }

    /// <summary>Суммарное число отличающихся байт. / Total number of differing bytes.</summary>
    public long DiffBytes { get; }

    /// <summary>Число отдельных диапазонов различий. / Number of distinct diff ranges.</summary>
    public int RangeCount => Ranges.Count;

    /// <summary>Файлы полностью идентичны (нет диапазонов различий). / Files are identical.</summary>
    public bool Identical => Ranges.Count == 0;

    public BinaryDiffResult(List<(long start, long end)> ranges, long leftSize, long rightSize, long diffBytes)
    {
        Ranges = ranges;
        LeftSize = leftSize;
        RightSize = rightSize;
        DiffBytes = diffBytes;
    }
}

/// <summary>
/// Потоковый движок бинарного сравнения (ph3.2, exp.yml).
/// Streaming binary compare engine (ph3.2, exp.yml).
/// Алгоритм: побайтовое offset-aligned сравнение по чанкам (~64 КБ) через
/// FileStream + SequenceEqual; большие файлы не загружаются в память целиком.
/// Algorithm: byte-by-byte offset-aligned compare in chunks (~64 KB) via
/// FileStream + SequenceEqual; large files are never fully loaded into memory.
/// BCL-only (System.IO).
/// </summary>
public static class BinaryDiffHelper
{
    /// <summary>Размер чанка сравнения: 64 КБ. / Compare chunk size: 64 KB.</summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>
    /// Определяет, является ли файл бинарным. Эвристика: наличие NUL-байта
    /// в выборке (первые 16 КБ) означает бинарный файл; текст NUL не содержит.
    /// Detects whether a file is binary. Heuristic: a NUL byte in the sample
    /// (first 16 KB) means binary; text never contains NUL.
    /// </summary>
    public static bool IsBinaryFile(string path, int sampleBytes = 16 * 1024)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
            if (fs.Length == 0) return false;
            int toRead = (int)Math.Min(sampleBytes, fs.Length);
            var buf = new byte[toRead];
            int read = ReadExact(fs, buf, toRead);
            for (int i = 0; i < read; i++)
                if (buf[i] == 0) return true;
            return false;
        }
        catch (Exception)
        {
            // При ошибке доступа считаем потенциально бинарным, чтобы не
            // пытаться грузить огромный файл в текстовый diff.
            // On access error treat as possibly binary to avoid loading a huge file as text.
            return true;
        }
    }

    /// <summary>
    /// Потоково сравнивает два файла и возвращает диапазоны различий.
    /// Streamingly compares two files and returns the differing ranges.
    /// Только общая часть (min размеров) сравнивается побайтово; хвост
    /// (если файлы разной длины) целиком считается отличием.
    /// Only the common part (min size) is compared byte-by-byte; the tail
    /// (if sizes differ) is treated as entirely different.
    /// </summary>
    public static async Task<BinaryDiffResult> CompareAsync(
        string leftPath, string rightPath,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        using var ls = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            ChunkSize, FileOptions.SequentialScan);
        using var rs = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            ChunkSize, FileOptions.SequentialScan);

        long leftLen = ls.Length;
        long rightLen = rs.Length;
        long common = Math.Min(leftLen, rightLen);

        var ranges = new List<(long start, long end)>();
        var lb = new byte[ChunkSize];
        var rb = new byte[ChunkSize];
        long i = 0;
        long? runStart = null;

        while (i < common)
        {
            ct.ThrowIfCancellationRequested();

            int toRead = (int)Math.Min(ChunkSize, common - i);
            int lr = ReadExact(ls, lb, toRead);
            int rr = ReadExact(rs, rb, toRead);

            for (int k = 0; k < toRead; k++)
            {
                if (lb[k] != rb[k])
                {
                    if (runStart is null) runStart = i + k;
                }
                else if (runStart is not null)
                {
                    ranges.Add((runStart.Value, i + k));
                    runStart = null;
                }
            }
            if (runStart is not null)
            {
                ranges.Add((runStart.Value, i + toRead));
                runStart = null;
            }

            i += toRead;
            progress?.Report(new OperationProgress(null, i, common, i, common, 0, 1));

            // Даём планировщику вздохнуть на очень больших файлах.
            // Yield to the scheduler on very large files.
            if ((i & 0x3FFFFF) == 0) await Task.Yield();
        }

        // Слияние диапазонов, разорванных только на границе чанков (зазор = 0).
        // Merge ranges split only at chunk boundaries (gap == 0).
        if (ranges.Count > 1)
        {
            var merged = new List<(long start, long end)>(ranges.Count) { ranges[0] };
            for (int idx = 1; idx < ranges.Count; idx++)
            {
                var last = merged[^1];
                var cur = ranges[idx];
                if (cur.start == last.end)
                    merged[^1] = (last.start, cur.end);
                else
                    merged.Add(cur);
            }
            ranges = merged;
        }

        // Хвост: один файл длиннее другого — вся разница длины отличается.
        // Добавляется отдельным диапазоном, не сливается с последним diff-диапазоном.
        // Tail: one file is longer — the whole length difference differs.
        // Added as a separate range, never merged with the last content-diff range.
        if (leftLen != rightLen)
        {
            ranges.Add((common, Math.Max(leftLen, rightLen)));
        }

        long diffBytes = 0;
        foreach (var r in ranges) diffBytes += r.end - r.start;

        return new BinaryDiffResult(ranges, leftLen, rightLen, diffBytes);
    }

    /// <summary>
    /// Читает ровно <paramref name="count"/> байт (или меньше при EOF) в буфер.
    /// Reads exactly <paramref name="count"/> bytes (or fewer at EOF) into the buffer.
    /// </summary>
    private static int ReadExact(Stream s, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buffer, total, count - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
