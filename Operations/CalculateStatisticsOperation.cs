using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Итоговая статистика каталога. / Final directory statistics.
/// </summary>
public sealed class StatisticsResult
{
    /// <summary>Путь к корню. / Root path.</summary>
    public string Path { get; }
    /// <summary>Число файлов. / File count.</summary>
    public long FileCount { get; }
    /// <summary>Число папок. / Folder count.</summary>
    public long DirectoryCount { get; }
    /// <summary>Число симлинков/точек монтирования. / Symlink/junction count.</summary>
    public long SymlinkCount { get; }
    /// <summary>Общий размер в байтах. / Total size in bytes.</summary>
    public long TotalSize { get; }
    /// <summary>Затраченное время. / Elapsed time.</summary>
    public TimeSpan Elapsed { get; }

    public StatisticsResult(string path, long fileCount, long directoryCount, long symlinkCount, long totalSize, TimeSpan elapsed)
    {
        Path = path; FileCount = fileCount; DirectoryCount = directoryCount;
        SymlinkCount = symlinkCount; TotalSize = totalSize; Elapsed = elapsed;
    }

    /// <summary>Человекочитаемый размер. / Human-readable size.</summary>
    public string TotalSizeDisplay => FormatBytes(TotalSize);

    private static string FormatBytes(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }
}

/// <summary>Потокобезопасные накопители статистики. / Thread-safe statistics accumulators.</summary>
internal sealed class StatisticsData
{
    public long FileCount;
    public long DirectoryCount;
    public long SymlinkCount;
    public long TotalSize;
    public long ItemsProcessed;
    public DateTime Start;
}

/// <summary>
/// Рекурсивный подсчёт статистики каталога: файлы, папки, симлинки, общий размер
/// (ph1.5, exp.yml). Параллельное перечисление через Parallel.ForEachAsync с
/// ограничением MaxDegreeOfParallelism = ProcessorCount. Прогресс = число
/// обработанных элементов. / Recursive directory statistics (ph1.5): files,
/// folders, symlinks, total size. Parallel enumeration via Parallel.ForEachAsync
/// (MaxDegreeOfParallelism = ProcessorCount). Progress = processed element count.
/// BCL-only (System.IO.Enumeration + System.Threading.Tasks.Parallel).
/// </summary>
public sealed class CalculateStatisticsOperation : FileOperation
{
    private readonly string _root;
    private readonly StatisticsData _data = new();

    /// <summary>Итоговый результат (доступен после завершения). / Final result (available after completion).</summary>
    public StatisticsResult Result { get; private set; } = new(string.Empty, 0, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Создаёт операцию подсчёта статистики.
    /// Creates a statistics operation.
    /// </summary>
    /// <param name="root">Корневой путь (файл или каталог). / Root path (file or directory).</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    public CalculateStatisticsOperation(string root, IProgress<OperationProgress>? progress = null)
        : base(progress) => _root = root;

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        _data.Start = DateTime.UtcNow;

        // Канал каталогов для обработки: производитель кладёт корень, параллельные
        // потребители обходят содержимое и добавляют вложенные папки.
        // Directory channel: a producer enqueues the root, parallel consumers walk
        // contents and enqueue nested folders.
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        var activeWriters = 1;

        var producer = Task.Run(async () =>
        {
            try
            {
                var info = new DirectoryInfo(_root);
                if (info.Exists)
                {
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) Interlocked.Increment(ref _data.SymlinkCount);
                    else Interlocked.Increment(ref _data.DirectoryCount);
                }
                await channel.Writer.WriteAsync(_root, ct).ConfigureAwait(false);
            }
            finally
            {
                if (Interlocked.Decrement(ref activeWriters) == 0)
                    channel.Writer.TryComplete();
            }
        }, ct);

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(channel.Reader.ReadAllAsync(ct), options, async (dir, cti) =>
        {
            Interlocked.Increment(ref activeWriters);
            try
            {
                await ProcessDirectoryAsync(dir, channel.Writer, cti).ConfigureAwait(false);
            }
            finally
            {
                if (Interlocked.Decrement(ref activeWriters) == 0)
                    channel.Writer.TryComplete();
            }
        }).ConfigureAwait(false);

        await producer.ConfigureAwait(false);

        var elapsed = DateTime.UtcNow - _data.Start;
        Result = new StatisticsResult(
            _root,
            Interlocked.Read(ref _data.FileCount),
            Interlocked.Read(ref _data.DirectoryCount),
            Interlocked.Read(ref _data.SymlinkCount),
            Interlocked.Read(ref _data.TotalSize),
            elapsed);
    }

    private async Task ProcessDirectoryAsync(string dir, ChannelWriter<string> writer, CancellationToken ct)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(f);
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // Файл-симлинк. / Symlink file.
                    Interlocked.Increment(ref _data.SymlinkCount);
                }
                else
                {
                    Interlocked.Add(ref _data.TotalSize, fi.Exists ? fi.Length : 0);
                    Interlocked.Increment(ref _data.FileCount);
                }
                Interlocked.Increment(ref _data.ItemsProcessed);
                ReportProgress(f);
            }

            foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var di = new DirectoryInfo(d);
                if ((di.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    // Симлинк/точка монтирования — считаем как симлинк, не заходим внутрь.
                    // Symlink/junction — count as symlink, do not recurse.
                    Interlocked.Increment(ref _data.SymlinkCount);
                    Interlocked.Increment(ref _data.ItemsProcessed);
                    ReportProgress(d);
                }
                else
                {
                    Interlocked.Increment(ref _data.DirectoryCount);
                    Interlocked.Increment(ref _data.ItemsProcessed);
                    ReportProgress(d);
                    await writer.WriteAsync(d, ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogService.Warn($"Statistics: skipped {dir}: {ex.Message}", nameof(CalculateStatisticsOperation));
        }
    }

    private void ReportProgress(string currentFile)
        => Report(new OperationProgress(currentFile, 0, 0, Interlocked.Read(ref _data.ItemsProcessed), 0, Interlocked.Read(ref _data.ItemsProcessed), 0));
}
