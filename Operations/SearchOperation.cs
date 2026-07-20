using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.FileSystem;
using CoderCommander.Models;

namespace CoderCommander.Operations;

/// <summary>
/// Критерии поиска файлов (ph2.1 / exp.yml): маски имён, regex-содержимое,
/// фильтры размера/даты/атрибутов, кодировка для файлов без BOM.
/// Search criteria (ph2.1): name masks, content regex, size/date/attribute
/// filters and the encoding used when no BOM is present.
/// </summary>
public sealed class SearchCriteria
{
    /// <summary>Корневая папка. / Root folder.</summary>
    public string RootPath { get; init; } = ".";

    /// <summary>Маски имён через «;» (поддержка * и ?). / Name masks separated by ';' (* and ? supported).</summary>
    public string NameMasks { get; init; } = "*.*";

    /// <summary>Интерпретировать маски имён как единое регулярное выражение. / Treat name masks as a single regex.</summary>
    public bool NameRegexMode { get; init; }

    /// <summary>Искомое содержимое (регулярное выражение). null/empty — поиск только по имени. / Content regex; null/empty = name-only.</summary>
    public string? ContentPattern { get; init; }

    /// <summary>Учитывать регистр. / Case-sensitive matching.</summary>
    public bool MatchCase { get; init; }

    /// <summary>Включать вложенные папки. / Recurse into subfolders.</summary>
    public bool Recurse { get; init; } = true;

    /// <summary>Минимальный размер (байты). / Minimum size (bytes).</summary>
    public long? MinSize { get; init; }

    /// <summary>Максимальный размер (байты). / Maximum size (bytes).</summary>
    public long? MaxSize { get; init; }

    /// <summary>Минимальная дата изменения (local). / Minimum last-write time (local).</summary>
    public DateTime? DateFrom { get; init; }

    /// <summary>Максимальная дата изменения (local). / Maximum last-write time (local).</summary>
    public DateTime? DateTo { get; init; }

    /// <summary>Требуемые атрибуты (все должны быть установлены). / Required attributes (all must be set).</summary>
    public FileAttributes RequiredAttributes { get; init; }

    /// <summary>Исключаемые атрибуты (ни один не должен быть установлен). / Excluded attributes (none may be set).</summary>
    public FileAttributes ExcludedAttributes { get; init; }

    /// <summary>Кодировка для файлов без BOM (null — UTF-8 по умолчанию). / Fallback encoding for BOM-less files (null = UTF-8).</summary>
    public Encoding? FallbackEncoding { get; init; }

    /// <summary>
    /// Искать содержимое внутри архивов (ZIP, 7Z, RAR, TAR, GZ и т.д.) (ph4.1).
    /// Search content inside archives (ZIP, 7Z, RAR, TAR, GZ, etc.) (ph4.1).
    /// Архивы обрабатываются через SharpCompress (streaming, без распаковки на диск).
    /// Archives are processed via SharpCompress (streaming, no extraction to disk).
    /// </summary>
    public bool SearchInArchives { get; init; }
}

/// <summary>
/// Прогресс поиска: число проверенных/найденных файлов и признак выполнения.
/// Search progress: scanned/found counts and a running flag.
/// </summary>
public sealed class SearchProgress
{
    public int Scanned { get; init; }
    public int Found { get; init; }
    public bool IsRunning { get; init; }
}

