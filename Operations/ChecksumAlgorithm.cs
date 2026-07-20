namespace CoderCommander.Operations;

/// <summary>
/// Поддерживаемые алгоритмы контрольных сумм (ph1.2, exp.yml).
/// Supported checksum algorithms (ph1.2).
/// </summary>
public enum ChecksumAlgorithm
{
    /// <summary>MD5 (128 бит). / MD5 (128-bit).</summary>
    MD5,
    /// <summary>SHA-1 (160 бит). / SHA-1 (160-bit).</summary>
    SHA1,
    /// <summary>SHA-256 (256 бит). / SHA-256 (256-bit).</summary>
    SHA256,
    /// <summary>SHA-512 (512 бит). / SHA-512 (512-bit).</summary>
    SHA512,
    /// <summary>CRC-32 (32 бита, System.IO.Hashing). / CRC-32 (32-bit, System.IO.Hashing).</summary>
    CRC32
}
