using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Services;

namespace CoderCommander.FileSystem;

/// <summary>
/// Реализация <see cref="IFileSystem"/> поверх локальной файловой системы (System.IO).
/// Local file system implementation backed by System.IO.
/// Содержит миграцию логики из устаревшего статического <see cref="CoderCommander.Services.FileService"/>.
/// Carries over logic previously living in the obsolete static FileService.
/// </summary>
public sealed class LocalFileSystem : IFileSystem
{
    /// <summary>Единственный экземпляр локальной ФС. / Singleton local FS instance.</summary>
    public static LocalFileSystem Instance { get; } = new();

    /// <inheritdoc/>
    public string Name => "Local";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden = false, CancellationToken ct = default)
    {
        var result = new List<FileEntry>();
        var opt = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        };

        IEnumerable<string> dirPaths, filePaths;
        try
        {
            dirPaths = Directory.EnumerateDirectories(path, "*", opt);
            filePaths = Directory.EnumerateFiles(path, "*", opt);
        }
        catch (UnauthorizedAccessException)
        {
            LogService.Warn($"Access denied: {path}", nameof(LocalFileSystem));
            return result;
        }
        catch (IOException ex)
        {
            LogService.Error($"IO error reading {path}", nameof(LocalFileSystem), ex);
            return result;
        }

        // Материализуем списки один раз, чтобы не повторять энумерацию.
        // Materialize lists once to avoid re-enumeration.
        var dirList = new List<string>();
        var fileList = new List<string>();
        try
        {
            foreach (var d in dirPaths) dirList.Add(d);
            foreach (var f in filePaths) fileList.Add(f);
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        int total = dirList.Count + fileList.Count;
        if (total == 0) return result;

        var entries = new FileEntry[total];
        int idx = 0;

        // Параллельно читаем метаданные директорий.
        // Read directory metadata in parallel.
        if (dirList.Count > 0)
        {
            int dirIdx = 0;
            await Parallel.ForEachAsync(dirList,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(dirList.Count, Environment.ProcessorCount), CancellationToken = ct },
                async (d, _) =>
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var i = new DirectoryInfo(d);
                        var fe = FromDirectoryInfo(i);
                        var pos = Interlocked.Increment(ref dirIdx) - 1;
                        entries[pos] = fe;
                    }
                    catch { /* пропускаем недоступные */ }
                    await ValueTask.CompletedTask;
                });
            idx = dirList.Count;
        }

        // Параллельно читаем метаданные файлов.
        // Read file metadata in parallel.
        if (fileList.Count > 0)
        {
            int fileIdx = 0;
            await Parallel.ForEachAsync(fileList,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Min(fileList.Count, Environment.ProcessorCount), CancellationToken = ct },
                async (f, _) =>
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var i = new FileInfo(f);
                        var fe = FromFileInfo(i);
                        var pos = idx + Interlocked.Increment(ref fileIdx) - 1;
                        entries[pos] = fe;
                    }
                    catch { /* пропускаем недоступные */ }
                    await ValueTask.CompletedTask;
                });
        }

        // Фильтруем null (недоступные) и возвращаем.
        // Filter out nulls (inaccessible) and return.
        result.Capacity = total;
        foreach (var e in entries)
            if (e.FullPath is not null) result.Add(e);

        return result;
    }

    /// <inheritdoc/>
    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (Directory.Exists(path)) return Task.FromResult<FileEntry?>(FromDirectoryInfo(new DirectoryInfo(path)));
            if (File.Exists(path)) return Task.FromResult<FileEntry?>(FromFileInfo(new FileInfo(path)));
            return Task.FromResult<FileEntry?>(null);
        }
        catch (Exception ex)
        {
            LogService.Error($"Error reading info for {path}", nameof(LocalFileSystem), ex);
            return Task.FromResult<FileEntry?>(null);
        }
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path) || Directory.Exists(path));
    }

    /// <inheritdoc/>
    public Task CopyAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(destination);
            var errors = new List<string>();
            foreach (var f in Directory.EnumerateFiles(source))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    File.Copy(f, Path.Combine(destination, Path.GetFileName(f)!), overwrite);
                }
                catch (Exception ex)
                {
                    var msg = $"Failed to copy file: {f} → {destination}: {ex.Message}";
                    LogService.Error(msg, nameof(LocalFileSystem), ex);
                    errors.Add(msg);
                }
            }
            foreach (var d in Directory.EnumerateDirectories(source))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    CopyAsync(d, Path.Combine(destination, Path.GetFileName(d)!), overwrite, ct).Wait();
                }
                catch (Exception ex)
                {
                    var msg = $"Failed to copy subdirectory: {d} → {destination}: {ex.Message}";
                    LogService.Error(msg, nameof(LocalFileSystem), ex);
                    errors.Add(msg);
                }
            }
            if (errors.Count > 0)
                throw new IOException($"Copy completed with {errors.Count} error(s):\n{string.Join("\n", errors)}");
            return Task.CompletedTask;
        }
        
        // Если файл назначения существует и overwrite=true, снимаем атрибут Read-only.
        // If destination file exists and overwrite=true, remove Read-only attribute.
        if (overwrite && File.Exists(destination))
        {
            try
            {
                var fi = new FileInfo(destination);
                if (fi.IsReadOnly) fi.IsReadOnly = false;
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to remove Read-only attribute from {destination}: {ex.Message}", nameof(LocalFileSystem));
            }
        }
        
        File.Copy(source, destination, overwrite);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MoveAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(source))
        {
            if (Directory.Exists(destination))
            {
                if (!overwrite) throw new IOException($"Destination already exists: {destination}");
                Directory.Delete(destination, true);
            }
            try
            {
                Directory.Move(source, destination);
            }
            catch (DirectoryNotFoundException ex)
            {
                LogService.Error($"Directory not found during move: {source} → {destination}", nameof(LocalFileSystem), ex);
                throw;
            }
            catch (IOException ex)
            {
                LogService.Error($"IO error moving directory: {source} → {destination}", nameof(LocalFileSystem), ex);
                throw;
            }
            return Task.CompletedTask;
        }
        if (File.Exists(destination))
        {
            if (!overwrite) throw new IOException($"Destination already exists: {destination}");
            // Снимаем атрибут Read-only, если файл защищён от записи.
            // Remove Read-only attribute if file is write-protected.
            try
            {
                var fi = new FileInfo(destination);
                if (fi.IsReadOnly) fi.IsReadOnly = false;
            }
            catch (Exception ex)
            {
                LogService.Warn($"Failed to remove Read-only attribute from {destination}: {ex.Message}", nameof(LocalFileSystem));
            }
            File.Delete(destination);
        }
        File.Move(source, destination, overwrite);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Directory.Exists(path)) { Directory.Delete(path, recursive); return Task.CompletedTask; }
        if (File.Exists(path)) { File.Delete(path); return Task.CompletedTask; }
        throw new FileNotFoundException($"Path not found: {path}", path);
    }

    /// <inheritdoc/>
    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(path);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        File.SetAttributes(path, attributes);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CreateHardlinkAsync(string source, string linkPath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // File.CreateHardLink недоступен в используемом ref-паке; используем kernel32 напрямую
        // (целевая платформа — Windows). / Use kernel32 directly on Windows.
        if (!NativeMethods.CreateHardLink(linkPath, source, IntPtr.Zero))
            throw new IOException($"Failed to create hard link: {linkPath} -> {source}");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CreateSymbolicLinkAsync(string target, string linkPath, bool isDirectory = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (isDirectory) Directory.CreateSymbolicLink(linkPath, target);
        else File.CreateSymbolicLink(linkPath, target);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Возвращает корень тома для заданного пути (для оптимизации Move через Rename).
    /// Returns the volume root of a path (used to optimize Move via Rename).
    /// </summary>
    public string GetVolumeRoot(string path)
    {
        try { return Directory.GetDirectoryRoot(path); }
        catch { return Path.GetPathRoot(path) ?? path; }
    }

    /// <summary>
    /// Копирует NTFS ACL (права доступа) с исходного элемента на целевой.
    /// Copies NTFS ACL (permissions) from source to destination.
    /// </summary>
    public static void CopyAcl(string source, string destination)
    {
        try
        {
            if (Directory.Exists(source) && Directory.Exists(destination))
            {
                var srcDir = new DirectoryInfo(source);
                var dstDir = new DirectoryInfo(destination);
                var srcSecurity = srcDir.GetAccessControl();
                dstDir.SetAccessControl(srcSecurity);
            }
            else if (File.Exists(source) && File.Exists(destination))
            {
                var srcFile = new FileInfo(source);
                var dstFile = new FileInfo(destination);
                var srcSecurity = srcFile.GetAccessControl();
                dstFile.SetAccessControl(srcSecurity);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Failed to copy ACL {source} → {destination}: {ex.Message}", nameof(LocalFileSystem));
        }
    }

    /// <summary>
    /// Генерирует уникальное имя файла, добавляя " (1)", " (2)" и т.д.
    /// Generates a unique file name by appending " (1)", " (2)", etc.
    /// </summary>
    public static string UniqueName(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var i = 1;
        string cand;
        do { cand = Path.Combine(dir, $"{name} ({i++}){ext}"); } while (File.Exists(cand) || Directory.Exists(cand));
        return cand;
    }

    private static FileEntry FromFileInfo(FileInfo i) => new(
        i.FullName, false, i.Exists, i.Exists ? i.Length : 0,
        i.Exists ? i.Attributes : default,
        i.Exists ? i.CreationTimeUtc : default,
        i.Exists ? i.LastWriteTimeUtc : default,
        i.Exists ? i.LastAccessTimeUtc : default);

    private static FileEntry FromDirectoryInfo(DirectoryInfo i) => new(
        i.FullName, true, i.Exists, 0,
        i.Exists ? i.Attributes : default,
        i.Exists ? i.CreationTimeUtc : default,
        i.Exists ? i.LastWriteTimeUtc : default,
        i.Exists ? i.LastAccessTimeUtc : default);

    /// <summary>
    /// P/Invoke к kernel32 для создания жёстких ссылок (недоступно в ref-паке целевого SDK).
    /// P/Invoke to kernel32 for hard-link creation (unavailable in the target SDK ref pack).
    /// </summary>
    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }
}
