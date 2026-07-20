namespace CoderCommander.Archives;

/// <summary>One entry paired with an open stream over its content, yielded by
/// <see cref="IArchiveReader.ScanAsync"/>. Valid only until the enumeration moves past it -
/// sequential-access formats (tar.gz, solid 7z/rar) can't rewind to re-open a prior entry.</summary>
public readonly record struct ArchiveEntryStream(ArchiveEntryRecord Entry, Stream Content);

/// <summary>
/// Reads an archive's directory listing and entry contents. Two access shapes exist because not
/// every format can seek to entry N without decoding everything before it (tar.gz, solid 7z/rar):
/// callers check <see cref="SupportsRandomAccess"/> and use <see cref="OpenEntry"/> when true, or
/// <see cref="ScanAsync"/> (a single forward pass) when false.
/// </summary>
public interface IArchiveReader : IDisposable
{
    /// <summary>Full listing. May require a full decode pass for sequential formats.</summary>
    Task<ArchiveDirectory> ReadDirectoryAsync(CancellationToken ct = default);

    /// <summary>True when <see cref="OpenEntry"/> is cheap (ZIP, plain TAR, non-solid 7z).</summary>
    bool SupportsRandomAccess { get; }

    /// <summary>Opens one entry's content directly. Throws <see cref="NotSupportedException"/>
    /// when <see cref="SupportsRandomAccess"/> is false - use <see cref="ScanAsync"/> instead.</summary>
    Stream OpenEntry(ArchiveEntryRecord entry);

    /// <summary>Forward-only pass over every entry in container order.</summary>
    IAsyncEnumerable<ArchiveEntryStream> ScanAsync(CancellationToken ct = default);
}
