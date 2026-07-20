using CoderCommander.Services;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace CoderCommander.FileSystem;

public sealed class ZipArchiveFileSystem : IFileSystem, IBatchDeletableFileSystem
{
    private readonly string _archivePath;

    public string Name => "ZIP";
    public string ArchivePath => _archivePath;

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
        public int Index { get; init; }
        public string FullName { get; init; } = "";
        public bool IsDirectory { get; init; }
        public long Size { get; init; }
        public long CompressedSize { get; init; }
        public DateTime LastWriteTimeUtc { get; init; }
    }

    /// <summary>Immutable snapshot of an archive's central directory.</summary>
    public sealed class ZipDirectory
    {
        public static readonly ZipDirectory Empty = new(Array.Empty<ZipEntryRecord>(), false);

        public IReadOnlyList<ZipEntryRecord> Entries { get; }

        /// <summary>True when at least one name is stored in an OEM code page rather than UTF-8.</summary>
        public bool HasLegacyNames { get; }

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
        var totalEntries = reader.ReadUInt16();
        reader.ReadUInt32(); // central directory size
        var cdOffset = reader.ReadUInt32();

        // Read Central Directory
        fs.Position = cdOffset;
        int entryIndex = 0;
        for (int i = 0; i < totalEntries; i++)
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
            var compressedSize = reader.ReadUInt32();
            var uncompressedSize = reader.ReadUInt32();
            var filenameLen = reader.ReadUInt16();
            var extraLen = reader.ReadUInt16();
            var commentLen = reader.ReadUInt16();
            reader.ReadUInt16(); // disk number start
            reader.ReadUInt16(); // internal attrs
            reader.ReadUInt32(); // external attrs
            reader.ReadUInt32(); // local header offset

            var filenameBytes = reader.ReadBytes(filenameLen);
            reader.ReadBytes(extraLen);
            reader.ReadBytes(commentLen);

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

    public void InvalidateCache() => Forget(_archivePath);

    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden, CancellationToken ct = default)
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

        return Task.FromResult<IReadOnlyList<FileEntry>>(result);
    }

    public Task<IReadOnlyList<FileEntry>> EnumerateDeepAsync(string path, bool includeHidden, CancellationToken ct = default)
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

        return Task.FromResult<IReadOnlyList<FileEntry>>(result);
    }

    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
        {
            var rootPath = MakePath(_archivePath, "");
            return Task.FromResult<FileEntry?>(new FileEntry(rootPath, true));
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
                return Task.FromResult<FileEntry?>(new FileEntry(
                    fullPath, isDir, true, entry.Size,
                    lastWriteTimeUtc: entry.LastWriteTimeUtc));
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
                return Task.FromResult<FileEntry?>(new FileEntry(fullPath, true));
            }
        }

        return Task.FromResult<FileEntry?>(null);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            return Task.FromResult(true);

        var entries = GetEntries();

        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/').Trim('/');
            if (string.Equals(name, innerPath, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(true);
        }

        var prefix = innerPath + "/";
        foreach (var entry in entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

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

                using var zip = OpenForUpdate(dstArchive, new[] { dstInner });
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
    public static ZipArchive OpenForUpdate(string archivePath, IEnumerable<string>? newEntryNames = null)
    {
        var stream = OpenExclusiveWithRetry(archivePath);
        var mode = stream.Length > 0 ? ZipArchiveMode.Update : ZipArchiveMode.Create;
        try
        {
            return new ZipArchive(stream, mode, leaveOpen: false, PickWriteEncoding(archivePath, newEntryNames));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens the archive file exclusively, retrying like <see cref="ReadDirectory"/> does: another
    /// panel reading the same archive (or an AV/indexer scan of a just-written one) can hold a
    /// transient lock, and failing immediately turns routine concurrent access into a hard error.
    /// </summary>
    private static FileStream OpenExclusiveWithRetry(string archivePath)
    {
        try
        {
            return File.Open(archivePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (ex.Message.Contains("being used by another process"))
        {
            LogService.Warning($"Archive locked by another process, retrying: {archivePath}");

            ReadOnlySpan<int> retryDelaysMs = [150, 300, 600];
            Exception lastError = ex;
            foreach (var delayMs in retryDelaysMs)
            {
                Thread.Sleep(delayMs);
                try
                {
                    return File.Open(archivePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (Exception ex2)
                {
                    lastError = ex2;
                }
            }

            throw new IOException($"Cannot open archive for update after {retryDelaysMs.Length} retries: {archivePath}", lastError);
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

    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            return Task.CompletedTask;

        using var zip = OpenForUpdate(_archivePath);

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

        using var zip = OpenForUpdate(_archivePath);

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
                var entry = FindEntry(zip, innerPath);
                if (entry != null)
                {
                    var idx = Array.IndexOf(zip.Entries.ToArray(), entry);
                    if (idx >= 0)
                        toDelete.Add(idx);
                }
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

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        var (_, innerPath) = SplitPath(path);
        innerPath = innerPath.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(innerPath))
            return Task.CompletedTask;

        using var zip = OpenForUpdate(_archivePath, new[] { innerPath + "/" });

        if (FindEntry(zip, innerPath + "/") != null)
            return Task.CompletedTask;

        zip.CreateEntry(innerPath + "/");
        InvalidateCache();
        return Task.CompletedTask;
    }

    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task<(long free, long total)> GetDriveSpaceAsync(string path, CancellationToken ct = default)
    {
        return Task.FromResult((0L, 0L));
    }

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

    public async Task CopyFromStreamAsync(string destinationPath, Stream source, CancellationToken ct = default)
    {
        var innerPath = VfsPath.NormalizeInner(SplitPath(destinationPath).innerPath);
        if (innerPath.Length == 0)
            throw new IOException("Cannot write to the archive root without an entry name.");

        using var zip = OpenForUpdate(_archivePath, new[] { innerPath });

        FindEntry(zip, innerPath)?.Delete();

        var entry = zip.CreateEntry(innerPath, CompressionLevel.Optimal);
        using var es = entry.Open();
        await source.CopyToAsync(es, 81920, ct).ConfigureAwait(false);

        InvalidateCache();
    }

    public string GetRootPath(string path) => MakePath(_archivePath, "");
}
