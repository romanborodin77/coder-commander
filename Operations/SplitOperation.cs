using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Services;

namespace CoderCommander.Operations;

/// <summary>
/// Операция разбиения файла(ов) на тома (ph1.3, exp.yml):
/// именование TC-стиля «имя.расширение.001», «.002» …,
/// опциональный размер тома, запись summary-файла «size=&lt;число&gt;».
/// File split operation (ph1.3): TC-style volume naming "name.ext.001",
/// optional volume size, and a "size=<n>" summary file.
/// Буферизованный FileStream (~1 МБ), прогресс через IProgress, CancellationToken.
/// Buffered FileStream (~1 MB), IProgress reporting, CancellationToken support.
/// </summary>
public sealed class SplitOperation : FileOperation
{
    /// <summary>Размер тома по умолчанию (100 МБ) при отсутствии явного значения. / Default volume size (100 MB).</summary>
    public const long DefaultVolumeSize = 100L * 1024 * 1024;

    private const int BufferSize = 1 << 20; // 1 МБ / 1 MB

    private readonly List<string> _sources;
    private readonly long _volumeSize;
    private readonly int _numberWidth;

    private int _totalFiles;
    private int _doneFiles;
    private long _totalBytes;
    private long _doneBytes;

    /// <summary>Число созданных томов (для статистики). / Number of volumes created.</summary>
    public int VolumesCreated { get; private set; }

    /// <summary>
    /// Создаёт операцию разбиения. / Creates a split operation.
    /// </summary>
    /// <param name="sources">Исходные файлы (не каталоги). / Source files (not directories).</param>
    /// <param name="volumeSize">Размер тома в байтах (null/&lt;=0 → <see cref="DefaultVolumeSize"/>). / Volume size in bytes.</param>
    /// <param name="numberWidth">Ширина номера тома (минимум 1). / Volume index width (minimum 1).</param>
    /// <param name="progress">Приёмник прогресса. / Progress sink.</param>
    public SplitOperation(
        IEnumerable<string> sources, long? volumeSize = null, int numberWidth = 3,
        IProgress<OperationProgress>? progress = null)
        : base(progress)
    {
        _sources = sources.ToList();
        _volumeSize = volumeSize is > 0 ? volumeSize.Value : DefaultVolumeSize;
        _numberWidth = numberWidth < 1 ? 3 : numberWidth;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteCoreAsync(CancellationToken ct)
    {
        _totalFiles = _sources.Count;
        _totalBytes = _sources.Sum(p =>
        {
            try { return new FileInfo(p).Length; }
            catch { return 0L; }
        });

        foreach (var src in _sources)
        {
            ct.ThrowIfCancellationRequested();
            await SplitOneAsync(src, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _doneFiles);
            Report(new OperationProgress(src, _volumeSize, _volumeSize, _doneBytes, _totalBytes, _doneFiles, _totalFiles));
        }
    }

    private async Task SplitOneAsync(string src, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(src)) ?? ".";
        var baseName = Path.GetFileName(src);
        long fileSize = new FileInfo(src).Length;
        long fileDone = 0;
        long totalRead = 0;
        int part = 1;
        var buffer = new byte[BufferSize];

        using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.SequentialScan);

        // Гарантируем хотя бы один том (даже для пустого файла).
        // Ensure at least one volume (even for an empty file).
        do
        {
            ct.ThrowIfCancellationRequested();
            long remaining = Math.Min(_volumeSize, fileSize - totalRead);
            var outPath = Path.Combine(dir, baseName + "." + part.ToString($"D{_numberWidth}"));

            using (var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None,
                       BufferSize, FileOptions.SequentialScan))
            {
                long written = 0;
                while (written < remaining)
                {
                    ct.ThrowIfCancellationRequested();
                    int toRead = (int)Math.Min(BufferSize, remaining - written);
                    int read = await input.ReadAsync(buffer, 0, toRead, ct).ConfigureAwait(false);
                    if (read == 0) break;
                    await outStream.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                    written += read;
                    totalRead += read;
                    fileDone += read;
                    _doneBytes += read;
                    Report(new OperationProgress(src, fileDone, fileSize, _doneBytes, _totalBytes, _doneFiles, _totalFiles));
                }
                await outStream.FlushAsync(ct).ConfigureAwait(false);
            }

            VolumesCreated++;
            part++;
        } while (totalRead < fileSize);

        // Summary-файл: size=<исходный размер в байтах>.
        // Summary file: size=<original size in bytes>.
        var summaryPath = Path.Combine(dir, baseName + ".sum");
        await File.WriteAllTextAsync(summaryPath, $"size={fileSize}{Environment.NewLine}", ct).ConfigureAwait(false);
    }
}
