using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Режим сравнения пар при синхронизации папок (ph3.3, exp.yml section «Синхронизация папок»).
/// Directory comparison mode for folder sync (ph3.3).
/// </summary>
public enum SyncCompareMode
{
    /// <summary>Сравнение по размеру и времени изменения (быстро). / Compare by size and last-write time (fast).</summary>
    SizeMtime,
    /// <summary>Дополнительно побайтовая/хеш-проверка содержимого (SHA-256 через ChecksumHelper). / Also verify content by SHA-256 hash.</summary>
    Content
}

/// <summary>
/// Направление синхронизации по умолчанию для различающихся файлов, присутствующих с обеих сторон.
/// Default sync direction for files present on both sides but differing.
/// </summary>
public enum SyncDefaultDirection
{
    /// <summary>Копировать более новый поверх более старого. / Copy the newer file over the older one.</summary>
    CopyToNewer,
    /// <summary>Копировать из правой панели в левую (левая обновляется). / Copy from right into left.</summary>
    CopyToLeft,
    /// <summary>Копировать из левой панели в правую (правая обновляется). / Copy from left into right.</summary>
    CopyToRight
}

/// <summary>
/// Действие для пары (7 состояний согласно exp.yml ph3.3).
/// Action for a pair (the 7 states from exp.yml ph3.3).
/// </summary>
public enum SyncAction
{
    /// <summary>Без действия (пользователь снял выбор). / No action (user cleared the selection).</summary>
    None,
    /// <summary>Файлы идентичны — ничего не делать. / Files identical — do nothing.</summary>
    Equal,
    /// <summary>Копировать в левую панель (из правой). / Copy into the left panel (from right).</summary>
    CopyLeft,
    /// <summary>Копировать в правую панель (из левой). / Copy into the right panel (from left).</summary>
    CopyRight,
    /// <summary>Удалить файл на левой панели. / Delete the file on the left panel.</summary>
    DeleteLeft,
    /// <summary>Удалить файл на правой панели. / Delete the file on the right panel.</summary>
    DeleteRight,
    /// <summary>Удалить файл с обеих панелей. / Delete the file on both panels.</summary>
    DeleteBoth
}

/// <summary>
/// Флаги различий между левой и правой сторонами пары.
/// Flags describing the differences between the left and right sides of a pair.
/// </summary>
[Flags]
public enum SyncDifference
{
    /// <summary>Нет различий. / No differences.</summary>
    None = 0,
    /// <summary>Существует только на левой стороне. / Exists only on the left side.</summary>
    ExistsLeft = 1,
    /// <summary>Существует только на правой стороне. / Exists only on the right side.</summary>
    ExistsRight = 2,
    /// <summary>Различается размер. / Differs in size.</summary>
    Size = 4,
    /// <summary>Различается время изменения. / Differs in last-write time.</summary>
    Time = 8,
    /// <summary>Различается содержимое (хеш). / Differs in content (hash).</summary>
    Content = 16
}

/// <summary>
/// Пара синхронизации: один относительный путь, сопоставленный между двумя папками.
/// A sync pair: one relative path matched between the two folders.
/// Поддерживает выбор действия пользователем и флаг включения в применение.
/// Supports a user-chosen action and an include-in-apply flag.
/// </summary>
public sealed partial class SyncPair : ObservableObject
{
    /// <summary>Относительный путь (относительно корня сравнения). / Relative path (relative to the comparison root).</summary>
    public string RelativePath { get; }
    /// <summary>Это каталог (а не файл). / Whether this is a directory (rather than a file).</summary>
    public bool IsDirectory { get; }
    /// <summary>Элемент на левой стороне (или null, если отсутствует). / Left-side entry (null if absent).</summary>
    public FileEntry? Left { get; }
    /// <summary>Элемент на правой стороне (или null, если отсутствует). / Right-side entry (null if absent).</summary>
    public FileEntry? Right { get; }
    /// <summary>Обнаруженные различия. / Detected differences.</summary>
    public SyncDifference Difference { get; set; }
    /// <summary>Действие по умолчанию, предложенное движком. / Default action suggested by the engine.</summary>
    public SyncAction DefaultAction { get; }

