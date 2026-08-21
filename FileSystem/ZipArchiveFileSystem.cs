using CoderCommander.Archives;
using CoderCommander.Services;
using CoderCommander.Utils;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace CoderCommander.FileSystem;

/// <summary>
/// IFileSystem implementation backed by a ZIP archive file.
/// </summary>
public sealed class ZipArchiveFileSystem : IFileSystem, IBatchDeletableFileSystem
{
    private readonly string _archivePath;

    /// <inheritdoc/>
    public string Name => "ZIP";

    /// <inheritdoc/>
    /// <remarks>A virtual tree inside a single file: none of the OS-level side channels apply, and
    /// its paths (<c>archive.zip|inner/name</c>) are not valid OS paths at all.</remarks>
    /// <summary>ZIP always supports adding and deleting entries (unlike e.g. RAR/7z/TAR.XZ via
    /// <c>Archives.ArchiveFileSystem</c>), so this declares both write-side flags unconditionally.
    /// Still no <see cref="FileSystemCapabilities.NativePaths"/> - a ZIP is a purely virtual tree.</summary>
    public FileSystemCapabilities Capabilities => FileSystemCapabilities.Writable | FileSystemCapabilities.Deletable;

    /// <summary>Path to the underlying ZIP archive file on disk.</summary>
    public string ArchivePath => _archivePath;

    /// <summary>Opens a ZIP archive at <paramref name="archivePath"/> for browsing and modification.</summary>
    public ZipArchiveFileSystem(string archivePath)
    {
        _archivePath = archivePath;
    }

    private static readonly Encoding Cp866 = Encoding.GetEncoding(866);
    private static readonly Encoding Utf8 = Encoding.UTF8;

    /// <summary>HRESULT for Win32 ERROR_SHARING_VIOLATION (0x20), which FileStream surfaces as an
    /// IOException when the file is locked by another process. Checking this instead of the
    /// exception's Message text (as this retry used to) works regardless of the OS/CLR display
    /// language - IOException.Message is localized, so a substring match silently stops matching
    /// (and the retry that exists specifically to survive AV/indexer locks goes dead) on any
    /// non-English Windows install.</summary>
    private const int ErrorSharingViolationHResult = unchecked((int)0x80070020);

    /// <summary>True if <paramref name="ex"/> represents the file being locked by another process
    /// (Win32 ERROR_SHARING_VIOLATION) - locale-independent, unlike matching its Message text.</summary>
    private static bool IsSharingViolation(IOException ex) => ex.HResult == ErrorSharingViolationHResult;