/// <summary>
/// Операция поиска по файловой системе (ph2.1 / exp.yml).
/// File-system search operation (ph2.1).
/// Перечисление — <see cref="DirectoryInfo.EnumerateFiles"/> + <see cref="EnumerationOptions"/>
/// (RecurseSubdirectories, IgnoreInaccessible); обработка файлов — параллельно через
/// <see cref="Parallel.ForEachAsync"/> (MaxDegreeOfParallelism = ProcessorCount).
/// Enumeration via DirectoryInfo.EnumerateFiles + EnumerationOptions; files are processed
/// in parallel with Parallel.ForEachAsync (MaxDegreeOfParallelism = ProcessorCount).
/// Поиск содержимого: regex (<see cref="RegexOptions.Compiled"/> | CultureInvariant; для
/// недоверенных паттернов — NonBacktracking), BOM-детект, кодировки CodePages, крупные файлы —
/// через <see cref="MemoryMappedFile"/> со скользящим буфером (без полной загрузки в память).
/// Content search uses regex (Compiled | CultureInvariant; NonBacktracking for untrusted
/// patterns), BOM detection, CodePages encodings, and MemoryMappedFile + sliding buffer for
/// large files (no full in-memory load).
/// </summary>
public sealed class SearchOperation
{
    // Скользящий буфер: перекрытие между окнами, чтобы совпадения не терялись на границе.
    // Sliding buffer: overlap between windows so matches are not lost at chunk boundaries.
    private static readonly int OverlapChars = 1 << 16;     // 64K символов / 64K chars
    private static readonly long MmapThreshold = 16L * 1024 * 1024; // 16 МБ / 16 MB

    private readonly SearchCriteria _criteria;
    private readonly IProgress<SearchProgress>? _progress;
    private readonly IProgress<IReadOnlyList<SearchResult>>? _resultSink;

    private readonly Regex? _nameRegex;        // задан, если NameRegexMode
    private readonly List<Regex> _nameWildcards = new();
    private readonly Regex? _contentRegex;

    private int _scanned;
    private int _found;
    private int _reportTick;

    public SearchOperation(SearchCriteria criteria,
        IProgress<SearchProgress>? progress = null,
        IProgress<IReadOnlyList<SearchResult>>? resultSink = null)
    {
        // Legacy-кодировки (CodePages) доступны глобально.
        // Register legacy (CodePages) encodings globally.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        _criteria = criteria;
        _progress = progress;
        _resultSink = resultSink;

        var caseOpt = criteria.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;

        // ── Имя файла / File name ──
        if (criteria.NameRegexMode)
        {
            _nameRegex = new Regex(criteria.NameMasks,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | caseOpt);
        }
        else
        {
            foreach (var raw in criteria.NameMasks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (raw.Length == 0) continue;
                var pattern = "^" + Regex.Escape(raw).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                _nameWildcards.Add(new Regex(pattern,
                    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | caseOpt));
            }
        }

        // ── Содержимое / Content ──
        if (!string.IsNullOrEmpty(criteria.ContentPattern))
        {
            // Пользовательский паттерн — недоверенный: NonBacktracking против ReDoS.
            // User-supplied pattern is untrusted: NonBacktracking guards against ReDoS.
            _contentRegex = new Regex(criteria.ContentPattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking | caseOpt);
        }
    }