    /// <summary>Действие, выбранное пользователем (по умолчанию = DefaultAction). / User-selected action (defaults to DefaultAction).</summary>
    [ObservableProperty] private SyncAction _action;
    /// <summary>Включена ли пара в применение. / Whether the pair is included in the apply step.</summary>
    [ObservableProperty] private bool _apply = true;

    /// <summary>Путь слева (или null). / Left path (or null).</summary>
    public string? LeftPath => Left?.FullPath;
    /// <summary>Путь справа (или null). / Right path (or null).</summary>
    public string? RightPath => Right?.FullPath;
    /// <summary>Размер слева (байты). / Left size (bytes).</summary>
    public long SizeLeft => Left?.Size ?? 0;
    /// <summary>Размер справа (байты). / Right size (bytes).</summary>
    public long SizeRight => Right?.Size ?? 0;
    /// <summary>Время изменения слева (UTC). / Left last-write time (UTC).</summary>
    public DateTime LastWriteLeft => Left?.LastWriteTimeUtc ?? default;
    /// <summary>Время изменения справа (UTC). / Right last-write time (UTC).</summary>
    public DateTime LastWriteRight => Right?.LastWriteTimeUtc ?? default;

    /// <summary>Создаёт пару. / Creates a pair.</summary>
    public SyncPair(string relativePath, bool isDirectory, FileEntry? left, FileEntry? right,
                    SyncDifference difference, SyncAction defaultAction)
    {
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        Left = left;
        Right = right;
        Difference = difference;
        DefaultAction = defaultAction;
        _action = defaultAction;
        // Равные пары и «без действия» по умолчанию не применяются.
        // Equal pairs and "no action" are not applied by default.
        _apply = defaultAction is not (SyncAction.Equal or SyncAction.None);
    }
}

/// <summary>
/// Параметры сравнения папок (ph3.3).
/// Folder comparison options (ph3.3).
/// </summary>
public sealed class SyncOptions
{
    /// <summary>Режим сравнения (size+mtime или content). / Comparison mode.</summary>
    public SyncCompareMode Mode { get; set; } = SyncCompareMode.SizeMtime;
    /// <summary>Направление синхронизации по умолчанию. / Default sync direction.</summary>
    public SyncDefaultDirection Direction { get; set; } = SyncDefaultDirection.CopyToNewer;
    /// <summary>Включать ли вложенные папки. / Include subfolders.</summary>
    public bool IncludeSubfolders { get; set; } = true;
    /// <summary>Маска имён (через «;», поддержка * и ?); null — все файлы. / Name mask (separated by ";", supports * and ?); null = all.</summary>
    public string? Mask { get; set; }
    /// <summary>Только выделенное: полные пути выбранных элементов в любой панели (null — без ограничения). / Only-selected: full paths of selected items in either panel (null = no restriction).</summary>
    public IReadOnlyList<string>? SelectedPaths { get; set; }
    /// <summary>Допуск по времени изменения (компенсация FAT/сетевых ФС). / Time tolerance (FAT/network FS compensation).</summary>
    public TimeSpan TimeTolerance { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>
    /// Асимметричный режим: копировать только из левой панели в правую.
    /// Файлы, существующие только справа, пропускаются (SyncAction.None); удаления подавляются.
    /// Asymmetric mode: copy from left to right only. Files existing only on the right are skipped;
    /// delete actions are suppressed.
    /// </summary>
    public bool Asymmetric { get; set; }
}

/// <summary>
/// Прогресс применения действий синхронизации.
/// Progress while applying sync actions.
/// </summary>
public sealed class SyncApplyProgress
{
    /// <summary>Обработано пар из Total. / Pairs processed out of Total.</summary>
    public int Done { get; }
    /// <summary>Всего пар к обработке. / Total pairs to process.</summary>
    public int Total { get; }
    /// <summary>Текущий обрабатываемый файл. / Current file being processed.</summary>
    public string CurrentFile { get; }
    /// <summary>Процент выполнения (0-100). / Completion percentage (0-100).</summary>
    public int Percent => Total == 0 ? 0 : (int)(100L * Done / Total);