    /// <summary>
    /// Packers that cannot store a character in the chosen OEM code page often fall back to a
    /// textual escape such as <c>%U0306</c>. Such sequences must be turned back into real characters.
    /// </summary>
    private static readonly Regex EscapedCodePointPattern =
        new(@"%[Uu]([0-9A-Fa-f]{4,6})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Decoded central-directory record. <see cref="Index"/> matches the ordinal
    /// position used by <see cref="System.IO.Compression.ZipArchive.Entries"/>, which lets callers
    /// address an entry by position instead of by its (possibly mis-decoded) name.</summary>
    public sealed class ZipEntryRecord
    {
        /// <summary>Ordinal position in the central directory, matching the index in <see cref="ZipArchive.Entries"/>.</summary>
        public int Index { get; init; }

        /// <summary>Full path of the entry within the archive (normalized to forward slashes).</summary>
        public string FullName { get; init; } = "";

        /// <summary>True when the entry represents a directory.</summary>
        public bool IsDirectory { get; init; }

        /// <summary>Uncompressed size in bytes.</summary>
        public long Size { get; init; }

        /// <summary>Compressed size in bytes.</summary>
        public long CompressedSize { get; init; }

        /// <summary>Last modification time in UTC.</summary>
        public DateTime LastWriteTimeUtc { get; init; }

        /// <summary>True when the entry's general-purpose bit flag 0 (encrypted) is set - covers
        /// classic ZipCrypto and the AES compression-method-0x63 scheme alike, since both set this
        /// bit regardless of which one actually encrypts the entry. Before this was tracked,
        /// <c>UnpackOperation</c>'s "skip encrypted entries instead of crashing" branch was dead
        /// code for every ZIP (audit finding G045) - it always saw <c>false</c>.</summary>
        public bool IsEncrypted { get; init; }

        /// <summary>True for a UNIX symbolic link entry (detected via the external-attributes
        /// field's high word when "version made by"'s host byte indicates UNIX). Windows-authored
        /// ZIPs essentially never set this - Explorer/7-Zip/WinRAR on Windows don't create ZIP
        /// symlink entries - so <c>false</c> is the overwhelmingly common, correct default.</summary>
        public bool IsLink { get; init; }

        /// <summary>DOS-compatible attribute bits (ReadOnly/Hidden/System/Archive) decoded from the
        /// external-attributes field's low byte - the same convention essentially every ZIP tool
        /// follows regardless of which OS actually wrote the archive, Windows tools always and most
        /// UNIX tools by convention for cross-platform compatibility. <see cref="FileAttributes.Directory"/>
        /// is deliberately excluded here even if the bit is set - <see cref="IsDirectory"/> already
        /// carries that, decided from the entry name/marker, not from a possibly-wrong stored bit.</summary>
        public FileAttributes DosAttributes { get; init; }
    }

    /// <summary>Immutable snapshot of an archive's central directory.</summary>
    public sealed class ZipDirectory
    {
        /// <summary>Empty directory used as a safe default when the archive cannot be read.</summary>
        public static readonly ZipDirectory Empty = new(Array.Empty<ZipEntryRecord>(), false);

        /// <summary>All entries in the central directory.</summary>
        public IReadOnlyList<ZipEntryRecord> Entries { get; }

        /// <summary>True when at least one name is stored in an OEM code page rather than UTF-8.</summary>
        public bool HasLegacyNames { get; }

        private readonly Lazy<(ZipEntryRecord Entry, string NormalizedName)[]> _normalizedEntries;

        /// <summary>Pre-computed trimmed names (directory entries carry a trailing "/" in
        /// <see cref="ZipEntryRecord.FullName"/>; backslashes are already normalized to forward
        /// slashes and "./" already stripped at parse time - see <c>ParseCentralDirectory</c>).
        /// Lazy for the same reason as <c>Archives.ArchiveDirectory.NormalizedEntries</c>: not
        /// every caller of <see cref="Entries"/> needs it.</summary>
        public IReadOnlyList<(ZipEntryRecord Entry, string NormalizedName)> NormalizedEntries => _normalizedEntries.Value;

        private readonly Lazy<Utils.PrefixTreeIndex<ZipEntryRecord>> _index;

        /// <summary>'/'-segmented prefix tree over <see cref="NormalizedEntries"/> - see
        /// <see cref="Utils.PrefixTreeIndex{T}"/>'s own doc comment. Turns
        /// EnumerateAsync/EnumerateDeepAsync/GetFileInfoAsync/ExistsAsync from an O(n) scan of
        /// every entry in the archive into O(children)/O(1)/O(1), built once per cached snapshot.</summary>
        internal Utils.PrefixTreeIndex<ZipEntryRecord> Index => _index.Value;

        /// <summary>Creates a new directory snapshot.</summary>
        public ZipDirectory(IReadOnlyList<ZipEntryRecord> entries, bool hasLegacyNames)
        {
            Entries = entries;
            HasLegacyNames = hasLegacyNames;
            _normalizedEntries = new Lazy<(ZipEntryRecord, string)[]>(() =>
            {
                var result = new (ZipEntryRecord, string)[entries.Count];
                for (var i = 0; i < entries.Count; i++)
                    result[i] = (entries[i], entries[i].FullName.Trim('/'));
                return result;
            });
            _index = new Lazy<Utils.PrefixTreeIndex<ZipEntryRecord>>(
                () => new Utils.PrefixTreeIndex<ZipEntryRecord>(NormalizedEntries, e => e.LastWriteTimeUtc));
        }
    }

    private readonly record struct DirectoryStamp(long Length, long Ticks);

    /// <summary>Distinct archive paths kept at once, evicting the least-recently-used past this -
    /// same bound as <see cref="Archives.ArchiveDirectoryCache.MaxEntries"/>, which this mirrors.
    /// Kept as an independent copy rather than switching to that generic cache: it works over
    /// <see cref="ZipDirectory"/>/<see cref="ZipEntryRecord"/>, not <c>Archives.ArchiveDirectory</c>,
    /// and its API is synchronous throughout (<see cref="ReadDirectory"/>, not
    /// <c>GetOrReadAsync</c>) - the same "ZIP keeps its own copy" split documented at the top of
    /// this class.</summary>
    private const int MaxDirectoryCacheEntries = 64;

    private static readonly Dictionary<string, (DirectoryStamp Stamp, ZipDirectory Directory)> DirectoryCache =
        new(StringComparer.OrdinalIgnoreCase);

    // Most-recently-used at the front (First); least-recently-used at the back (Last). Must only
    // be touched while holding the DirectoryCache lock, same as DirectoryCache itself.
    private static readonly LinkedList<string> DirectoryCacheLruOrder = new();
    private static readonly Dictionary<string, LinkedListNode<string>> DirectoryCacheLruNodes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-archive-path lock objects, so two threads reading the same never-yet-cached
    /// archive at the same moment serialize onto one <see cref="ParseCentralDirectory"/> call
    /// instead of both parsing it independently (audit finding G043 - two panels opening the same
    /// large ZIP at once used to each pay the full parse cost). Guarded by <see cref="DirectoryCache"/>'s
    /// own lock, and swept whenever an entry leaves that cache so this never outlives it.</summary>
    private static readonly Dictionary<string, object> ParseLocks = new(StringComparer.OrdinalIgnoreCase);

    private static object GetParseLock(string archivePath)
    {
        lock (DirectoryCache)
        {
            if (!ParseLocks.TryGetValue(archivePath, out var lockObj))
                ParseLocks[archivePath] = lockObj = new object();
            return lockObj;
        }
    }

    private static DirectoryStamp? TryStatArchive(string archivePath)
    {
        try
        {
            var info = new FileInfo(archivePath);
            return info.Exists ? new DirectoryStamp(info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads (and memoises) the central directory. The cache key carries the file length and
    /// timestamp, so any external modification of the archive invalidates it automatically.
    /// </summary>
    public static ZipDirectory ReadDirectory(string archivePath)
    {
        if (TryStatArchive(archivePath) is not { } stamp)
            return ZipDirectory.Empty;

        lock (DirectoryCache)
        {
            if (DirectoryCache.TryGetValue(archivePath, out var cached) && cached.Stamp == stamp)
            {
                TouchDirectoryCache(archivePath);
                return cached.Directory;
            }
        }

        // Serialize on a per-archive-path lock, not the shared DirectoryCache lock, so parsing
        // archive A never blocks a concurrent read of unrelated archive B.
        lock (GetParseLock(archivePath))
        {
            // Another thread may have already parsed and cached this exact stamp while this one
            // was waiting for the lock above - re-check before parsing again.
            lock (DirectoryCache)
            {
                if (DirectoryCache.TryGetValue(archivePath, out var cached2) && cached2.Stamp == stamp)
                {
                    TouchDirectoryCache(archivePath);
                    return cached2.Directory;
                }
            }

            ZipDirectory parsed;
            try
            {
                parsed = ParseCentralDirectory(archivePath);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                LogService.Warning($"Archive locked by another process, retrying: {archivePath}");

                // A single 100ms retry proved too short in practice — a freshly written archive can
                // stay locked for a second or more while Windows Defender/the search indexer scans
                // it. Back off across a few attempts instead of giving up after one.
                ReadOnlySpan<int> retryDelaysMs = [150, 300, 600];
                Exception lastError = ex;
                parsed = ZipDirectory.Empty;
                var succeeded = false;
                foreach (var delayMs in retryDelaysMs)
                {
                    Thread.Sleep(delayMs);
                    try
                    {
                        parsed = ParseCentralDirectory(archivePath);
                        succeeded = true;
                        break;
                    }
                    catch (Exception ex2)
                    {
                        lastError = ex2;
                    }
                }

                if (!succeeded)
                {
                    LogService.Error($"Cannot read archive directory after {retryDelaysMs.Length} retries: {archivePath}: {lastError.Message}", lastError);
                    // Prefer a stale-but-real listing over an empty one - the archive momentarily
                    // looking empty while another operation holds it is far more misleading than
                    // briefly showing its last known contents.
                    lock (DirectoryCache)
                    {
                        if (DirectoryCache.TryGetValue(archivePath, out var stale))
                            return stale.Directory;
                    }
                    return ZipDirectory.Empty;
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"Cannot read archive directory: {archivePath}: {ex.Message}", ex);
                lock (DirectoryCache)
                {
                    if (DirectoryCache.TryGetValue(archivePath, out var stale))
                        return stale.Directory;
                }
                return ZipDirectory.Empty;
            }

            // Re-stat after parsing: if the archive was rewritten while ParseCentralDirectory was
            // running, the snapshot just built no longer matches the file's current content and
            // must not be cached under the pre-parse stamp - the next access should re-read for
            // real rather than serve this stale-on-arrival snapshot indefinitely.
            if (TryStatArchive(archivePath) == stamp)
            {
                lock (DirectoryCache)
                {
                    DirectoryCache[archivePath] = (stamp, parsed);
                    TouchDirectoryCache(archivePath);
                    EvictLeastRecentlyUsedDirectoryCacheEntryIfOverCapacity();
                }
            }
            return parsed;
        }
    }

    /// <summary>Drops the memoised directory of an archive.</summary>
    public static void Forget(string archivePath)
    {
        lock (DirectoryCache)
        {
            DirectoryCache.Remove(archivePath);
            if (DirectoryCacheLruNodes.Remove(archivePath, out var node))
                DirectoryCacheLruOrder.Remove(node);
            ParseLocks.Remove(archivePath);
        }
    }

    /// <summary>Moves <paramref name="archivePath"/> to the most-recently-used end. Must be called
    /// with the <see cref="DirectoryCache"/> lock already held.</summary>
    private static void TouchDirectoryCache(string archivePath)
    {
        if (DirectoryCacheLruNodes.TryGetValue(archivePath, out var existing))
            DirectoryCacheLruOrder.Remove(existing);
        DirectoryCacheLruNodes[archivePath] = DirectoryCacheLruOrder.AddFirst(archivePath);
    }

    /// <summary>Must be called with the <see cref="DirectoryCache"/> lock already held.</summary>
    private static void EvictLeastRecentlyUsedDirectoryCacheEntryIfOverCapacity()
    {
        while (DirectoryCache.Count > MaxDirectoryCacheEntries && DirectoryCacheLruOrder.Last is { } lru)
        {
            DirectoryCache.Remove(lru.Value);
            DirectoryCacheLruNodes.Remove(lru.Value);
            DirectoryCacheLruOrder.RemoveLast();
            ParseLocks.Remove(lru.Value);
        }
    }

    private IReadOnlyList<ZipEntryRecord> GetEntries() => ReadDirectory(_archivePath).Entries;

    private static ZipDirectory ParseCentralDirectory(string archivePath)
    {
        var legacyNames = false;

        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

        // Find End of Central Directory record
        var fileSize = fs.Length;
        long eocdOffset = -1;

        // Search backwards from end of file (EOCD is at least 22 bytes, comment can be up to
        // 65535) - read the whole search window into memory once and scan it there. The previous
        // byte-at-a-time version set fs.Position on every iteration, which resets FileStream's
        // internal buffer, turning what looks like a buffered read into a separate seek+read
        // syscall per byte (up to 65,557 of them). A non-ZIP file (signature never found) always
        // pays the full cost, and ArchiveFormatRegistry runs signature detection on every Detect()
        // call, not just once per archive.
        var searchStart = Math.Max(0, fileSize - 22 - 65535);
        var tailLength = checked((int)(fileSize - searchStart));
        if (tailLength >= 22)
        {
            fs.Position = searchStart;
            var tail = new byte[tailLength];
            var totalRead = 0;
            while (totalRead < tailLength)
            {
                var n = fs.Read(tail, totalRead, tailLength - totalRead);
                if (n == 0) break;
                totalRead += n;
            }

            // EOCD signature "PK\x05\x06", little-endian as bytes: 0x50 0x4B 0x05 0x06.
            for (var i = totalRead - 22; i >= 0; i--)
            {
                if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
                {
                    eocdOffset = searchStart + i;
                    break;
                }
            }
        }

        if (eocdOffset < 0)
            return ZipDirectory.Empty;

        // Read EOCD
        fs.Position = eocdOffset + 4;
        reader.ReadUInt16(); // disk number
        reader.ReadUInt16(); // disk with CD
        reader.ReadUInt16(); // entries on this disk
        long totalEntries = reader.ReadUInt16();
        reader.ReadUInt32(); // central directory size
        long cdOffset = reader.ReadUInt32();

        // ZIP64: a 0xFFFF/0xFFFFFFFF sentinel in the standard EOCD means the real values live in
        // the ZIP64 EOCD record, reached via a fixed 20-byte locator immediately preceding the
        // standard EOCD. Without this, archives over 4 GB or with more than 65535 entries get a
        // truncated cdOffset/totalEntries here, silently desyncing our index from what
        // System.IO.Compression.ZipArchive (used for actual content/writes) sees.
        if (totalEntries == 0xFFFF || cdOffset == 0xFFFFFFFF)
            TryReadZip64Eocd(fs, reader, eocdOffset, ref totalEntries, ref cdOffset);

        // Read Central Directory
        fs.Position = cdOffset;
        var records = new List<ZipEntryRecord>((int)Math.Min(totalEntries, 1 << 20));
        int entryIndex = 0;

        // Ceiling on how many entries a single listing will parse, independent of what the file's
        // own EOCD/ZIP64-EOCD record claims (audit finding G044). Without this, a crafted archive
        // whose ZIP64 locator claims an astronomically large entry count is bounded only by how
        // many minimal-size fake central-directory headers physically fit in the file - a
        // maliciously packed few-hundred-MB file can hold tens of millions of them, each becoming a
        // heap-allocated ZipEntryRecord well before the truncation guard below ever triggers.
        // Mirrors Operations.UnpackLimits.MaxEntries (200,000) - not referenced directly, since
        // Archives/FileSystem stays below Operations/ in the dependency layering documented in
        // CLAUDE.md.
        const long MaxCentralDirectoryEntries = 200_000;
        if (totalEntries > MaxCentralDirectoryEntries)
        {
            LogService.Warning($"Archive {archivePath}: central directory claims {totalEntries:N0} entries, more than the {MaxCentralDirectoryEntries:N0} this app will list - truncating.");
            totalEntries = MaxCentralDirectoryEntries;
        }

        for (long i = 0; i < totalEntries; i++)
        {
            var sig = reader.ReadUInt32();
            if (sig != 0x02014b50) break; // CD file header signature

            var versionMadeBy = reader.ReadUInt16();
            reader.ReadUInt16(); // version needed
            var flags = reader.ReadUInt16();
            reader.ReadUInt16(); // compression method
            var modTime = reader.ReadUInt16();
            var modDate = reader.ReadUInt16();
            reader.ReadUInt32(); // crc32
            long compressedSize = reader.ReadUInt32();
            long uncompressedSize = reader.ReadUInt32();
            var filenameLen = reader.ReadUInt16();
            var extraLen = reader.ReadUInt16();
            var commentLen = reader.ReadUInt16();
            reader.ReadUInt16(); // disk number start
            reader.ReadUInt16(); // internal attrs
            var externalAttrs = reader.ReadUInt32();
            reader.ReadUInt32(); // local header offset

            // A truncated/corrupted file can claim lengths that run past EOF; without this check
            // that throws an EndOfStreamException from ReadBytes below, which the caller's retry
            // logic (meant for a transiently locked file) would misinterpret as "still locked" and
            // retry three times for nothing before giving up.
            if (filenameLen + extraLen + commentLen > fileSize - fs.Position)
            {
                LogService.Warning($"Archive {archivePath}: truncated/corrupt central directory record at entry {i} - stopping scan early.");
                break;
            }

            var filenameBytes = reader.ReadBytes(filenameLen);
            var extraBytes = reader.ReadBytes(extraLen);
            if (commentLen > 0)
                fs.Seek(commentLen, SeekOrigin.Current);

            if (compressedSize == 0xFFFFFFFF || uncompressedSize == 0xFFFFFFFF)
                ReadZip64Sizes(extraBytes, ref uncompressedSize, ref compressedSize);

            var filename = DecodeEntryName(filenameBytes, flags, out var isLegacyName);
            legacyNames |= isLegacyName;

            var normalized = filename.Replace('\\', '/');
            // Strip "./" prefix (e.g. from Info-ZIP or similar tools)
            if (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized[2..];

            // General-purpose bit 0 (0x0001) marks the entry as encrypted for both classic
            // ZipCrypto and AES (compression method 0x63) - either way, this app has no
            // password-prompt UI, so knowing this up front lets extraction skip the entry cleanly
            // instead of the reader throwing a raw crypto exception when the stream is touched.
            var isEncrypted = (flags & 0x0001) != 0;

            // DOS-compatible attribute byte (ReadOnly/Hidden/System/Archive) is the external
            // attributes field's low byte - a convention followed by Windows tools always and most
            // UNIX tools too for cross-platform compatibility, even though only the low 8 of the
            // 16-bit field are meaningfully "DOS" attributes. Directory is deliberately excluded -
            // IsDirectory is already decided from the name/marker, not trusted from this bit.
            const uint DosAttributeMask = 0x01 | 0x02 | 0x04 | 0x20; // ReadOnly | Hidden | System | Archive
            var dosAttributes = (FileAttributes)(externalAttrs & DosAttributeMask);

            // A UNIX symlink is marked in the external attributes field's HIGH word (the st_mode
            // bits a UNIX zip tool stored there), but only means anything when "version made by"'s
            // host byte says the archive was actually written on UNIX (3) - Windows tools reuse
            // that same 32-bit field for other things, so checking the mode bits without this guard
            // would occasionally misidentify an ordinary Windows-authored entry as a symlink.
            var isLink = (versionMadeBy >> 8) == 3 && ((externalAttrs >> 16) & 0xF000) == 0xA000;

            records.Add(new ZipEntryRecord
            {
                Index = entryIndex++,
                FullName = normalized,
                IsDirectory = normalized.EndsWith('/'),
                Size = uncompressedSize,
                CompressedSize = compressedSize,
                LastWriteTimeUtc = ParseDosDateTime(modDate, modTime),
                IsEncrypted = isEncrypted,
                IsLink = isLink,
                DosAttributes = dosAttributes
            });
        }

        return new ZipDirectory(records, legacyNames);
    }

    /// <summary>
    /// Locates and reads the ZIP64 End Of Central Directory record via its 20-byte locator
    /// (immediately before the standard EOCD), overwriting <paramref name="totalEntries"/>/
    /// <paramref name="cdOffset"/> with the real 64-bit values. Leaves both untouched (falling
    /// back to the already-truncated 32-bit values) if the locator/record isn't where expected -
    /// a best-effort archive is preferable to throwing on a merely unusual layout.
    /// </summary>
    private static void TryReadZip64Eocd(FileStream fs, BinaryReader reader, long eocdOffset, ref long totalEntries, ref long cdOffset)
    {
        var locatorOffset = eocdOffset - 20;
        if (locatorOffset < 0)
            return;

        fs.Position = locatorOffset;
        if (reader.ReadUInt32() != 0x07064b50) // ZIP64 EOCD locator signature
            return;

        reader.ReadUInt32(); // disk number holding the ZIP64 EOCD
        var zip64EocdOffset = (long)reader.ReadUInt64();
        // total number of disks (4 bytes) intentionally unread - irrelevant for single-disk archives

        fs.Position = zip64EocdOffset;
        if (reader.ReadUInt32() != 0x06064b50) // ZIP64 EOCD record signature
            return;

        reader.ReadUInt64(); // size of this record
        reader.ReadUInt16(); // version made by
        reader.ReadUInt16(); // version needed
        reader.ReadUInt32(); // number of this disk
        reader.ReadUInt32(); // disk with start of central directory
        reader.ReadUInt64(); // entries on this disk
        totalEntries = (long)reader.ReadUInt64();
        reader.ReadUInt64(); // size of central directory
        cdOffset = (long)reader.ReadUInt64();
    }

    /// <summary>
    /// Parses the ZIP64 extended-information extra field (header ID 0x0001) for a single central
    /// directory entry. Fields appear only for those that were 0xFFFFFFFF sentinels in the main
    /// 32-bit record, in a fixed order (uncompressed size, then compressed size, then others this
    /// caller doesn't need) - so only sentinel-valued <c>ref</c> parameters are overwritten.
    /// </summary>
    private static void ReadZip64Sizes(byte[] extra, ref long uncompressedSize, ref long compressedSize)
    {
        var needUncompressed = uncompressedSize == 0xFFFFFFFF;
        var needCompressed = compressedSize == 0xFFFFFFFF;

        var pos = 0;
        while (pos + 4 <= extra.Length)
        {
            var headerId = BitConverter.ToUInt16(extra, pos);
            var dataSize = BitConverter.ToUInt16(extra, pos + 2);
            var dataStart = pos + 4;
            if (dataStart + dataSize > extra.Length)
                break;

            if (headerId == 0x0001)
            {
                var fieldPos = dataStart;
                var fieldEnd = dataStart + dataSize;
                if (needUncompressed && fieldPos + 8 <= fieldEnd)
                {
                    var raw = BitConverter.ToUInt64(extra, fieldPos);
                    if (raw > (ulong)long.MaxValue) throw new InvalidDataException("ZIP64 uncompressed size exceeds long.MaxValue");
                    uncompressedSize = (long)raw;
                    fieldPos += 8;
                }
                if (needCompressed && fieldPos + 8 <= fieldEnd)
                {
                    var raw = BitConverter.ToUInt64(extra, fieldPos);
                    if (raw > (ulong)long.MaxValue) throw new InvalidDataException("ZIP64 compressed size exceeds long.MaxValue");
                    compressedSize = (long)raw;
                }
                return;
            }

            pos = dataStart + dataSize;
        }
    }

    /// <summary>
    /// Restores a human readable entry name: picks the right byte encoding, expands textual
    /// <c>%Uxxxx</c> escapes and composes decomposed diacritics (macOS style NFD) into NFC.
    /// </summary>
    private static string DecodeEntryName(byte[] rawName, ushort flags, out bool isLegacyName)
    {
        isLegacyName = false;
        if (rawName.Length == 0)
            return string.Empty;

        string text;
        if ((flags & 0x0800) != 0 || LooksLikeUtf8(rawName))
        {
            text = Utf8.GetString(rawName);
        }
        else
        {
            isLegacyName = true;
            text = Cp866.GetString(rawName);
        }

        text = ExpandEscapedCodePoints(text);
        return Compose(text);
    }

    private static bool LooksLikeUtf8(byte[] data)
    {
        var hasHighByte = false;
        foreach (var b in data)
        {
            if (b >= 0x80) { hasHighByte = true; break; }
        }
        if (!hasHighByte)
            return true;

        // System.Text.Unicode.Utf8.IsValid, not a try/catch around StrictUtf8.GetString - a
        // CP866-named archive (the exact case this method exists for) used to throw and catch a
        // DecoderFallbackException for EVERY non-ASCII entry name, at ~10-50us per throw; a
        // 200,000-entry Russian-named ZIP spent seconds purely in exception dispatch, plus
        // allocated a throwaway decoded string per entry that was immediately discarded either
        // way. This validates the bytes directly with no allocation and no exception.
        return System.Text.Unicode.Utf8.IsValid(data);
    }

    private static string ExpandEscapedCodePoints(string text)
    {
        if (text.IndexOf('%', StringComparison.Ordinal) < 0)
            return text;

        return EscapedCodePointPattern.Replace(text, m =>
            char.ConvertFromUtf32(Convert.ToInt32(m.Groups[1].Value, 16)));
    }

    private static string Compose(string text)
    {
        try
        {
            return text.IsNormalized(NormalizationForm.FormC)
                ? text
                : text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return text;
        }
    }

    private static DateTime ParseDosDateTime(ushort date, ushort time)
    {
        try
        {
            var year = ((date >> 9) & 0x7F) + 1980;
            var month = (date >> 5) & 0x0F;
            var day = date & 0x1F;
            var hour = (time >> 11) & 0x1F;
            var minute = (time >> 5) & 0x3F;
            var second = (time & 0x1F) * 2;

            if (month < 1) month = 1;
            if (month > 12) month = 12;
            if (day < 1) day = 1;
            if (day > DateTime.DaysInMonth(year, month)) day = DateTime.DaysInMonth(year, month);
            if (hour > 23) hour = 23;
            if (minute > 59) minute = 59;
            if (second > 59) second = 59;

            // DOS timestamps are wall-clock values of the machine that created the archive.
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local).ToUniversalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>Invalidates the cached central directory for this archive.</summary>
    public void InvalidateCache() => Forget(_archivePath);

    // EnumerateAsync/EnumerateDeepAsync/GetFileInfoAsync/ExistsAsync below all run their body via
    // Task.Run rather than the plain Task.FromResult a "GetEntries() is already cached" reading
    // would suggest: GetEntries() -> ReadDirectory can retry with a blocking Thread.Sleep for
    // seconds if the archive is transiently locked (AV/indexer scanning a just-written file), and
    // PanelViewModel's navigation path no longer forces a hop off the calling thread itself (see
    // its NavigateAsync/RefreshAsync comments) - without Task.Run here, that retry would freeze
    // the UI thread directly instead of just delaying the panel refresh.

    /// <inheritdoc/>
    /// <remarks>
    /// Queries <see cref="ZipDirectory.Index"/> (a <see cref="Utils.PrefixTreeIndex{T}"/>) instead
    /// of scanning every entry in the archive - previously an O(n) walk with a fresh
    /// <c>Replace('\')</c>/<c>StartsWith</c> per entry on every single navigation, which is what
    /// made browsing a ZIP with hundreds of thousands of entries freeze the panel on each folder
    /// click. A directory synthesized here purely from deeper entries (no entry of its own) shows
    /// the timestamp of whichever entry first touched it; an entry that has both children AND its
    /// own explicit directory-marker record instead always shows that marker's own timestamp,
    /// regardless of scan order - a minor, deliberate improvement over the old "whichever entry
    /// happened to be seen first" behavior, not merely a perf-neutral rewrite.
    /// </remarks>
    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
    {
        var (_, innerPath) = CoderCommander.FileSystem.ArchivePath.SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        var dir = ReadDirectory(_archivePath);
        var node = dir.Index.Navigate(innerPath);
        if (node == null)
            return Array.Empty<FileEntry>();

        var prefix = innerPath.Length == 0 ? "" : innerPath + "/";
        var result = new List<FileEntry>(node.Children.Count);
        foreach (var (name, child) in node.Children)
        {
            ct.ThrowIfCancellationRequested();

            // A node can be both a directory (has children, or an explicit "name/" marker entry)
            // and a file (a same-named entry with no trailing slash) in a pathological archive -
            // the linear scan this replaces showed both rows for that case too.
            if (child.Children.Count > 0 || child.Entry is { IsDirectory: true })
            {
                result.Add(new FileEntry(CoderCommander.FileSystem.ArchivePath.MakePath(_archivePath, prefix + name),
                    true, lastWriteTimeUtc: child.LastWriteTimeUtc, attributes: child.Entry?.DosAttributes ?? default));
            }
            if (child.Entry is { IsDirectory: false } file)
            {
                result.Add(new FileEntry(CoderCommander.FileSystem.ArchivePath.MakePath(_archivePath, prefix + name),
                    false, true, file.Size, lastWriteTimeUtc: file.LastWriteTimeUtc, attributes: file.DosAttributes));
            }
        }

        return result;
    }, ct);

    /// <inheritdoc/>
    /// <remarks>Never synthesizes a row for an implicit folder (one with no entry of its own) -
    /// only entries genuinely present in the archive are returned, matching the linear scan this
    /// replaces exactly (unlike <see cref="EnumerateAsync"/>, which does synthesize immediate
    /// child folders one level down).</remarks>
    public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
    {
        var (_, innerPath) = CoderCommander.FileSystem.ArchivePath.SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        var dir = ReadDirectory(_archivePath);
        var node = dir.Index.Navigate(innerPath);
        if (node == null)
            return Array.Empty<FileEntry>();

        var result = new List<FileEntry>();
        CollectDeepEntries(node, _archivePath, innerPath, result, ct);
        return result;
    }, ct);

    private static void CollectDeepEntries(
        Utils.PrefixTreeIndex<ZipEntryRecord>.Node node, string archivePath, string normalizedPath,
        List<FileEntry> result, CancellationToken ct)
    {
        foreach (var (name, child) in node.Children)
        {
            ct.ThrowIfCancellationRequested();
            var childPath = normalizedPath.Length == 0 ? name : normalizedPath + "/" + name;
            if (child.Entry is { } entry)
            {
                var fullPath = CoderCommander.FileSystem.ArchivePath.MakePath(archivePath, childPath);
                result.Add(entry.IsDirectory
                    ? new FileEntry(fullPath, true, lastWriteTimeUtc: entry.LastWriteTimeUtc, attributes: entry.DosAttributes)
                    : new FileEntry(fullPath, false, true, entry.Size, lastWriteTimeUtc: entry.LastWriteTimeUtc, attributes: entry.DosAttributes));
            }
            CollectDeepEntries(child, archivePath, childPath, result, ct);
        }
    }

    /// <inheritdoc/>
    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) =>
        Task.Run<FileEntry?>(() =>
    {
        var (_, innerPath) = CoderCommander.FileSystem.ArchivePath.SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
        {
            var rootPath = CoderCommander.FileSystem.ArchivePath.MakePath(_archivePath, "");
            return new FileEntry(rootPath, true);
        }

        var dir = ReadDirectory(_archivePath);
        if (dir.Index.TryGetExact(innerPath, out var entry) && entry != null)
        {
            var fullPath = CoderCommander.FileSystem.ArchivePath.MakePath(_archivePath, innerPath);
            return new FileEntry(fullPath, entry.IsDirectory, true, entry.Size, lastWriteTimeUtc: entry.LastWriteTimeUtc, attributes: entry.DosAttributes);
        }

        var node = dir.Index.Navigate(innerPath);
        if (node != null && node.Children.Count > 0)
        {
            var fullPath = CoderCommander.FileSystem.ArchivePath.MakePath(_archivePath, innerPath);
            return new FileEntry(fullPath, true);
        }

        return null;
    }, ct);

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
    {
        var (_, innerPath) = CoderCommander.FileSystem.ArchivePath.SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            return true;

        var dir = ReadDirectory(_archivePath);
        return dir.Index.Navigate(innerPath) != null;
    }, ct);

    /// <inheritdoc/>
    public async Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        var (_, srcInner) = CoderCommander.FileSystem.ArchivePath.SplitPath(source);
        srcInner = srcInner.Replace('\\', '/');

        var tempFile = TempFileNaming.NextTo(_archivePath, "extract");
        try
        {
            using (var zip = ZipFile.OpenRead(_archivePath))
            {
                var entry = FindEntry(zip, srcInner);
                if (entry == null)
                    throw new FileNotFoundException($"Entry not found in archive: {srcInner}");
                using var s = entry.Open();
                using var fs = File.Create(tempFile);
                await s.CopyToAsync(fs, ct);
            }

            if (CoderCommander.FileSystem.ArchivePath.IsArchivePath(destination))
            {
                var (dstArchive, rawInner) = CoderCommander.FileSystem.ArchivePath.SplitPath(destination);
                var dstInner = VfsPath.NormalizeInner(rawInner);
                if (dstInner.Length == 0)
                    throw new IOException("Cannot write to the archive root without an entry name.");

                using var session = OpenForUpdate(dstArchive, new[] { dstInner });
                var zip = session.Archive;
                if (overwrite)
                    FindEntry(zip, dstArchive, dstInner)?.Delete();

                var dstEntry = zip.CreateEntry(dstInner, CompressionLevel.Optimal);
                using var ts = File.OpenRead(tempFile);
                using var es = dstEntry.Open();
                await ts.CopyToAsync(es, ct);
                session.Commit();
                Forget(dstArchive);
            }
            else if (RemotePath.IsRemote(destination))
            {
                // Same reasoning as ArchiveFileSystem.CopyFileAsync's identical guard: this type has
                // no reference to the live connection a remote destination would need, and the
                // operations layer never actually reaches this branch (Copy/MoveOperation transfer
                // via the destination's own IFileSystem.CopyFromStreamAsync, never through here) -
                // but without this check, a "sftp://host/x.txt" destination would silently be
                // File.Copy'd as a literal local Windows path instead of failing.
                throw new NotSupportedException(
                    $"\"{destination}\" is a remote path; use the destination's own IFileSystem instead of ZipArchiveFileSystem.CopyFileAsync.");
            }
            else
            {
                var dir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(tempFile, destination, overwrite);
            }
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort cleanup */ }
        }
    }