    /// <summary>
    /// Выполняет поиск. Результаты поступают через <see cref="_resultSink"/> (уже в поток UI,
    /// если <see cref="Progress{T}"/> создан на нём), прогресс — через <see cref="_progress"/>.
    /// Runs the search. Results arrive via _resultSink (already on the UI thread if Progress{T}
    /// was created there); progress via _progress.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        ReportProgress(true);
        var di = new DirectoryInfo(_criteria.RootPath);
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = _criteria.Recurse,
            IgnoreInaccessible = true,
            BufferSize = 8192,
            AttributesToSkip = 0,                 // фильтр атрибутов — в коде / attribute filter is in code
            ReturnSpecialDirectories = false,
        };

        var files = di.EnumerateFiles("*", opts);
        var parallelOpts = new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
        };

        await Parallel.ForEachAsync(files, parallelOpts,
            (fi, cti) => new ValueTask(ProcessFileAsync(fi, cti))).ConfigureAwait(false);

        ReportProgress(false);
    }

    private async Task ProcessFileAsync(FileInfo fi, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!PassesName(fi.Name)) return;
            if (!PassesSize(fi.Length)) return;
            if (!PassesDate(fi.LastWriteTime)) return;
            if (!PassesAttributes(fi.Attributes)) return;

            Interlocked.Increment(ref _scanned);
            MaybeReport();

            // ph4.1: Если файл — архив и включён поиск в архивах, ищем внутри.
            // ph4.1: If the file is an archive and archive search is enabled, search inside.
            if (_criteria.SearchInArchives && ArchiveHelper.IsSearchableArchive(fi))
            {
                await SearchInsideArchiveAsync(fi, ct).ConfigureAwait(false);
                MaybeReport();
                return;
            }

            if (_contentRegex is null)
            {
                _resultSink?.Report(new[] { new SearchResult(fi.FullName, fi.Length) });
                Interlocked.Increment(ref _found);
                MaybeReport();
                return;
            }

            var matches = new List<SearchResult>();
            ScanContent(fi, _contentRegex, ct, matches);
            if (matches.Count > 0)
            {
                _resultSink?.Report(matches);
                Interlocked.Add(ref _found, matches.Count);
            }
            MaybeReport();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { /* недоступный/битый файл — пропускаем / unreadable file: skip */ }
    }

    // ── ph4.1: Поиск по содержимому архива / Archive content search ──

    /// <summary>
    /// Ищет совпадения внутри архива через SharpCompress streaming (ph4.1).
    /// Searches for matches inside an archive via SharpCompress streaming (ph4.1).
    /// Каждая текстовая запись проверяется по имени (маска) и содержимому (regex).
    /// Each text entry is checked by name (mask) and content (regex).
    /// </summary>
    private async Task SearchInsideArchiveAsync(FileInfo archiveFi, CancellationToken ct)
    {
        await foreach (var entry in ArchiveHelper.EnumerateEntriesAsync(archiveFi.FullName, ct))
        {
            ct.ThrowIfCancellationRequested();

            // Фильтр по имени записи: извлекаем имя из ключа архива.
            // Entry name filter: extract name from archive key.
            var entryName = Path.GetFileName(entry.Key);
            if (string.IsNullOrEmpty(entryName)) continue;
            if (!PassesName(entryName)) continue;

            // Если нет regex-поиска по содержимому — просто отдаём запись.
            // If no content regex search — just report the entry.
            if (_contentRegex is null)
            {
                _resultSink?.Report(new[]
                {
                    new SearchResult(entry.DisplayPath, entry.Size)
                });
                Interlocked.Increment(ref _found);
                MaybeReport();
                continue;
            }

            // Пропускаем слишком крупные записи (>256 МБ) и нулевые.
            // Skip oversized entries (>256 MB) and zero-size.
            if (entry.Size <= 0 || entry.Size > ArchiveHelper.MaxEntrySizeForSearch) continue;

            // Проверяем, текстовый ли файл по расширению.
            // Check whether the file is text by extension.
            if (!IsTextLikeFile(entry.Key)) continue;

            // Streaming-чтение содержимого записи и поиск regex.
            // Streaming read of entry content and regex search.
            using var stream = await ArchiveHelper.OpenEntryStreamAsync(
                archiveFi.FullName, entry.Key, ct).ConfigureAwait(false);
            if (stream is null) continue;

            var matches = ScanContentFromStream(stream, _contentRegex, entry.DisplayPath,
                entry.Size, entry.ArchivePath, ct);
            if (matches.Count > 0)
            {
                _resultSink?.Report(matches);
                Interlocked.Add(ref _found, matches.Count);
            }
            MaybeReport();
        }
    }

    /// <summary>
    /// Определяет, вероятно ли текстовый файл по расширению entry в архиве (ph4.1).
    /// Heuristically determines whether an archive entry is a text file by extension (ph4.1).
    /// </summary>
    private static bool IsTextLikeFile(string entryKey)
    {
        var ext = Path.GetExtension(entryKey).ToLowerInvariant();
        // Текстовые расширения + неизвестные (без расширения) — пробуем.
        // Text extensions + unknown (no extension) — try.
        var textExts = new HashSet<string>
        {
            ".txt",".md",".cs",".c",".cpp",".h",".hpp",".java",".py",".js",".ts",
            ".html",".css",".xml",".json",".yaml",".yml",".toml",
            ".sh",".ps1",".bat",".cmd",".sql",".ini",".log",".cfg",".conf",
            ".rs",".go",".rb",".php",".swift",".kt",".scala",".r",".lua",
            ".csv",".tsv",".gradle",".cmake",".makefile",
            ".gitignore",".gitattributes",".editorconfig",".dockerfile",
            ".xaml",".resx",".csproj",".props",".targets",".sln",
            ".xshd",".lng",".json5",".jsonc",".env",
        };
        if (string.IsNullOrEmpty(ext)) return true; // без расширения — пробуем / no ext — try
        return textExts.Contains(ext);
    }

    /// <summary>
    /// Сканирует поток содержимого на совпадения regex (ph4.1).
    /// Scans a content stream for regex matches (ph4.1).
    /// Работает по чанкам ~1 МБ с перекрытием (overlap) для корректности на границах.
    /// Works in ~1 MB chunks with overlap for correctness at chunk boundaries.
    /// </summary>
    private List<SearchResult> ScanContentFromStream(
        Stream stream, Regex regex, string displayPath, long size,
        string archivePath, CancellationToken ct)
    {
        var matches = new List<SearchResult>();
        var enc = _criteria.FallbackEncoding ?? Encoding.UTF8;
        int lastLine = -1;

        const int chunk = 1 << 20; // 1 МБ / 1 MB
        var buf = new byte[chunk];
        var charBuf = new char[chunk + (chunk >> 2)];

        long absCharPos = 0;
        int newlinesTotal = 0;
        string overlap = "";
int bytesRead;
var decoder = enc.GetDecoder();
        int newlinesBeforeWindow = 0;
        while ((bytesRead = stream.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                int charsDecoded = decoder.GetChars(buf, 0, bytesRead, charBuf, 0, false);
            var chunkText = new string(charBuf, 0, charsDecoded);
            if (enc.IsSingleByte && chunkText.IndexOf('\0') >= 0) return matches; // бинарный / binary

            string window = overlap + chunkText;
            int prefixLen = overlap.Length;

            foreach (Match m in regex.Matches(window))
            {
                if (m.Index + m.Length <= prefixLen) continue;
                int lineNo = 1 + newlinesBeforeWindow + CountNewlines(window.AsSpan(0, m.Index));
                if (lineNo == lastLine) continue;
                lastLine = lineNo;
                var line = ExtractLine(window, m.Index);
                // Путь результата: "archive.zip!/entry/path" с номером строки.
                // Result path: "archive.zip!/entry/path" with line number.
                var fullDisplayPath = displayPath;
                matches.Add(new SearchResult(fullDisplayPath, size, line, lineNo, enc.WebName));
            }

            newlinesTotal += CountNewlines(chunkText.AsSpan());
            absCharPos += chunkText.Length;

            int ol = Math.Min(OverlapChars, chunkText.Length);
            overlap = ol == 0 ? "" : chunkText.Substring(chunkText.Length - ol);
            newlinesBeforeWindow = newlinesTotal - CountNewlines(overlap.AsSpan());
        }

        return matches;
    }

    // ── Фильтры / Filters ──
    private bool PassesName(string name)
    {
        if (_nameRegex is not null) return _nameRegex.IsMatch(name);
        if (_nameWildcards.Count == 0) return true;
        foreach (var rx in _nameWildcards) if (rx.IsMatch(name)) return true;
        return false;
    }

    private bool PassesSize(long size)
    {
        if (_criteria.MinSize is { } min && size < min) return false;
        if (_criteria.MaxSize is { } max && size > max) return false;
        return true;
    }

    private bool PassesDate(DateTime lastWrite)
    {
        if (_criteria.DateFrom is { } from && lastWrite < from) return false;
        if (_criteria.DateTo is { } to && lastWrite > to) return false;
        return true;
    }

    private bool PassesAttributes(FileAttributes attrs)
    {
        if (_criteria.RequiredAttributes != 0 && (attrs & _criteria.RequiredAttributes) != _criteria.RequiredAttributes) return false;
        if (_criteria.ExcludedAttributes != 0 && (attrs & _criteria.ExcludedAttributes) != 0) return false;
        return true;
    }

    // ── Поиск содержимого / Content scan ──
    private void ScanContent(FileInfo fi, Regex regex, CancellationToken ct, List<SearchResult> outMatches)
    {
        ct.ThrowIfCancellationRequested();

        var enc = DetectEncoding(fi.FullName, out int preamble) ?? _criteria.FallbackEncoding ?? Encoding.UTF8;
        long size = fi.Length;
        int lastLine = -1;

        if (size <= MmapThreshold)
        {
            // Небольшой файл: пробуем ContentProvider, затем прямое чтение.
            // Small file: try ContentProvider first, then direct read.
            var provider = ContentProviderRegistry.Instance.GetProvider(fi.FullName);
            if (provider is not null)
            {
                using var providerStream = provider.OpenContentAsync(fi.FullName, ct).GetAwaiter().GetResult();
                if (providerStream is not null)
                {
                    byte[] bytes;
                    try { bytes = ReadAllBytesFromStream(providerStream, (int)size); }
                    catch { return; }

                    if (enc.IsSingleByte && ContainsNul(bytes, preamble)) return;
                    var text = enc.GetString(bytes, preamble, bytes.Length - preamble);
                    CollectMatches(regex, text, fi, enc, outMatches, ref lastLine);
                    return;
                }
            }

            // Фоллбэк: прямое чтение с диска / Fallback: direct disk read.
            byte[] fileBytes;
            try { fileBytes = File.ReadAllBytes(fi.FullName); }
            catch { return; }

            if (enc.IsSingleByte && ContainsNul(fileBytes, preamble)) return; // бинарный / binary
            var fileText = enc.GetString(fileBytes, preamble, fileBytes.Length - preamble);
            CollectMatches(regex, fileText, fi, enc, outMatches, ref lastLine);
            return;
        }

        // Крупный файл: MemoryMappedFile + скользящий буфер. / Large file: mmap + sliding buffer.
        using var mmf = MemoryMappedFile.CreateFromFile(
            fi.FullName, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        using var stream = mmf.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
        if (preamble > 0) stream.Position = preamble;

        var decoder = enc.GetDecoder();
        const int chunk = 1 << 20; // 1 МБ / 1 MB
        var buf = new byte[chunk];
        var charBuf = new char[chunk + (chunk >> 2)];

        long absCharPos = 0;
        int newlinesTotal = 0;
        string overlap = "";
        int newlinesBeforeWindow = 0;

        int bytesRead;
        while ((bytesRead = stream.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            int charsDecoded = decoder.GetChars(buf, 0, bytesRead, charBuf, 0, false);
            var chunkText = new string(charBuf, 0, charsDecoded);
            if (enc.IsSingleByte && chunkText.IndexOf('\0') >= 0) return; // бинарный / binary

            string window = overlap + chunkText;
            int prefixLen = overlap.Length;
            CollectMatchesInWindow(regex, window, prefixLen,
                newlinesBeforeWindow, fi, enc, outMatches, ref lastLine);

            newlinesTotal += CountNewlines(chunkText.AsSpan());
            absCharPos += chunkText.Length;

            int ol = Math.Min(OverlapChars, chunkText.Length);
            overlap = ol == 0 ? "" : chunkText.Substring(chunkText.Length - ol);
            newlinesBeforeWindow = newlinesTotal - CountNewlines(overlap.AsSpan());
        }
    }

    /// <summary>
    /// Читает все байты из потока (для интеграции с ContentProvider).
    /// Reads all bytes from a stream (for ContentProvider integration).
    /// </summary>
    private static byte[] ReadAllBytesFromStream(Stream stream, int estimatedSize)
    {
        if (estimatedSize > 0 && estimatedSize <= 16 * 1024 * 1024)
        {
            var buffer = new byte[estimatedSize];
            int offset = 0;
            int read;
            while (offset < buffer.Length &&
                   (read = stream.Read(buffer, offset, buffer.Length - offset)) > 0)
            {
                offset += read;
            }
            return offset == buffer.Length ? buffer : buffer.AsSpan(0, offset).ToArray();
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms, bufferSize: 81920);
        return ms.ToArray();
    }

    private void CollectMatches(Regex regex, string text,
        FileInfo fi, Encoding enc, List<SearchResult> outMatches, ref int lastLine)
    {
        foreach (Match m in regex.Matches(text))
        {
            int lineNo = 1 + CountNewlines(text.AsSpan(0, m.Index));
            if (lineNo == lastLine) continue; // одна запись на строку / one entry per line
            lastLine = lineNo;
            outMatches.Add(new SearchResult(fi.FullName, fi.Length, ExtractLine(text, m.Index), lineNo, enc.WebName));
        }
    }

    private void CollectMatchesInWindow(Regex regex, string window, int prefixLen,
        int newlinesBeforeWindow, FileInfo fi, Encoding enc, List<SearchResult> outMatches, ref int lastLine)
    {
        foreach (Match m in regex.Matches(window))
        {
            if (m.Index + m.Length <= prefixLen) continue; // уже учтено в предыдущем окне / already reported
            int lineNo = 1 + newlinesBeforeWindow + CountNewlines(window.AsSpan(0, m.Index));
            if (lineNo == lastLine) continue;
            lastLine = lineNo;
            outMatches.Add(new SearchResult(fi.FullName, fi.Length, ExtractLine(window, m.Index), lineNo, enc.WebName));
        }
    }

    // ── Утилиты / Helpers ──
    private static string ExtractLine(string text, int idx)
    {
        int start = text.LastIndexOf('\n', idx);
        start = start < 0 ? 0 : start + 1;
        int end = text.IndexOf('\n', idx);
        if (end < 0) end = text.Length;
        var line = text.Substring(start, end - start);
        if (line.EndsWith("\r")) line = line.Substring(0, line.Length - 1);
        const int max = 400;
        if (line.Length > max) line = line.Substring(0, max) + "…";
        return line;
    }

    private static int CountNewlines(ReadOnlySpan<char> s)
    {
        int c = 0;
        foreach (var ch in s) if (ch == '\n') c++;
        return c;
    }

    private static bool ContainsNul(byte[] bytes, int skip)
    {
        for (int i = skip; i < bytes.Length; i++) if (bytes[i] == 0) return true;
        return false;
    }

    /// <summary>
    /// Определяет кодировку по BOM. Возвращает <c>null</c>, если BOM нет (используется Fallback/UTF-8).
    /// Detects encoding from the BOM. Returns null when there is no BOM (Fallback/UTF-8 is used).
    /// </summary>
    private static Encoding? DetectEncoding(string path, out int preamble)
    {
        preamble = 0;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        var bom = new byte[4];
        int n = fs.Read(bom, 0, 4);
        if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) { preamble = 3; return new UTF8Encoding(false); }
        if (n >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
        {
            if (n >= 4 && bom[2] == 0x00 && bom[3] == 0x00) { preamble = 4; return new UTF32Encoding(false, true); }
            preamble = 2; return new UnicodeEncoding(false, true); // UTF-16 LE
        }
        if (n >= 2 && bom[0] == 0xFE && bom[1] == 0xFF) { preamble = 2; return new UnicodeEncoding(true, true); } // UTF-16 BE
        if (n >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF) { preamble = 4; return new UTF32Encoding(true, true); } // UTF-32 BE
        return null;
    }

    private void MaybeReport()
    {
        if (Interlocked.Increment(ref _reportTick) % 64 == 0) ReportProgress(true);
    }

    private void ReportProgress(bool running)
    {
        _progress?.Report(new SearchProgress
        {
            Scanned = _scanned,
            Found = _found,
            IsRunning = running,
        });
    }
}