    /// <summary>Создаёт прогресс. / Creates progress.</summary>
    public SyncApplyProgress(int done, int total, string currentFile)
    {
        Done = done;
        Total = total;
        CurrentFile = currentFile;
    }
}

/// <summary>
/// Результат применения действий синхронизации.
/// Result of applying sync actions.
/// </summary>
public sealed class SyncApplyResult
{
    /// <summary>Успешно обработано пар. / Pairs successfully processed.</summary>
    public int Succeeded { get; init; }
    /// <summary>Число неудач. / Number of failures.</summary>
    public int Failed { get; init; }
    /// <summary>Описания ошибок. / Error descriptions.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Движок синхронизации папок (ph3.3, exp.yml section «Синхронизация папок»).
/// Directory sync engine (ph3.3).
/// Рекурсивно сканирует обе папки через <see cref="IFileSystem.EnumerateAsync"/> (параллельно),
/// сопоставляет файлы по относительному пути, сравнивает size + mtime, опционально содержимое
/// (буферный SequenceEqual или хеш SHA-256 через <see cref="ChecksumHelper"/>).
/// Recursively scans both folders via IFileSystem.EnumerateAsync (in parallel), matches files by
/// relative path, compares size + mtime, optionally content (buffered SequenceEqual or SHA-256 hash).
/// Применение действий выполняется через существующие <see cref="CopyOperation"/> / <see cref="DeleteOperation"/>
/// с агрегированным прогрессом и отменой. / Actions are applied via existing CopyOperation/DeleteOperation.
/// </summary>
public static class DirectorySyncEngine
{
    /// <summary>
    /// Сравнивает две папки и возвращает список пар с предложенными действиями.
    /// Compares two folders and returns the list of pairs with suggested actions.
    /// </summary>
    public static async Task<IReadOnlyList<SyncPair>> CompareAsync(
        IFileSystem fs, string leftRoot, string rightRoot, SyncOptions options,
        IProgress<double>? progress, CancellationToken ct)
    {
        progress?.Report(0);
        var selected = NormalizeSelected(options.SelectedPaths, leftRoot, rightRoot);

        // Параллельное сканирование обеих сторон (учёт больших папок).
        // Parallel scan of both sides (handles large folders).
        var leftTask = ScanAsync(fs, leftRoot, options, ct);
        var rightTask = ScanAsync(fs, rightRoot, options, ct);
        await Task.WhenAll(leftTask, rightTask).ConfigureAwait(false);
        var left = await leftTask;
        var right = await rightTask;

        // Объединяем ключи (относительные пути). / Union of relative-path keys.
        var keys = new HashSet<string>(left.Keys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(right.Keys);

        var pairs = new List<SyncPair>(keys.Count);
        var differing = new List<SyncPair>();

        foreach (var key in keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (selected is not null && !IsInSelected(selected, key))
                continue;

            left.TryGetValue(key, out var l);
            right.TryGetValue(key, out var r);
            var isDir = (l ?? r)?.IsDirectory ?? false;

            var pair = BuildPair(key, isDir, l, r, options);
            if (pair is null) continue;

            // Откладываем пары, требующие проверки содержимого.
            // Defer pairs that need content verification.
            if (options.Mode == SyncCompareMode.Content
                && pair.Difference.HasFlag(SyncDifference.Size) == false
                && pair.Difference.HasFlag(SyncDifference.Time)
                && l is not null && r is not null && !isDir)
            {
                differing.Add(pair);
            }
            pairs.Add(pair);
        }

        // Побайтовая/хеш-проверка различающихся по времени файлов (параллельно).
        // Content/hash verification of time-differing files (in parallel).
        if (differing.Count > 0)
            await VerifyContentAsync(differing, ct).ConfigureAwait(false);

        progress?.Report(100);
        return pairs;
    }

    /// <summary>
    /// Применяет выбранные действия ко всем парам через CopyOperation/DeleteOperation.
    /// Applies the chosen actions to all pairs via CopyOperation/DeleteOperation.
    /// </summary>
    public static async Task<SyncApplyResult> ApplyAsync(
        IFileSystem fs, IReadOnlyList<SyncPair> pairs, string leftRoot, string rightRoot,
        IProgress<SyncApplyProgress>? progress, CancellationToken ct)
    {
        var toApply = pairs.Where(p => p.Apply && p.Action is not (SyncAction.None or SyncAction.Equal)).ToList();
        int total = toApply.Count;
        int done = 0;
        int failed = 0;
        var errors = new ConcurrentBag<string>();

        foreach (var pair in toApply)
        {
            ct.ThrowIfCancellationRequested();
            var current = pair.RelativePath;
            try
            {
                await ApplyPairAsync(fs, pair, leftRoot, rightRoot, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{current}: {ex.Message}");
                LogService.Error($"Sync apply failed for {current}: {ex.Message}", nameof(DirectorySyncEngine), ex);
            }
            done++;
            progress?.Report(new SyncApplyProgress(done, total, current));
        }

        return new SyncApplyResult { Succeeded = done - failed, Failed = failed, Errors = errors.ToList() };
    }

    // ──────────────────────────────────────────────────────────────
    // Внутренние helpers / Internal helpers
    // ──────────────────────────────────────────────────────────────

    private static async Task ApplyPairAsync(IFileSystem fs, SyncPair pair, string leftRoot, string rightRoot, CancellationToken ct)
    {
        string dest;
        switch (pair.Action)
        {
            case SyncAction.CopyRight:
                dest = Path.Combine(rightRoot, pair.RelativePath);
                await fs.CreateDirectoryAsync(Path.GetDirectoryName(dest) ?? rightRoot, ct).ConfigureAwait(false);
                await new CopyOperation(fs, new[] { pair.Left!.FullPath }, Path.GetDirectoryName(dest)!,
                    OverwritePolicy.Overwrite, null, null).ExecuteAsync(ct).ConfigureAwait(false);
                break;

            case SyncAction.CopyLeft:
                dest = Path.Combine(leftRoot, pair.RelativePath);
                await fs.CreateDirectoryAsync(Path.GetDirectoryName(dest) ?? leftRoot, ct).ConfigureAwait(false);
                await new CopyOperation(fs, new[] { pair.Right!.FullPath }, Path.GetDirectoryName(dest)!,
                    OverwritePolicy.Overwrite, null, null).ExecuteAsync(ct).ConfigureAwait(false);
                break;

            case SyncAction.DeleteLeft:
                await new DeleteOperation(fs, new[] { pair.Left!.FullPath }, null).ExecuteAsync(ct).ConfigureAwait(false);
                break;

            case SyncAction.DeleteRight:
                await new DeleteOperation(fs, new[] { pair.Right!.FullPath }, null).ExecuteAsync(ct).ConfigureAwait(false);
                break;

            case SyncAction.DeleteBoth:
                await new DeleteOperation(fs, new[] { pair.Left!.FullPath, pair.Right!.FullPath }, null).ExecuteAsync(ct).ConfigureAwait(false);
                break;

            default:
                break;
        }
    }

    private static SyncPair? BuildPair(string key, bool isDir, FileEntry? l, FileEntry? r, SyncOptions opt)
    {
        if (l is not null && r is null)
        {
            // Только слева → скопировать вправо, чтобы папки совпали.
            // Only on left → copy to right so the folders match.
            return new SyncPair(key, isDir, l, null, SyncDifference.ExistsLeft, SyncAction.CopyRight);
        }
        if (l is null && r is not null)
        {
            // В асимметричном режиме файлы только справа пропускаются.
            // In asymmetric mode, files existing only on the right are skipped.
            var rightOnlyAction = opt.Asymmetric ? SyncAction.None : SyncAction.CopyLeft;
            return new SyncPair(key, isDir, null, r, SyncDifference.ExistsRight, rightOnlyAction);
        }
        if (l is null || r is null) return null; // не должно происходить / should not happen

        // Оба существуют. / Both exist.
        var diff = SyncDifference.None;
        if (l.Size != r.Size) diff |= SyncDifference.Size;
        if (Math.Abs((l.LastWriteTimeUtc - r.LastWriteTimeUtc).TotalSeconds) > opt.TimeTolerance.TotalSeconds)
            diff |= SyncDifference.Time;

        if (diff == SyncDifference.None)
            return new SyncPair(key, isDir, l, r, SyncDifference.None, SyncAction.Equal);

        if (isDir)
        {
            // Для каталогов сравнение только по времени; копируем более новый.
            // For directories compare time only; copy the newer one.
            var dir = l.LastWriteTimeUtc >= r.LastWriteTimeUtc ? SyncAction.CopyRight : SyncAction.CopyLeft;
            return new SyncPair(key, isDir, l, r, diff, dir);
        }

        // Различаются размер или время → выбираем действие по направлению.
        // Differ in size or time → pick action by direction.
        var action = opt.Direction switch
        {
            SyncDefaultDirection.CopyToLeft => SyncAction.CopyLeft,
            SyncDefaultDirection.CopyToRight => SyncAction.CopyRight,
            _ => l.LastWriteTimeUtc >= r.LastWriteTimeUtc ? SyncAction.CopyRight : SyncAction.CopyLeft
        };
        return new SyncPair(key, isDir, l, r, diff, action);
    }

    private static async Task VerifyContentAsync(IReadOnlyList<SyncPair> pairs, CancellationToken ct)
    {
        await Parallel.ForEachAsync(pairs,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount), CancellationToken = ct },
            async (pair, cti) =>
            {
                try
                {
                    var hl = await ChecksumHelper.ComputeHashAsync(pair.Left!.FullPath, ChecksumAlgorithm.SHA256, null, cti).ConfigureAwait(false);
                    var hr = await ChecksumHelper.ComputeHashAsync(pair.Right!.FullPath, ChecksumAlgorithm.SHA256, null, cti).ConfigureAwait(false);
                    if (string.Equals(hl, hr, StringComparison.OrdinalIgnoreCase))
                    {
                        // Содержимое совпадает → считаем равными (различие только по времени).
                        // Content matches → treat as equal (only time differs).
                        pair.Action = SyncAction.Equal;
                        pair.Apply = false;
                    }
                    else
                    {
                        pair.Difference |= SyncDifference.Content;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    LogService.Warn($"Content compare failed for {pair.RelativePath}: {ex.Message}", nameof(DirectorySyncEngine));
                }
            }).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, FileEntry>> ScanAsync(IFileSystem fs, string root, SyncOptions opt, CancellationToken ct)
    {
        var dict = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        await ScanDirAsync(fs, root, root, opt, dict, ct).ConfigureAwait(false);
        return dict;
    }

    private static async Task ScanDirAsync(IFileSystem fs, string dir, string root, SyncOptions opt, Dictionary<string, FileEntry> dict, CancellationToken ct)
    {
        IReadOnlyList<FileEntry> entries;
        try { entries = await fs.EnumerateAsync(dir, includeHidden: true, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Warn($"Sync scan failed: {dir}: {ex.Message}", nameof(DirectorySyncEngine));
            return;
        }

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            var rel = GetRelativePath(root, e.FullPath);
            if (e.IsDirectory)
            {
                dict[rel] = e;
                if (opt.IncludeSubfolders) await ScanDirAsync(fs, e.FullPath, root, opt, dict, ct).ConfigureAwait(false);
            }
            else
            {
                if (!MatchMask(opt.Mask, e.Name)) continue;
                dict[rel] = e;
            }
        }
    }

    private static bool IsInSelected(HashSet<string> selected, string rel)
    {
        // Прямое совпадение либо вложенность в выбранный элемент.
        // Direct match or being nested under a selected item.
        if (selected.Contains(rel)) return true;
        foreach (var s in selected)
        {
            if (rel.Length > s.Length && rel[s.Length] == Path.DirectorySeparatorChar && rel.StartsWith(s, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static HashSet<string>? NormalizeSelected(IReadOnlyList<string>? selectedPaths, string leftRoot, string rightRoot)
    {
        if (selectedPaths is null || selectedPaths.Count == 0) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in selectedPaths)
        {
            if (p.StartsWith(leftRoot, StringComparison.OrdinalIgnoreCase))
                set.Add(GetRelativePath(leftRoot, p));
            else if (p.StartsWith(rightRoot, StringComparison.OrdinalIgnoreCase))
                set.Add(GetRelativePath(rightRoot, p));
            else
                set.Add(p);
        }
        return set;
    }

    private static string GetRelativePath(string root, string full)
    {
        // Path.GetRelativePath доступен в .NET 8; нормализуем разделитель.
        // Path.GetRelativePath is available in .NET 8; normalize the separator.
        var rel = Path.GetRelativePath(root, full);
        return rel.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private static bool MatchMask(string? mask, string name)
    {
        if (string.IsNullOrWhiteSpace(mask)) return true;
        foreach (var part in mask.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (MatchWildcard(part, name)) return true;
        }
        return false;
    }

    private static bool MatchWildcard(string pattern, string name)
    {
        // Простой wildcard: * — любые символы, ? — один символ. Регистронезависимо.
        // Simple wildcard: * any chars, ? single char. Case-insensitive.
        var rx = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, rx, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
