using CoderCommander.FileSystem;

namespace CoderCommander.Services.Search;

/// <summary>
/// Finds duplicate files by grouping them first by size (cheap), then by CRC32 hash (only within
/// same-size groups), so the expensive hash computation runs on a small subset rather than every
/// file in the tree.
///
/// <para><b>VFS-aware.</b> Operates through <see cref="IFileSystem.EnumerateDeepAsync"/> and
/// <see cref="ChecksumService"/>, so it works inside archives and remote connections, not only on
/// local paths.</para>
///
/// <para><b>Two-phase design.</b> Phase 1: enumerate all files and group by size — any size group
/// with only one file is instantly eliminated (no duplicate possible). Phase 2: for each remaining
/// group, compute CRC32 on every file and group by hash — files with the same size <i>and</i> hash
/// are true duplicates. This is the same "cheap filter first, expensive check second" principle
/// <see cref="ContentSearcher"/> uses, and the same one every duplicate-finder since <c>fdupes</c>
/// uses because there is no cheaper way to be correct.</para>
/// </summary>
public static class DuplicateFinder
{
    /// <summary>One group of duplicate files — all identical in content.</summary>
    public sealed class DuplicateGroup
    {
        /// <summary>Files in this group, all with the same size and hash.</summary>
        public IReadOnlyList<FileEntry> Files { get; init; } = [];
        /// <summary>Common file size in bytes.</summary>
        public long Size { get; init; }
        /// <summary>CRC32 hash shared by all files in this group.</summary>
        public string Hash { get; init; } = "";
    }

    /// <summary>
    /// Finds all duplicate files under <paramref name="rootPath"/> on <paramref name="fs"/>.
    /// </summary>
    /// <param name="fs">Filesystem to search — may be local, archive, or remote.</param>
    /// <param name="rootPath">Root directory to search recursively.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One <see cref="DuplicateGroup"/> per set of identical files, or an empty list if no
    /// duplicates were found.</returns>
    public static async Task<IReadOnlyList<DuplicateGroup>> FindAsync(
        IFileSystem fs, string rootPath, CancellationToken ct = default)
    {
        // Phase 1: enumerate all files, group by size.
        var allFiles = await fs.EnumerateDeepAsync(rootPath, includeHidden: false, ct).ConfigureAwait(false);
        var bySize = allFiles
            .Where(f => !f.IsDirectory && f.Size > 0)
            .GroupBy(f => f.Size)
            .Where(g => g.Count() > 1)
            .ToList();

        if (bySize.Count == 0) return [];

        // Phase 2: within each same-size group, compute CRC32 and group by hash.
        var result = new List<DuplicateGroup>();
        foreach (var sizeGroup in bySize)
        {
            ct.ThrowIfCancellationRequested();
            var byHash = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in sizeGroup)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var hash = await ChecksumService.ComputeCrc32Async(fs, file.FullPath, ct).ConfigureAwait(false);
                    if (!byHash.TryGetValue(hash, out var list))
                    {
                        list = new List<FileEntry>();
                        byHash[hash] = list;
                    }
                    list.Add(file);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    LogService.Warning($"DuplicateFinder: skipping {file.FullPath}: {ex.Message}");
                }
            }

            foreach (var (hash, files) in byHash)
            {
                if (files.Count > 1)
                {
                    result.Add(new DuplicateGroup
                    {
                        Files = files,
                        Size = sizeGroup.Key,
                        Hash = hash
                    });
                }
            }
        }

        return result;
    }
}
