using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.Operations;

/// <summary>
/// Внутренний помощник для потокового копирования файлов с буферизацией
/// и отчётом прогресса по байтам. / Internal helper for buffered streaming
/// file copy with per-byte progress reporting (ph0.2, exp.yml).
/// </summary>
internal static class FileCopyHelper
{
    /// <summary>Размер буфера по умолчанию: 1 МБ. / Default buffer size: 1 MB.</summary>
    public const int DefaultBufferSize = 1 << 20;

    /// <summary>
    /// Копирует файл потоково, сообщая о числе скопированных байт.
    /// При ошибке или отмене удаляет неполный файл назначения.
    /// Streams a file copy, reporting bytes copied. Deletes partial destination on error/cancel.
    /// </summary>
    /// <param name="source">Исходный путь. / Source path.</param>
    /// <param name="destination">Целевой путь. / Destination path.</param>
    /// <param name="byteProgress">Прогресс по байтам (необязательный). / Byte progress (optional).</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <param name="bufferSize">Размер буфера в байтах. / Buffer size in bytes.</param>
    /// <param name="pauseEvent">Событие паузы (блокирует цикл до Resume). / Pause event (blocks loop until Resume).</param>
    /// <param name="skipCurrentFileFunc">Функция проверки флага Skip (необязательная). / Skip flag check function (optional).</param>
    /// <returns>True если файл скопирован полностью, false если пропущен. / True if fully copied, false if skipped.</returns>
    public static async Task<bool> CopyFileAsync(string source, string destination, IProgress<long>? byteProgress,
        CancellationToken ct, int bufferSize = DefaultBufferSize,
        ManualResetEventSlim? pauseEvent = null, Func<bool>? skipCurrentFileFunc = null)
    {
        if (bufferSize <= 0) bufferSize = DefaultBufferSize;

        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var sin = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);
        using var sout = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize, FileOptions.SequentialScan | FileOptions.Asynchronous);

        var buf = new byte[bufferSize];
        long done = 0;
        int read;
        while ((read = await sin.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
        {
            pauseEvent?.Wait(ct);
            if (skipCurrentFileFunc?.Invoke() == true)
            {
                // Скип во время копирования — закрываем стримы, удаляем partial.
                // Skip during copy — close streams, delete partial.
                sout.Close();
                sin.Close();
                TryDelete(destination);
                return false;
            }
            await sout.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            byteProgress?.Report(done);
        }
        await sout.FlushAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
