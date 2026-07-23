using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Безопасное удаление (Wipe) по методу DoD 5220.22-M (ph1.4, exp.yml):
/// переименование → перезапись проходами 0x00 / 0xFF / случайными байтами →
/// truncate → delete. Опциональное число проходов (по умолчанию 3).
/// Secure deletion (Wipe) following DoD 5220.22-M: rename → overwrite passes
/// (0x00 / 0xFF / random) → truncate → delete. Optional pass count (default 3).
/// Реализация целиком на BCL (System.IO + System.Security.Cryptography).
/// Implemented entirely with BCL (System.IO + System.Security.Cryptography).
/// </summary>
public sealed class WipeOperation : FileOperation
{
    private enum FillPattern { Zero, FF, Random }

    private readonly List<string> _targets;
    private readonly int _passes;
    private readonly bool _renameBeforeWipe;

    private long _totalBytes;
    private long _doneBytes;
    private long _totalItems;
    private long _doneItems;
    private long _failed;
    private readonly List<string> _errors = new();

    /// <summary>Число успешно вытертых элементов. / Number of successfully wiped items.</summary>
    public long Wiped => _doneItems;

    /// <summary>Число неудач. / Number of failures.</summary>
    public long Failed => _failed;

    /// <summary>Список описаний ошибок. / List of error descriptions.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>
    /// Создаёт операцию безопасного удаления.
    /// Creates a secure-deletion operation.
    /// </summary>
    /// <param name="targets">Цели (файлы или каталоги). / Targets (files or directories).</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    /// <param name="passes">Число проходов перезаписи (по умолчанию 3). / Overwrite passes (default 3).</param>
    /// <param name="renameBeforeWipe">Переименовать файл перед затиркой (скрыть имя). / Rename the file before wiping (obscure name).</param>
    public WipeOperation(IEnumerable<string> targets, IProgress<OperationProgress>? progress = null, int passes = 3, bool renameBeforeWipe = true)
        : base(progress)
    {
        _targets = new List<string>(targets);
        _passes = Math.Max(1, passes);
        _renameBeforeWipe = renameBeforeWipe;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        // Предпросмотр объёмов: сумма размеров файлов * число проходов.
        // Pre-scan volumes: total file size * number of passes.
        foreach (var t in _targets)
        {
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(t)) Scan(t, ct);
            else if (File.Exists(t)) { _totalBytes += new FileInfo(t).Length * _passes; _totalItems++; }
        }

        foreach (var t in _targets)
        {
            ct.ThrowIfCancellationRequested();
            try { await WipeEntryAsync(t, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failed++;
                _errors.Add($"{t}: {ex.Message}");
                LogService.Error($"Wipe failed for {t}: {ex.Message}", nameof(WipeOperation), ex);
            }
        }
    }

    private async Task WipeEntryAsync(string path, CancellationToken ct)
    {
        if (Directory.Exists(path))
        {
            // FIXED: Symlink traversal — проверяем ReparsePoint перед рекурсией.
            // Если это symlink/junction, удаляем саму ссылку, не затирая цель.
            var dirInfo = new DirectoryInfo(path);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                try { Directory.Delete(path, recursive: false); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogService.Warn($"Cannot delete reparse point {path}: {ex.Message}", nameof(WipeOperation));
                }
                Interlocked.Increment(ref _doneItems);
                ReportProgress(path);
                return;
            }
            // Сначала вытираем содержимое, затем удаляем пустой каталог.
            // Wipe contents first, then remove the now-empty directory.
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                await WipeFileAsync(f, ct).ConfigureAwait(false);
            }
            foreach (var d in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                await WipeEntryAsync(d, ct).ConfigureAwait(false);
            }
            Directory.Delete(path, recursive: false);
            Interlocked.Increment(ref _doneItems);
            ReportProgress(path);
        }
        else
        {
            await WipeFileAsync(path, ct).ConfigureAwait(false);
        }
    }

    private Task WipeFileAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fi = new FileInfo(path);
        if (!fi.Exists) return Task.CompletedTask;

        // Снимаем read-only, чтобы можно было перезаписывать/удалять.
        // Strip read-only so we can overwrite/delete.
        if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
            fi.Attributes &= ~FileAttributes.ReadOnly;

        string work = path;
        if (_renameBeforeWipe)
        {
            // Переименование скрывает исходное имя файла (DoD 5220.22-M).
            // Renaming obscures the original file name (DoD 5220.22-M).
            var dir = Path.GetDirectoryName(path) ?? ".";
            var rnd = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".wip");
            try { File.Move(path, rnd); work = rnd; }
            catch { work = path; } // если не вышло — работаем с исходным именем
        }

        long size = new FileInfo(work).Length;
        const int bufSize = 1 << 20; // ~1 МБ буфер
        var buf = new byte[bufSize];
        try
        {
            using var fs = new FileStream(work, FileMode.Open, FileAccess.Write, FileShare.None, bufSize, FileOptions.WriteThrough);
            for (int p = 0; p < _passes; p++)
            {
                ct.ThrowIfCancellationRequested();
                var pattern = p switch
                {
                    0 => FillPattern.Zero,
                    1 => FillPattern.FF,
                    _ => FillPattern.Random
                };
                WipePass(fs, buf, size, pattern, work, ct);
            }
            // Обрезка до нуля (уничтожает остаточные данные в allocation unit).
            // Truncate to zero (destroys residual data in the allocation unit).
            fs.SetLength(0);
            fs.Flush(true);
        }
        finally
        {
            // Удаление вытертого файла. / Delete the wiped file.
            try { File.Delete(work); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _failed++;
                _errors.Add($"{work}: {ex.Message}");
            }
        }

        Interlocked.Increment(ref _doneItems);
        ReportProgress(work);
        return Task.CompletedTask;
    }

    private void WipePass(FileStream fs, byte[] buf, long size, FillPattern pattern, string path, CancellationToken ct)
    {
        fs.Position = 0;
        long remaining = size;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int toWrite = (int)Math.Min(buf.Length, remaining);
            switch (pattern)
            {
                case FillPattern.Zero: buf.AsSpan(0, toWrite).Fill(0); break;
                case FillPattern.FF: buf.AsSpan(0, toWrite).Fill(0xFF); break;
                case FillPattern.Random: RandomNumberGenerator.Fill(buf.AsSpan(0, toWrite)); break;
            }
            fs.Write(buf, 0, toWrite);
            remaining -= toWrite;
            Interlocked.Add(ref _doneBytes, toWrite);
            Report(new OperationProgress(path, size - remaining, size, _doneBytes, _totalBytes, _doneItems, _totalItems));
        }
        fs.Flush(true);
    }

    private void ReportProgress(string currentFile)
        => Report(new OperationProgress(currentFile, 0, 0, _doneBytes, _totalBytes, _doneItems, _totalItems));

    private void Scan(string path, CancellationToken ct)
    {
        // Каталог сам считается за элемент. / The directory itself counts as an item.
        Interlocked.Increment(ref _totalItems);
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try { _totalBytes += new FileInfo(f).Length * _passes; }
            catch { /* недоступно — пропускаем из подсчёта / inaccessible, skip */ }
            Interlocked.Increment(ref _totalItems);
        }
        foreach (var d in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            Scan(d, ct);
        }
    }
}
