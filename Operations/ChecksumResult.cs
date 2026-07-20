namespace CoderCommander.Operations;

/// <summary>
/// Результат расчёта или проверки контрольной суммы одного файла.
/// Result of calculating or verifying a single file's checksum.
/// </summary>
public sealed class ChecksumResult
{
    /// <summary>Путь к файлу (абсолютный или относительно sum-файла). / File path.</summary>
    public string FilePath { get; }

    /// <summary>Вычисленная (или ожидаемая при проверке) сумма в hex. / Computed (or expected, on verify) hex hash.</summary>
    public string Hash { get; }

    /// <summary>Ожидаемая сумма из sum-файла (только при проверке). / Expected hash from the sum file (verify only).</summary>
    public string? ExpectedHash { get; }

    /// <summary>Размер файла в байтах. / File size in bytes.</summary>
    public long Size { get; }

    /// <summary>Длительность операции. / Operation duration.</summary>
    public System.TimeSpan Duration { get; }

    /// <summary>Статус проверки: null — просто рассчитано, true — совпало, false — несовпало. / Verify status.</summary>
    public bool? Match { get; }

    public ChecksumResult(string filePath, string hash, long size, System.TimeSpan duration, string? expectedHash = null, bool? match = null)
    {
        FilePath = filePath;
        Hash = hash;
        Size = size;
        Duration = duration;
        ExpectedHash = expectedHash;
        Match = match;
    }

    /// <summary>true, если при проверке обнаружено несовпадение. / true if a verify mismatch was detected.</summary>
    public bool IsMismatch => Match == false;
}