    /// <summary>
    /// Locates an entry by its decoded name. Because <see cref="ZipArchive"/> may decode OEM names
    /// differently, the ordinal position from our own central-directory scan is the reliable link.
    /// </summary>
    private ZipArchiveEntry? FindEntry(ZipArchive zip, string name) => FindEntry(zip, _archivePath, name);

    /// <summary>Finds a <see cref="ZipArchiveEntry"/> by its decoded name, using the cached central directory index for reliable matching.</summary>
    internal static ZipArchiveEntry? FindEntry(ZipArchive zip, string archivePath, string name)
    {
        if (zip.Mode == ZipArchiveMode.Create)
            return null;

        var normalized = name.Replace('\\', '/').Trim('/');
        if (normalized.Length == 0)
            return null;

        var direct = zip.GetEntry(normalized) ?? zip.GetEntry(normalized + "/");
        if (direct != null)
            return direct;

        var record = ReadDirectory(archivePath).Entries
            .FirstOrDefault(e => Matches(e.FullName, normalized));
        if (record != null && record.Index < zip.Entries.Count)
            return zip.Entries[record.Index];

        return zip.Entries.FirstOrDefault(e => Matches(e.FullName, normalized));

        static bool Matches(string candidate, string wanted) =>
            string.Equals(candidate.Replace('\\', '/').Trim('/'), wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renames/moves an entry within this archive using a single <see cref="ZipUpdateSession"/> -
    /// audit finding G054: the previous implementation (<see cref="CopyFileAsync"/> then
    /// <see cref="DeleteAsync"/>) opened two independent sessions, and each one's own doc comment
    /// on <see cref="ZipUpdateSession.Open"/> explains why that means two full byte-for-byte copies
    /// of the whole archive to a temp file for what is, for an interactive F2 rename, typically a
    /// change to one entry's name. This class's own <see cref="MoveAsync"/> is only ever reached
    /// with both <paramref name="source"/> and <paramref name="destination"/> inside THIS instance's
    /// own <c>_archivePath</c> - <see cref="Operations.MoveOperation.CanRenameInPlace"/> requires
    /// <c>ReferenceEquals(sourceFs, destFs)</c> for a provider with no <see cref="FileSystemCapabilities.NativePaths"/>
    /// (ZIP), and the interactive rename command reads/writes through one panel's one
    /// <see cref="FileSystemCapabilities"/>-checked <c>IFileSystem</c> instance - so there is no
    /// cross-archive case to special-case here.
    /// <para>
    /// Still recompresses (<see cref="ZipArchiveEntry.Open"/> has no raw-compressed-bytes escape
    /// hatch in <see cref="System.IO.Compression"/>), but that CPU cost is unavoidable either way;
    /// what this removes is the second full-archive I/O pass <see cref="DeleteAsync"/> used to add
    /// on top via its own separate session.
    /// </para>
    /// </summary>
    public Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default) => Task.Run(() =>
    {
        var srcInner = VfsPath.NormalizeInner(CoderCommander.FileSystem.ArchivePath.SplitPath(source).innerPath.Replace('\\', '/'));
        var dstInner = VfsPath.NormalizeInner(CoderCommander.FileSystem.ArchivePath.SplitPath(destination).innerPath.Replace('\\', '/'));
        if (srcInner.Length == 0 || dstInner.Length == 0)
            throw new IOException("Cannot move to/from the archive root without an entry name.");

        using var session = OpenForUpdate(_archivePath, new[] { dstInner });
        var zip = session.Archive;

        var srcEntry = FindEntry(zip, _archivePath, srcInner) ?? FindEntry(zip, _archivePath, srcInner + "/")
            ?? throw new FileNotFoundException($"Entry not found in archive: {srcInner}");
        var isDirectory = srcEntry.FullName.EndsWith('/');

        if (isDirectory)
        {
            // Directory.Move(path, path) semantics for a non-empty folder: refuse rather than
            // silently rename just the marker and orphan the children still filed under the old
            // prefix - the same guard DeleteAsync(recursive:false) already applies (see its own
            // doc comment), checked here BEFORE any entry is touched so a refusal leaves the
            // archive completely untouched instead of a half-renamed state (the old
            // CopyFileAsync-then-DeleteAsync path could reach exactly that: CopyFileAsync would
            // copy only the empty marker, then DeleteAsync would throw on the non-empty original,
            // leaving both an orphaned new empty marker AND the untouched original in place).
            var oldPrefix = srcInner + "/";
            var hasDescendants = GetEntries().Any(e =>
            {
                var name = e.FullName.Replace('\\', '/');
                return !string.Equals(name, srcInner, StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(name, oldPrefix, StringComparison.OrdinalIgnoreCase) &&
                       name.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase);
            });
            if (hasDescendants)
                throw new IOException($"\"{srcInner}\" is not empty.");
        }

        var newName = isDirectory ? dstInner.TrimEnd('/') + "/" : dstInner;

        var existing = FindEntry(zip, _archivePath, newName);
        if (existing != null && existing != srcEntry)
        {
            if (!overwrite)
                throw new IOException($"\"{dstInner}\" already exists.");
            existing.Delete();
        }

        var newEntry = zip.CreateEntry(newName, CompressionLevel.Optimal);
        newEntry.LastWriteTime = srcEntry.LastWriteTime;
        if (!isDirectory)
        {
            using var src = srcEntry.Open();
            using var dst = newEntry.Open();
            src.CopyTo(dst);
        }
        srcEntry.Delete();

        session.Commit();
        Forget(_archivePath);
    }, ct);

    /// <summary>
    /// Opens the archive for writing. When the archive holds OEM-encoded names, the same code page
    /// is handed to <see cref="ZipArchive"/> so that rewriting the central directory does not
    /// mangle those names into their mis-decoded form.
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="ZipUpdateSession"/> rather than a bare <see cref="ZipArchive"/>: the
    /// archive is opened against a private temporary copy, and only <see cref="ZipUpdateSession.Dispose"/>
    /// atomically replaces the real file - see its doc comment for why. Callers keep using
    /// <c>session.Archive</c> exactly like the old return value.
    /// </remarks>
    public static ZipUpdateSession OpenForUpdate(string archivePath, IEnumerable<string>? newEntryNames = null) =>
        ZipUpdateSession.Open(archivePath, newEntryNames);

    /// <summary>
    /// Wraps a <see cref="ZipArchive"/> opened in Update/Create mode against a private temporary
    /// copy of the archive, so a crash, thrown exception, or I/O failure while flushing the
    /// central directory can never corrupt or truncate the file the user actually has.
    /// <see cref="ZipArchive"/>'s Update mode writes the modified central directory directly into
    /// whatever stream it was given - previously that was the real archive file itself, so a
    /// failure mid-write (process killed, disk full) destroyed the entire original archive, not
    /// just the new entry. <see cref="Dispose"/> flushes to the temp copy first and only then
    /// swaps it in via <see cref="File.Move(string, string, bool)"/>, mirroring the temp-file +
    /// atomic-replace pattern <see cref="RewritingArchiveWriter"/> already uses for TAR/TAR.GZ.
    /// <para>
    /// <see cref="Commit"/> must be called explicitly once the caller's own writes/deletes have
    /// all succeeded; <see cref="Dispose"/> only replaces the real file if that happened. Before
    /// this guard existed, <see cref="Dispose"/> committed unconditionally - including when it
    /// ran because an exception unwound the caller's <c>using</c> block partway through a write,
    /// which replaced the user's original archive with a truncated/partial one instead of leaving
    /// it untouched. Mirrors the explicit-commit pattern <see cref="RewritingArchiveWriter"/>
    /// already uses, and for the same reason (see its own doc comment).
    /// </para>
    /// </summary>
    public sealed class ZipUpdateSession : IDisposable
    {
        private readonly string _archivePath;
        private readonly string _tempPath;
        private FileStream? _lock;
        private bool _disposed;
        private bool _committed;

        /// <summary>The archive, opened against a private temporary copy - never the real file.</summary>
        public ZipArchive Archive { get; }

        private ZipUpdateSession(string archivePath, string tempPath, ZipArchive archive, FileStream? @lock)
        {
            _archivePath = archivePath;
            _tempPath = tempPath;
            Archive = archive;
            _lock = @lock;
        }

        /// <summary>Marks this session's changes as ready to replace the real archive on
        /// <see cref="Dispose"/>. Call only after every write/delete this session was going to make
        /// has actually succeeded - never speculatively, and never from a catch/finally that might
        /// run after a partial failure.</summary>
        public void Commit() => _committed = true;

        internal static ZipUpdateSession Open(string archivePath, IEnumerable<string>? newEntryNames)
        {
            var tempPath = TempFileNaming.NextTo(archivePath, "update");
            FileStream? @lock = null;
            try
            {
                // Exclusive lock on the real archive for this session's entire lifetime, not just
                // the final replace: without it, two concurrent sessions for the same archive
                // (e.g. a Pack operation racing a panel-level rename/delete inside the same ZIP)
                // would each copy the same starting point, mutate independently, and whichever
                // finishes last would silently discard the other's changes via its own File.Move.
                // ArchiveFileRetry.OpenExclusiveWithRetry already backs off if another process
                // (AV/indexer) is transiently scanning a just-written archive.
                if (File.Exists(archivePath))
                {
                    @lock = ArchiveFileRetry.OpenExclusiveWithRetry(archivePath);
                    CopyLockedFile(@lock, tempPath);
                }

                var stream = new FileStream(tempPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var mode = stream.Length > 0 ? ZipArchiveMode.Update : ZipArchiveMode.Create;
                ZipArchive archive;
                try
                {
                    archive = new ZipArchive(stream, mode, leaveOpen: false, PickWriteEncoding(archivePath, newEntryNames));
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
                return new ZipUpdateSession(archivePath, tempPath, archive, @lock);
            }
            catch
            {
                @lock?.Dispose();
                TryDeleteFile(tempPath);
                throw;
            }
        }

        /// <summary>If <see cref="Commit"/> was called, flushes the central directory to the temp
        /// copy and atomically replaces the original. Otherwise (abandoned session: the caller's
        /// own write/delete threw, or simply never called Commit) discards the temp copy and
        /// leaves the original completely untouched - same "no Commit means no corruption" contract
        /// <see cref="RewritingArchiveWriter.Dispose"/> already has. If the flush itself throws (or
        /// is never reached because an earlier step in the calling method threw first), the
        /// original file is likewise left untouched and the temp file is discarded.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                Archive.Dispose(); // flushes to the temp copy's stream only - never the real file

                if (_committed)
                {
                    // Release the exclusive lock only now, immediately before the replace - Windows
                    // won't let File.Move overwrite a file this same process still has open without
                    // FileShare.Delete. This leaves only the instant between releasing the lock and
                    // the move actually completing unprotected, versus the entire session beforehand.
                    _lock?.Dispose();
                    _lock = null;

                    File.Move(_tempPath, _archivePath, overwrite: true);
                }
            }
            finally
            {
                _lock?.Dispose();
                TryDeleteFile(_tempPath);
            }
        }

        private static void CopyLockedFile(FileStream source, string destPath)
        {
            source.Position = 0;
            using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
            source.CopyTo(dest);
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort cleanup */ }
        }
    }

