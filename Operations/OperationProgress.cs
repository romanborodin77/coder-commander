namespace CoderCommander.Operations;

/// <summary>
/// Прогресс выполнения файловой операции (передаётся через IProgress&lt;OperationProgress&gt;).
/// Progress of a file operation (reported via IProgress&lt;OperationProgress&gt;).
/// </summary>
public sealed class OperationProgress
{
    /// <summary>Текущий обрабатываемый файл (для отображения в UI). / Current file being processed.</summary>
    public string? CurrentFile { get; }

    /// <summary>Обработано байт на текущем файле. / Bytes processed in the current file.</summary>
    public long BytesDone { get; }

    /// <summary>Общий объём текущего файла в байтах. / Total size of the current file in bytes.</summary>
    public long BytesTotal { get; }

    /// <summary>Обработано байт по всей операции. / Bytes processed across the whole operation.</summary>
    public long TotalBytesDone { get; }

    /// <summary>Общий объём операции в байтах. / Total byte volume of the operation.</summary>
    public long TotalBytes { get; }

    /// <summary>Обработано файлов. / Files processed.</summary>
    public long FilesDone { get; }

    /// <summary>Всего файлов. / Total files.</summary>
    public long FilesTotal { get; }

    /// <summary>Процент завершения всей операции (0..100). / Overall completion percent (0..100).</summary>
    public double Percent { get; }

    /// <summary>
    /// Создаёт снимок прогресса. / Creates a progress snapshot.
    /// </summary>
    public OperationProgress(
        string? currentFile, long bytesDone, long bytesTotal,
        long totalBytesDone, long totalBytes, long filesDone, long filesTotal)
    {
        CurrentFile = currentFile;
        BytesDone = bytesDone;
        BytesTotal = bytesTotal;
        TotalBytesDone = totalBytesDone;
        TotalBytes = totalBytes;
        FilesDone = filesDone;
        FilesTotal = filesTotal;
        Percent = totalBytes <= 0 ? (filesTotal == 0 ? 100.0 : 0.0)
                                  : Math.Min(100.0, 100.0 * totalBytesDone / totalBytes);
    }

    /// <summary>Возвращает краткое описание для логов/UI. / Returns a short description for logs/UI.</summary>
    public override string ToString()
        => $"{FilesDone}/{FilesTotal} files, {Percent:0.0}% ({TotalBytesDone}/{TotalBytes} bytes)";
}
