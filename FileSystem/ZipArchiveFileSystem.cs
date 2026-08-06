using CoderCommander.Archives;
using CoderCommander.Services;
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

    /// <summary>Path to the underlying ZIP archive file on disk.</summary>
    public string ArchivePath => _archivePath;

    /// <summary>Opens a ZIP archive at <paramref name="archivePath"/> for browsing and modification.</summary>
    public ZipArchiveFileSystem(string archivePath)
    {
        _archivePath = archivePath;
    }

    [Obsolete("Use CoderCommander.FileSystem.ArchivePath.MakePath.")]
    public static string MakePath(string archivePath, string innerPath) => CoderCommander.FileSystem.ArchivePath.MakePath(archivePath, innerPath);

    [Obsolete("Use CoderCommander.FileSystem.ArchivePath.SplitPath.")]
    public static (string archivePath, string innerPath) SplitPath(string fullPath) => CoderCommander.FileSystem.ArchivePath.SplitPath(fullPath);

    [Obsolete("Use CoderCommander.FileSystem.ArchivePath.IsArchivePath.")]
    public static bool IsArchivePath(string path) => CoderCommander.FileSystem.ArchivePath.IsArchivePath(path);

    private static readonly Encoding Cp866 = Encoding.GetEncoding(866);
    private static readonly Encoding Utf8 = Encoding.UTF8;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>
    /// Packers that cannot store a character in the chosen OEM code page often fall back to a
    /// textual escape such as <c>%U0306</c>. Such sequences must be turned back into real characters.
    /// </summary>
    private static readonly Regex EscapedCodePointPattern =
        new(@"%[Uu]([0-9A-Fa-f]{4})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        /// <summary>Creates a new directory snapshot.</summary>
        public ZipDirectory(IReadOnlyList<ZipEntryRecord> entries, bool hasLegacyNames)
        {
            Entries = entries;
            HasLegacyNames = hasLegacyNames;
        }
    }

    private readonly record struct DirectoryStamp(long Length, long Ticks);

    private static readonly Dictionary<string, (DirectoryStamp Stamp, ZipDirectory Directory)> DirectoryCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads (and memoises) the central directory. The cache key carries the file length and
    /// timestamp, so any external modification of the archive invalidates it automatically.
    /// </summary>
    public static ZipDirectory ReadDirectory(string archivePath)
    {
        DirectoryStamp stamp;
        try
        {
            var info = new FileInfo(archivePath);
            if (!info.Exists) return ZipDirectory.Empty;
            stamp = new DirectoryStamp(info.Length, info.LastWriteTimeUtc.Ticks);
        }
        catch (Exception ex)
        {
            LogService.Warning($"Archive not accessible: {archivePath}: {ex.Message}");
            return ZipDirectory.Empty;
        }

        lock (DirectoryCache)
        {
            if (DirectoryCache.TryGetValue(archivePath, out var cached) && cached.Stamp == stamp)
                return cached.Directory;
        }

        ZipDirectory parsed;
        try
        {
            parsed = ParseCentralDirectory(archivePath);
        }
        catch (IOException ex) when (ex.Message.Contains("being used by another process"))
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

        lock (DirectoryCache)
        {
            DirectoryCache[archivePath] = (stamp, parsed);
        }
        return parsed;
    }

    /// <summary>Drops the memoised directory of an archive.</summary>
    public static void Forget(string archivePath)
    {
        lock (DirectoryCache)
        {
            DirectoryCache.Remove(archivePath);
        }
    }

    private IReadOnlyList<ZipEntryRecord> GetEntries() => ReadDirectory(_archivePath).Entries;

    private static ZipDirectory ParseCentralDirectory(string archivePath)
    {
        var records = new List<ZipEntryRecord>();
        var legacyNames = false;

        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);

        // Find End of Central Directory record
        var fileSize = fs.Length;
        long eocdOffset = -1;

        // Search backwards from end of file (EOCD is at least 22 bytes, comment can be up to 65535)
        var searchStart = Math.Max(0, fileSize - 22 - 65535);
        for (var pos = fileSize - 22; pos >= searchStart; pos--)
        {
            fs.Position = pos;
            var sig = reader.ReadUInt32();
            if (sig == 0x06054b50) // EOCD signature
            {
                eocdOffset = pos;
                break;
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
        int entryIndex = 0;
        for (long i = 0; i < totalEntries; i++)
        {
            var sig = reader.ReadUInt32();
            if (sig != 0x02014b50) break; // CD file header signature

            reader.ReadUInt16(); // version made by
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
            reader.ReadUInt32(); // external attrs
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
            reader.ReadBytes(commentLen);

            if (compressedSize == 0xFFFFFFFF || uncompressedSize == 0xFFFFFFFF)
                ReadZip64Sizes(extraBytes, ref uncompressedSize, ref compressedSize);

            var filename = DecodeEntryName(filenameBytes, flags, out var isLegacyName);
            legacyNames |= isLegacyName;

            var normalized = filename.Replace('\\', '/');
            // Strip "./" prefix (e.g. from Info-ZIP or similar tools)
            if (normalized.StartsWith("./"))
                normalized = normalized[2..];

            records.Add(new ZipEntryRecord
            {
                Index = entryIndex++,
                FullName = normalized,
                IsDirectory = normalized.EndsWith('/'),
                Size = uncompressedSize,
                CompressedSize = compressedSize,
                LastWriteTimeUtc = ParseDosDateTime(modDate, modTime)
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
                    uncompressedSize = (long)BitConverter.ToUInt64(extra, fieldPos);
                    fieldPos += 8;
                }
                if (needCompressed && fieldPos + 8 <= fieldEnd)
                    compressedSize = (long)BitConverter.ToUInt64(extra, fieldPos);
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

        try
        {
            StrictUtf8.GetString(data);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string ExpandEscapedCodePoints(string text)
    {
        if (text.IndexOf('%') < 0)
            return text;

        return EscapedCodePointPattern.Replace(text, m =>
            ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
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
    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');
        var prefix = string.IsNullOrEmpty(innerPath) ? "" : innerPath + "/";

        var result = new List<FileEntry>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = GetEntries();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            var isDirEntry = name.EndsWith('/');

            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = name[prefix.Length..];
            if (string.IsNullOrEmpty(rest))
                continue;

            if (isDirEntry)
            {
                var dirName = rest.TrimEnd('/');
                if (string.IsNullOrEmpty(dirName))
                    continue;
                var slashIdx = dirName.IndexOf('/');
                if (slashIdx >= 0)
                    dirName = dirName[..slashIdx];
                if (seenDirs.Add(dirName))
                {
                    var dirFullPath = MakePath(_archivePath, prefix + dirName);
                    result.Add(new FileEntry(dirFullPath, true, lastWriteTimeUtc: entry.LastWriteTimeUtc));
                }
            }
            else
            {
                var slashIdx = rest.IndexOf('/');
                if (slashIdx >= 0)
                {
                    var dirName = rest[..slashIdx];
                    if (seenDirs.Add(dirName))
                    {
                        var dirFullPath = MakePath(_archivePath, prefix + dirName);
                        result.Add(new FileEntry(dirFullPath, true, lastWriteTimeUtc: entry.LastWriteTimeUtc));
                    }
                }
                else
                {
                    var fileFullPath = MakePath(_archivePath, name);
                    result.Add(new FileEntry(
                        fileFullPath, false, true, entry.Size,
                        lastWriteTimeUtc: entry.LastWriteTimeUtc));
                }
            }
        }

        return result;
    }, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FileEntry>>(() =>
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');
        var prefix = string.IsNullOrEmpty(innerPath) ? "" : innerPath + "/";

        var result = new List<FileEntry>();
        var entries = GetEntries();

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            var isDirEntry = name.EndsWith('/');

            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rest = name[prefix.Length..];
            if (string.IsNullOrEmpty(rest))
                continue;

            var fullPath = MakePath(_archivePath, name.TrimEnd('/'));

            if (isDirEntry)
                result.Add(new FileEntry(fullPath, true, lastWriteTimeUtc: entry.LastWriteTimeUtc));
            else
                result.Add(new FileEntry(
                    fullPath, false, true, entry.Size,
                    lastWriteTimeUtc: entry.LastWriteTimeUtc));
        }

        return result;
    }, ct);

    /// <inheritdoc/>
    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default) =>
        Task.Run<FileEntry?>(() =>
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
        {
            var rootPath = MakePath(_archivePath, "");
            return new FileEntry(rootPath, true);
        }

        var entries = GetEntries();

        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            // Strip "./" prefix (e.g. from Info-ZIP or similar tools)
            if (name.StartsWith("./"))
                name = name[2..];
            name = name.Trim('/');
            if (string.Equals(name, innerPath, StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = MakePath(_archivePath, name);
                var isDir = entry.FullName.EndsWith('/');
                return new FileEntry(
                    fullPath, isDir, true, entry.Size,
                    lastWriteTimeUtc: entry.LastWriteTimeUtc);
            }
        }

        var prefix = innerPath + "/";
        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith("./"))
                name = name[2..];
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var fullPath = MakePath(_archivePath, innerPath);
                return new FileEntry(fullPath, true);
            }
        }

        return null;
    }, ct);

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default) =>
        Task.Run(() =>
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            return true;

        var entries = GetEntries();

        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/').Trim('/');
            if (string.Equals(name, innerPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var prefix = innerPath + "/";
        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }, ct);

    /// <inheritdoc/>
    public async Task CopyFileAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        var (_, srcInner) = SplitPath(source);
        srcInner = srcInner.Replace('\\', '/');

        var tempFile = Path.GetTempFileName();
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

            if (IsArchivePath(destination))
            {
                var (dstArchive, rawInner) = SplitPath(destination);
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
                Forget(dstArchive);
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
            try { File.Delete(tempFile); } catch { }
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

    /// <inheritdoc/>
    public async Task MoveAsync(string source, string destination, bool overwrite, CancellationToken ct = default)
    {
        await CopyFileAsync(source, destination, overwrite, ct).ConfigureAwait(false);
        await DeleteAsync(source, false, ct).ConfigureAwait(false);
    }

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
    /// </summary>
    public sealed class ZipUpdateSession : IDisposable
    {
        private readonly string _archivePath;
        private readonly string _tempPath;
        private FileStream? _lock;
        private bool _disposed;

        /// <summary>The archive, opened against a private temporary copy - never the real file.</summary>
        public ZipArchive Archive { get; }

        private ZipUpdateSession(string archivePath, string tempPath, ZipArchive archive, FileStream? @lock)
        {
            _archivePath = archivePath;
            _tempPath = tempPath;
            Archive = archive;
            _lock = @lock;
        }

        internal static ZipUpdateSession Open(string archivePath, IEnumerable<string>? newEntryNames)
        {
            var tempPath = archivePath + ".update-" + Guid.NewGuid().ToString("N") + ".tmp";
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

        /// <summary>Flushes the central directory to the temp copy, then atomically replaces the
        /// original. If this throws (or is never reached because an earlier step in the calling
        /// method threw first), the original file is left completely untouched and the temp file
        /// is discarded.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                Archive.Dispose();

                // Release the exclusive lock only now, immediately before the replace - Windows
                // won't let File.Move overwrite a file this same process still has open without
                // FileShare.Delete. This leaves only the instant between releasing the lock and
                // the move actually completing unprotected, versus the entire session beforehand.
                _lock?.Dispose();
                _lock = null;

                File.Move(_tempPath, _archivePath, overwrite: true);
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

    /// <inheritdoc/>
    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            return Task.CompletedTask;

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
            var entry = FindEntry(zip, innerPath);
            if (entry != null)
                toDelete.Add(entry);
        }

        foreach (var entry in toDelete)
            entry.Delete();

        InvalidateCache();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Batch delete: removes multiple entries in a single archive open/close cycle.
    /// More efficient than calling <see cref="DeleteAsync"/> repeatedly.
    /// </summary>
    public async Task DeleteBatchAsync(IReadOnlyList<string> paths, bool recursive, CancellationToken ct = default)
    {
        if (paths.Count == 0)
            return;

        using var session = OpenForUpdate(_archivePath);
        var zip = session.Archive;

        var toDelete = new HashSet<int>();

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();

            var (_, innerPath) = SplitPath(path);
            innerPath = innerPath.Replace('\\', '/').Trim('/');

            if (string.IsNullOrEmpty(innerPath))
                continue;

            if (recursive)
            {
                var prefix = innerPath + "/";
                foreach (var cached in GetEntries())
                {
                    var name = cached.FullName.Replace('\\', '/');
                    if (name.Equals(innerPath, StringComparison.OrdinalIgnoreCase) ||
                        name.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (cached.Index < zip.Entries.Count)
                            toDelete.Add(cached.Index);
                    }
                }
            }
            else
            {
                // Look up the index directly from the cached record instead of finding the
                // ZipArchiveEntry first and then Array.IndexOf-ing zip.Entries.ToArray() for it -
                // that materialized a fresh array and did an O(m) scan per path on top of this
                // already-O(n) lookup.
                var record = GetEntries().FirstOrDefault(e =>
                    string.Equals(e.FullName.Replace('\\', '/').Trim('/'), innerPath, StringComparison.OrdinalIgnoreCase));
                if (record != null && record.Index < zip.Entries.Count)
                    toDelete.Add(record.Index);
            }
        }

        // Delete in reverse order to preserve indices
        foreach (var idx in toDelete.OrderByDescending(i => i))
        {
            if (idx < zip.Entries.Count)
                zip.Entries[idx].Delete();
        }

        InvalidateCache();
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            return Task.CompletedTask;

        using var session = OpenForUpdate(_archivePath, new[] { innerPath + "/" });
        var zip = session.Archive;

        if (FindEntry(zip, innerPath + "/") != null)
            return Task.CompletedTask;

        zip.CreateEntry(innerPath + "/");
        InvalidateCache();
        return Task.CompletedTask;
    }

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
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/');

        var tempFile = Path.GetTempFileName();
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
            try { File.Delete(tempFile); } catch { }
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        var innerPath = VfsPath.NormalizeInner(SplitPath(destinationPath).innerPath);
        if (innerPath.Length == 0)
            throw new IOException("Cannot write to the archive root without an entry name.");

        using var session = OpenForUpdate(_archivePath, new[] { innerPath });
        var zip = session.Archive;

        FindEntry(zip, innerPath)?.Delete();

        var entry = zip.CreateEntry(innerPath, CompressionLevel.Optimal);
        using var es = entry.Open();
        await source.CopyToAsync(es, 81920, ct).ConfigureAwait(false);

        InvalidateCache();
    }

    /// <inheritdoc/>
    public string GetRootPath(string path) => MakePath(_archivePath, "");
}