    private static Encoding? PickWriteEncoding(string archivePath, IEnumerable<string>? newEntryNames)
    {
        if (!ReadDirectory(archivePath).HasLegacyNames)
            return null;

        if (newEntryNames != null)
        {
            foreach (var name in newEntryNames)
            {
                if (!string.Equals(Cp866.GetString(Cp866.GetBytes(name)), name, StringComparison.Ordinal))
                {
                    LogService.Warning($"Archive {archivePath}: new name '{name}' is not OEM-representable, falling back to UTF-8 names.");
                    return null;
                }
            }
        }

        return Cp866;
    }

    // DeleteAsync/DeleteBatchAsync/CreateDirectoryAsync/CopyFromStreamAsync below all run their
    // body via Task.Run for the same reason EnumerateAsync and friends do (see the comment above
    // EnumerateAsync): OpenForUpdate -> ArchiveFileRetry.OpenExclusiveWithRetry blocks with a
    // Thread.Sleep-backoff (150/300/600/1200/2400ms) if the archive is transiently locked, and
    // GetEntries() -> ReadDirectory can add its own blocking retry on top. Without Task.Run, a
    // caller on the UI thread would freeze for that whole retry window with no warning - the
    // method's Task-returning signature promises asynchrony these bodies didn't actually have.

