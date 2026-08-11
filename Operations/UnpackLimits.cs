namespace CoderCommander.Operations;

/// <summary>
/// Every bound <see cref="UnpackOperation"/> holds an archive to before extraction starts, in one
/// file - the same arrangement as <c>Terminal/Vt/VtLimits.cs</c> and <c>FileSystem/Remote/RemoteLimits.cs</c>,
/// and for the same reason: an archive is untrusted input (it may not even have been downloaded by
/// the person extracting it), so "how much of this will we act on" must be answerable by reading
/// one screen rather than by auditing the extraction loop.
///
/// <para><see cref="Operations.UnpackOperation"/>'s free-space check already covers the classic
/// decompression-bomb shape - a small file whose declared uncompressed size would fill the disk -
/// by comparing the declared total directly against what the destination actually has free, which
/// is a strictly more accurate bound than any fixed constant here could be. What it does not cover,
/// and what these three do, is an archive that would technically fit on disk but is still
/// pathological in a way free space doesn't measure: absurdly many entries, an absurd
/// compression ratio, or absurdly deep nesting.</para>
/// </summary>
public static class UnpackLimits
{
    /// <summary>Entries refused past this count, before extraction starts. Protects against an
    /// archive engineered to have millions of tiny (even empty) entries - each one is a file
    /// created, a directory possibly created, a listing row - regardless of how few total bytes
    /// they declare, which is exactly what a total-size or free-space check cannot catch.</summary>
    public const int MaxEntries = 200_000;

    /// <summary>Uncompressed-to-compressed ratio refused past this multiple. Chosen well above
    /// anything a legitimate file produces under ordinary compression (text/source archives
    /// routinely sit under 10:1; even highly repetitive data rarely clears a few hundred to one)
    /// and well below what a deliberately crafted bomb reaches (the classic examples run into the
    /// thousands or millions to one). Computed only over entries that report a compressed size -
    /// see <see cref="Operations.UnpackOperation"/> for what happens when none do.</summary>
    public const int MaxRatio = 500;

    /// <summary>Path segments (directory levels) refused past this depth for a single entry.
    /// Deep nesting is a second, less well-known decompression-bomb technique - "42.zip"-style
    /// archives use it alongside a high ratio - and, independently of that, a legitimate archive
    /// has no reason to nest anywhere near this deep.</summary>
    public const int MaxPathDepth = 64;
}
