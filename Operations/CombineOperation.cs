using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Операция склейки томов обратно в исходный файл байт-в-байт (ph1.3, exp.yml).
/// Combine operation that joins volumes back into the original file byte-by-byte (ph1.3).
/// Читает summary-файл «size=…» (при наличии) и склеивает «имя.001», «имя.002» … .
/// Reads the "size=…" summary file (if present) and concatenates "name.001", "name.002" … .
/// Буферизованный FileStream (~1 МБ), прогресс через IProgress, CancellationToken.
/// Buffered FileStream (~1 MB), IProgress reporting, CancellationToken support.
/// </summary>
public sealed class CombineOperation : FileOperation
{
    private const int BufferSize = 1 << 20; // 1 МБ / 1 MB

    private static readonly Regex VolumeSuffix = new(@"^\.(\d+)$", RegexOptions.Compiled);

    private readonly string _input; // путь к summary (.sum) либо к первому тому (.001)
    private long _totalBytes;
    private long _doneBytes;

    /// <summary>Итоговый путь восстановленного файла. / Restored output file path.</summary>
    public string? OutputPath { get; private set; }

    /// <summary>Ожидаемый размер файла (из summary или суммы томов). / Expected size (from summary or sum of volumes).</summary>
    public long ExpectedSize { get; private set; }

    /// <summary>Число склеенных томов. / Number of volumes combined.</summary>
    public int VolumesCombined { get; private set; }

    /// <summary>
    /// Создаёт операцию склейки. / Creates a combine operation.
    /// </summary>
    /// <param name="input">
    /// Путь к summary-файлу (<c>.sum</c>) ИЛИ к первому тому (<c>.001</c>).
    /// Path to the summary file (.sum) OR to the first volume (.001).
    /// </param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    public CombineOperation(string input, IProgress<OperationProgress>? progress = null)
        : base(progress) => _input = input;

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        var full = Path.GetFullPath(_input);
        var dir = Path.GetDirectoryName(full) ?? ".";
        var name = Path.GetFileName(full);

        string baseName;
        string summaryPath;

        if (name.EndsWith(".sum", StringComparison.OrdinalIgnoreCase))
        {
            // Передан сам summary-файл. / The summary file itself was passed.
            baseName = name.Substring(0, name.Length - 4);
            summaryPath = full;
            ExpectedSize = ParseSummary(summaryPath);
        }
        else if (TryStripVolumeIndex(name, out var stripped))
        {
            // Передан том «имя.NNN» — ищем рядом summary, иначе размер по сумме томов.
            // A "name.NNN" volume was passed — look for a sibling summary, else sum volumes.
            baseName = stripped;
            summaryPath = Path.Combine(dir, baseName + ".sum");
            ExpectedSize = File.Exists(summaryPath) ? ParseSummary(summaryPath) : 0L;
        }
        else
        {
            throw new ArgumentException(
                $"Неизвестный входной файл для склейки: {name} / Unknown combine input: {name}", nameof(_input));
        }

        OutputPath = Path.Combine(dir, baseName);

        var volumes = EnumerateVolumes(dir, baseName).ToList();
        if (volumes.Count == 0)
            throw new FileNotFoundException(
                $"Тома не найдены рядом с «{baseName}» / Volumes not found for «{baseName}»", OutputPath);

        _totalBytes = ExpectedSize > 0
            ? ExpectedSize
            : volumes.Sum(v => { try { return new FileInfo(v).Length; } catch { return 0L; } });

        var buffer = new byte[BufferSize];
        var output = new FileStream(OutputPath, FileMode.Create, FileAccess.Write, FileShare.None,
            BufferSize, FileOptions.SequentialScan);
        try
        {
            foreach (var vol in volumes)
            {
                ct.ThrowIfCancellationRequested();
                using var input = new FileStream(vol, FileMode.Open, FileAccess.Read, FileShare.Read,
                    BufferSize, FileOptions.SequentialScan);
                int read;
                while ((read = await input.ReadAsync(buffer, 0, BufferSize, ct).ConfigureAwait(false)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    await output.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    _doneBytes += read;
                    Report(new OperationProgress(vol, _doneBytes, _totalBytes, _doneBytes, _totalBytes, 1, 1));
                }
                VolumesCombined++;
            }

            await output.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            output.Dispose();
            try { File.Delete(OutputPath); } catch { /* best-effort cleanup */ }
            throw;
        }
        finally { output.Dispose(); }
    }

    private static IEnumerable<string> EnumerateVolumes(string dir, string baseName)
    {
        var prefix = baseName + ".";
        return Directory.GetFiles(dir)
            .Select(f => Path.GetFileName(f))
            .Where(fn => fn.Length > prefix.Length && fn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(fn => VolumeSuffix.IsMatch(fn.Substring(baseName.Length))) // суффикс ".NNN"
            .OrderBy(fn => fn, StringComparer.OrdinalIgnoreCase)             // равная ширина → лексикографический = числовой
            .Select(fn => Path.Combine(dir, fn));
    }

    private static long ParseSummary(string summaryPath)
    {
        // Формат: "size=<число>" (одна или несколько строк).
        // Format: "size=<number>" (one or more lines).
        foreach (var raw in File.ReadAllLines(summaryPath))
        {
            var line = raw.Trim();
            if (line.StartsWith("size=", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(line.Substring(5).Trim(), out var size))
                return size;
        }
        LogService.Warn($"Combine: summary без size=: {summaryPath}", nameof(CombineOperation));
        return 0L;
    }

    private static bool TryStripVolumeIndex(string name, out string baseName)
    {
        var idx = name.LastIndexOf('.');
        if (idx <= 0) { baseName = name; return false; }
        var suffix = name.Substring(idx + 1);
        if (suffix.Length > 0 && suffix.All(char.IsDigit))
        {
            baseName = name.Substring(0, idx);
            return true;
        }
        baseName = name;
        return false;
    }
}