    /// <inheritdoc/>
    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default) => Task.Run(() =>
    {
        var (_, innerPath) = CoderCommander.FileSystem.ArchivePath.SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            throw new InvalidOperationException("Cannot delete the archive root.");

        using var session = OpenForUpdate(_archivePath);
        var zip = session.Archive;

        var toDelete = new List<ZipArchiveEntry>();

        if (recursive)
        {
            var prefix = innerPath + "/";
            foreach (var cached in GetEntries())
            {
                var name = cached.FullName.Replace('\\', '/');
                if (!name.Equals(innerPath, StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (cached.Index < zip.Entries.Count)
                    toDelete.Add(zip.Entries[cached.Index]);
            }
        }
        else
        {
            // Directory.Delete(path, false) semantics: refuse a non-empty directory instead of
            // silently deleting only its marker entry and orphaning the children underneath it in
            // the archive (they used to stay in the ZIP with no listable parent - EnumerateAsync
            // would then re-synthesize the folder on the very next listing, making the delete look
            // like it had silently failed). A lone file target is unaffected: nothing else in the
            // archive can share its name as a prefix.
            var prefix = innerPath + "/";
            var hasDescendants = GetEntries().Any(cached =>
            {
                var name = cached.FullName.Replace('\\', '/');
                return !name.Equals(innerPath, StringComparison.OrdinalIgnoreCase) &&
                       !name.Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
                       name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            });
            if (hasDescendants)
                throw new IOException($"\"{innerPath}\" is not empty.");

            var entry = FindEntry(zip, innerPath);
            if (entry != null)
                toDelete.Add(entry);
        }

        foreach (var entry in toDelete)
            entry.Delete();

        InvalidateCache();
        session.Commit();
    }, ct);

    /// <summary>
    /// Batch delete: removes multiple entries in a single archive open/close cycle.
    /// More efficient than calling <see cref="DeleteAsync"/> repeatedly.
    ///
    /// Resolves every path against the cached <see cref="ZipDirectory.Index"/> (O(children) per
    /// path, not the O(n) scan-per-path this used to be - deleting 5,000 selected items from a
    /// 500k-entry ZIP used to be 2.5x10^9 string comparisons before a single byte was rewritten)
    /// and only opens <see cref="OpenForUpdate"/> - a full byte-for-byte copy of the archive -
    /// once something has actually been resolved to delete, same reasoning as
    /// <see cref="CreateDirectoryAsync"/>'s no-op check.
    /// </summary>
    public Task DeleteBatchAsync(IReadOnlyList<string> paths, bool recursive, CancellationToken ct = default) => Task.Run(() =>
    {
        if (paths.Count == 0)
            return;

        var dir = ReadDirectory(_archivePath);
        var toDelete = new HashSet<int>();

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();

            var (_, innerPath) = CoderCommander.FileSystem.ArchivePath.SplitPath(path);
            innerPath = innerPath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(innerPath))
                continue;

            var node = dir.Index.Navigate(innerPath);
            if (node == null)
                continue; // nothing at this path - nothing to delete

            // Directory.Delete(path, false) semantics - see DeleteAsync's identical guard above.
            if (!recursive && node.Children.Count > 0)
                throw new IOException($"\"{innerPath}\" is not empty.");

            CollectZipIndicesForDeletion(node, toDelete);
        }

        if (toDelete.Count == 0)
            return;

        using var session = OpenForUpdate(_archivePath);
        var zip = session.Archive;

        // Delete in reverse order to preserve indices
        foreach (var idx in toDelete.OrderByDescending(i => i))
        {
            if (idx < zip.Entries.Count)
                zip.Entries[idx].Delete();
        }

        InvalidateCache();
        session.Commit();
    }, ct);

    private static void CollectZipIndicesForDeletion(Utils.PrefixTreeIndex<ZipEntryRecord>.Node node, HashSet<int> result)
    {
        if (node.Entry is { } entry)
            result.Add(entry.Index);
        foreach (var child in node.Children.Values)
            CollectZipIndicesForDeletion(child, result);
    }

    /// <inheritdoc/>
    public Task CreateDirectoryAsync(string path, CancellationToken ct = default) => Task.Run(() =>
    {
        var (_, innerPath) = CoderCommander.FileSystem.ArchivePath.SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            return;

        // Cheap check against the cached directory BEFORE paying for OpenForUpdate - which copies
        // the entire archive byte-for-byte before anything else happens (see ZipUpdateSession.Open/
        // CopyLockedFile). The common case for this call ("ensure this folder exists") is that it
        // already does - MakeDir on an existing folder, or the panel silently ensuring a
        // destination directory before every file write - and paying for a full multi-gigabyte
        // copy just to discover a no-op was the actual cost here, not the ZipArchive Update-mode
        // work itself.
        if (ReadDirectory(_archivePath).Index.TryGetExact(innerPath, out var existing) && existing is { IsDirectory: true })
            return;

        using var session = OpenForUpdate(_archivePath, new[] { innerPath + "/" });
        var zip = session.Archive;

        // Re-checked against the live session: the cached snapshot above could be stale if another
        // process/session modified the archive between the check and OpenForUpdate's own copy.
        if (FindEntry(zip, innerPath + "/") != null)
            return;

        zip.CreateEntry(innerPath + "/");
        InvalidateCache();
        session.Commit();
    }, ct);

    /// <inheritdoc/>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default)
    {
        return Task.FromResult((0L, 0L));
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var (_, innerPath) = CoderCommander.FileSystem.ArchivePath.SplitPath(path);
        innerPath = innerPath.Replace('\\', '/');

        var tempFile = TempFileNaming.NextTo(_archivePath, "extract");
        try
        {
            using (var zip = ZipFile.OpenRead(_archivePath))
            {
                var entry = FindEntry(zip, innerPath);
                if (entry == null)
                    throw new FileNotFoundException($"Entry not found in archive: {innerPath}");
                using var s = entry.Open();
                using var fs = File.Create(tempFile);
                await s.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            return new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.DeleteOnClose);
        }
        catch
        {
            try { File.Delete(tempFile); } catch { /* best effort cleanup */ }
            throw;
        }
    }

    /// <inheritdoc/>
    public Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default) => Task.Run(async () =>
    {
        var innerPath = VfsPath.NormalizeInner(CoderCommander.FileSystem.ArchivePath.SplitPath(destinationPath).innerPath);
        if (innerPath.Length == 0)
            throw new IOException("Cannot write to the archive root without an entry name.");

        using var session = OpenForUpdate(_archivePath, new[] { innerPath });
        var zip = session.Archive;

        FindEntry(zip, innerPath)?.Delete();

        var entry = zip.CreateEntry(innerPath, CompressionLevel.Optimal);
        using var es = entry.Open();
        await source.CopyToAsync(es, 81920, ct).ConfigureAwait(false);

        InvalidateCache();
        session.Commit();
    }, ct);

    /// <inheritdoc/>
    public string GetRootPath(string path) => CoderCommander.FileSystem.ArchivePath.MakePath(_archivePath, "");
}
