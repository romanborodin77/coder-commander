using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Services;

/// <summary>
/// Политика перезаписи при копировании файлов.
/// Overwrite policy for file copying.
/// </summary>
public enum OverwritePolicy
{
    /// <summary>Спрашивать пользователя. / Ask the user.</summary>
    Ask,
    /// <summary>Всегда перезаписывать. / Always overwrite.</summary>
    Always,
    /// <summary>Никогда не перезаписывать (пропустить). / Never overwrite (skip).</summary>
    Never,
    /// <summary>Переименовать новый файл. / Rename the new file.</summary>
    Rename
}

/// <summary>
/// Сервис копирования файлов и директорий с поддержкой прогресса и политик перезаписи.
/// File and directory copy service with progress reporting and overwrite policies.
/// </summary>
public sealed class CopyService
{
    private readonly IProcessService _proc;

    /// <summary>
    /// Создаёт экземпляр сервиса копирования.
    /// Creates an instance of the copy service.
    /// </summary>
    /// <param name="proc">Сервис для запуска процессов (зарезервирован). / Process service (reserved).</param>
    public CopyService(IProcessService proc) => _proc = proc;

    /// <summary>
    /// Событие прогресса копирования: (doneBytes, totalBytes, sourcePath).
    /// Copy progress event: (doneBytes, totalBytes, sourcePath).
    /// </summary>
    public event Action<long, long, string>? Progress;

    /// <summary>
    /// Запускает копирование с указанными параметрами и возвращает сводку.
    /// Starts copying with the specified parameters and returns a summary.
    /// </summary>
    /// <param name="src">Исходный путь (файл или директория). / Source path (file or directory).</param>
    /// <param name="dst">Целевой путь. / Destination path.</param>
    /// <param name="policy">Политика перезаписи. / Overwrite policy.</param>
    /// <param name="onConflict">Функция разрешения конфликтов (если policy = Ask). / Conflict resolution function (if policy = Ask).</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Сводка копирования. / Copy summary.</returns>
    public async Task<CopySummary> CopyAsync(string src, string dst, OverwritePolicy policy,
        Func<string, OverwritePolicy>? onConflict = null, CancellationToken ct = default)
    {
        var summary = new CopySummary();
        await CopyRecursive(src, dst, policy, onConflict, summary, ct);
        return summary;
    }

    /// <summary>
    /// Рекурсивно копирует директорию или файл с обработкой политики перезаписи.
    /// Recursively copies a directory or file with overwrite policy handling.
    /// </summary>
    private async Task CopyRecursive(string src, string dst, OverwritePolicy policy,
        Func<string, OverwritePolicy>? onConflict, CopySummary summary, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(src))
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.EnumerateFiles(src))
            {
                var d = Path.Combine(dst, Path.GetFileName(f)!);
                try { await CopyFile(f, d, policy, onConflict, summary, ct); }
                catch { summary.Failed++; }
            }
            foreach (var d in Directory.EnumerateDirectories(src))
            {
                await CopyRecursive(d, Path.Combine(dst, Path.GetFileName(d)!), policy, onConflict, summary, ct);
            }
        }
        else
        {
            try { await CopyFile(src, dst, policy, onConflict, summary, ct); }
            catch { summary.Failed++; }
        }
    }

    /// <summary>
    /// Копирует один файл с учётом политики перезаписи и отслеживанием прогресса.
    /// Copies a single file respecting overwrite policy and tracking progress.
    /// </summary>
    private async Task CopyFile(string src, string dst, OverwritePolicy policy,
        Func<string, OverwritePolicy>? onConflict, CopySummary summary, CancellationToken ct)
    {
        if (File.Exists(dst))
        {
            var p = policy;
            if (p == OverwritePolicy.Ask) p = onConflict?.Invoke(dst) ?? OverwritePolicy.Never;
            if (p == OverwritePolicy.Never) { summary.Skipped++; return; }
            if (p == OverwritePolicy.Rename)
            {
                dst = UniqueName(dst);
            }
        }
        Progress?.Invoke(0, 0, src);
        var buf = new byte[1 << 20];
        using var sin = File.OpenRead(src);
        using var sout = File.Create(dst);
        long total = sin.Length, done = 0;
        int read;
        while ((read = await sin.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
        {
            await sout.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            summary.Bytes += read;
            Progress?.Invoke(done, total, src);
        }
        summary.Copied++;
    }

    /// <summary>
    /// Генерирует уникальное имя файла, добавляя "(1)", "(2)" и т.д.
    /// Generates a unique file name by appending "(1)", "(2)", etc.
    /// </summary>
    /// <param name="path">Исходный путь. / Original path.</param>
    /// <returns>Путь с уникальным именем. / Path with a unique name.</returns>
    private static string UniqueName(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        int i = 1;
        string cand;
        do { cand = Path.Combine(dir, $"{name} ({i++}){ext}"); } while (File.Exists(cand));
        return cand;
    }
}

/// <summary>
/// Сводка результатов операции копирования.
/// Summary of a copy operation result.
/// </summary>
public sealed class CopySummary
{
    /// <summary>
    /// Количество успешно скопированных файлов. / Number of successfully copied files.
    /// </summary>
    public int Copied { get; set; }
    /// <summary>
    /// Количество пропущенных файлов. / Number of skipped files.
    /// </summary>
    public int Skipped { get; set; }
    /// <summary>
    /// Количество файлов, которые не удалось скопировать. / Number of files that failed to copy.
    /// </summary>
    public int Failed { get; set; }
    /// <summary>
    /// Общее количество скопированных байт. / Total bytes copied.
    /// </summary>
    public long Bytes { get; set; }
}
