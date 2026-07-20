using System.Threading;

namespace CoderCommander.Operations;

/// <summary>
/// Настройки операции копирования/перемещения.
/// Options for copy/move operation.
/// </summary>
public sealed class TransferOptions
{
    /// <summary>Размер буфера в байтах. / Buffer size in bytes.</summary>
    public int BufferSize { get; set; } = 1 << 20;

    /// <summary>Копировать атрибуты. / Copy file attributes.</summary>
    public bool CopyAttributes { get; set; } = true;

    /// <summary>Копировать временные метки. / Copy file timestamps.</summary>
    public bool CopyTimestamps { get; set; } = true;

    /// <summary>Резервировать место на диске. / Reserve disk space before copy.</summary>
    public bool ReserveDiskSpace { get; set; }

    /// <summary>Копировать NTFS ACL (права доступа). Только для локальных операций. / Copy NTFS permissions. Local only.</summary>
    public bool CopyNtfsPermissions { get; set; }

    /// <summary>Событие паузы (ManualResetEventSlim). / Pause event.</summary>
    public ManualResetEventSlim? PauseEvent { get; set; }

    /// <summary>Флаг пропуска текущего файла. / Skip-current-file flag (volatile).</summary>
    public Func<bool>? SkipCurrentFileFunc { get; set; }
}
