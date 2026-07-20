using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Operations;

/// <summary>
/// Формат экспорта sum-файла. / Export format for the sum file.
/// </summary>
public enum ChecksumFormat
{
    /// <summary>SFV (CRC-32, по файлам). / SFV (CRC-32, per file).</summary>
    Sfv,
    /// <summary>md5sum. / md5sum format.</summary>
    Md5,
    /// <summary>sha1sum. / sha1sum format.</summary>
    Sha1,
    /// <summary>sha256sum. / sha256sum format.</summary>
    Sha256,
    /// <summary>sha512sum. / sha512sum format.</summary>
    Sha512
}

/// <summary>
/// Статические помощники для расчёта/парсинга/экспорта контрольных сумм (ph1.2, exp.yml).
/// Static helpers for checksum computation, parsing and export (ph1.2).
/// Потоковый расчёт без полной загрузки файла в память.
/// Streaming computation — the whole file is never loaded into memory at once.
/// </summary>
public static class ChecksumHelper
{
    private const int BufferSize = 1 << 20; // 1 МБ

    /// <summary>Возвращает формат экспорта, соответствующий алгоритму. / Export format for an algorithm.</summary>
    public static ChecksumFormat FormatFor(ChecksumAlgorithm algo) => algo switch
    {
        ChecksumAlgorithm.MD5 => ChecksumFormat.Md5,
        ChecksumAlgorithm.SHA1 => ChecksumFormat.Sha1,
        ChecksumAlgorithm.SHA256 => ChecksumFormat.Sha256,
        ChecksumAlgorithm.SHA512 => ChecksumFormat.Sha512,
        _ => ChecksumFormat.Sfv
    };

    /// <summary>
    /// Потоково вычисляет хеш файла в виде hex-строки.
    /// Streamingly computes the file hash as a hex string.
    /// </summary>
    public static async Task<string> ComputeHashAsync(string path, ChecksumAlgorithm algo, IProgress<long>? byteProgress, CancellationToken ct)
    {
        if (algo == ChecksumAlgorithm.CRC32)
        {
            var crc = new Crc32();
            var buf = new byte[BufferSize];
            using var s = File.OpenRead(path);
            long done = 0;
            int read;
            while ((read = await s.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
            {
                crc.Append(buf.AsSpan(0, read));
                done += read;
                byteProgress?.Report(done);
            }
            return ToHex(crc.GetHashAndReset());
        }

#pragma warning disable SYSLIB0021 // MD5/SHA1 помечены устаревшими, но требуются по ТЗ / marked obsolete but required by spec
        using HashAlgorithm ha = algo switch
        {
            ChecksumAlgorithm.MD5 => MD5.Create(),
            ChecksumAlgorithm.SHA1 => SHA1.Create(),
            ChecksumAlgorithm.SHA256 => SHA256.Create(),
            ChecksumAlgorithm.SHA512 => SHA512.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(algo))
        };
#pragma warning restore SYSLIB0021

        var buf2 = new byte[BufferSize];
        using var stream = File.OpenRead(path);
        long done2 = 0;
        int r;
        while ((r = await stream.ReadAsync(buf2.AsMemory(0, buf2.Length), ct).ConfigureAwait(false)) > 0)
        {
            ha.TransformBlock(buf2, 0, r, null, 0);
            done2 += r;
            byteProgress?.Report(done2);
        }
        ha.TransformFinalBlock(buf2, 0, 0);
        return ToHex(ha.Hash ?? Array.Empty<byte>());
    }

    /// <summary>Переводит байты в hex (нижний регистр). / Converts bytes to lowercase hex.</summary>
    public static string ToHex(byte[] data) => Convert.ToHexString(data).ToLowerInvariant();

    /// <summary>
    /// Формирует одну строку sum-файла в стандартном формате.
    /// Builds a single standard sum-file line.
    /// Для *sum: "&lt;hash&gt;  &lt;filename&gt;" (текстовый режим GNU) или "&lt;hash&gt; *&lt;filename&gt;" (бинарный).
    /// For *sum: "<hash>  <filename>" (GNU text) or "<hash> *<filename>" (binary).
    /// Для SFV: "&lt;filename&gt; &lt;hash&gt;" (8 hex для CRC32).
    /// For SFV: "<filename> <hash>" (8 hex for CRC32).
    /// </summary>
    public static string FormatLine(ChecksumFormat format, string hashHex, string fileName)
    {
        if (format == ChecksumFormat.Sfv)
            return $"{fileName} {hashHex.ToLowerInvariant()}";
        // GNU coreutils: два пробела для текстового режима / two spaces for text mode
        return $"{hashHex}  {fileName}";
    }

    /// <summary>
    /// Парсит содержимое sum-файла, возвращая пары (hash, fileName).
    /// Parses sum-file content into (hash, fileName) pairs.
    /// Поддерживаются форматы GNU (*sum с "*" или двумя пробелами) и SFV (имя+хеш).
    /// Supports GNU (*sum with "*" or two spaces) and SFV (name+hash) formats.
    /// </summary>
    public static IReadOnlyList<(string Hash, string FileName)> ParseSumFile(string content, ChecksumAlgorithm expected)
    {
        var result = new List<(string, string)>();
        foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

            // GNU-формат с бинарным маркером '*' или текстовым ' '
            // GNU format with binary marker '*' or text ' '
            var star = line.IndexOf('*');
            if (star > 0 && line[star - 1] != '\\' && IsHex(line.AsSpan(0, star)))
            {
                result.Add((line.Substring(0, star).ToLowerInvariant(), line.Substring(star + 1)));
                continue;
            }

            // Разбиваем по пробелам
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (IsHex(parts[0]))
                result.Add((parts[0].ToLowerInvariant(), string.Join(" ", parts.Skip(1))));
            else
                result.Add((parts[^1].ToLowerInvariant(), string.Join(" ", parts.SkipLast(1))));
        }
        return result;
    }

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        if (s.Length is not (8 or 32 or 40 or 64 or 128)) return false;
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}
