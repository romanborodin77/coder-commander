using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.FileSystem;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// СТАТИЧЕСКИЙ ФАСАД (устаревший). / STATIC FACADE (OBSOLETE).
/// Сохраняет обратную совместимость с существующими панелями: все вызовы
/// перенаправляются в <see cref="LocalFileSystem"/> через <see cref="FileSystem"/>.
/// Keeps backward compatibility with existing panels: every call is forwarded
/// to LocalFileSystem via the FileSystem property.
/// ПРЕДПОЧИТАЙТЕ <see cref="IFileSystem"/> для нового кода.
/// PREFER IFileSystem for new code.
/// </summary>
public static class FileService
{
    /// <summary>
    /// Активная реализация файловой системы (точка входа для нового кода / DI-фабрик).
    /// Active file system implementation (entry point for new code / DI factories).
    /// </summary>
    public static IFileSystem FileSystem { get; set; } = LocalFileSystem.Instance;

    /// <summary>
    /// Асинхронно перечисляет содержимое директории, добавляя служебную ссылку ".."
    /// (кроме корня диска), и возвращает модели UI <see cref="FileSystemItem"/>.
    /// Asynchronously enumerates a directory, adding the ".." pseudo entry (except at
    /// a drive root), returning UI models (FileSystemItem).
    /// </summary>
    [Obsolete("Use FileSystem (IFileSystem) for new code; this facade is kept for backward compatibility")]
    public static async Task<List<FileSystemItem>> EnumerateDirectoryAsync(string path, bool showHidden, CancellationToken ct = default)
    {
        var result = new List<FileSystemItem>();
        if (!IsDriveRoot(path))
            result.Add(new FileSystemItem(Path.Combine(path, ".."), true, isParent: true));
        var entries = await FileSystem.EnumerateAsync(path, showHidden, ct).ConfigureAwait(false);
        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(e.IsDirectory
                ? new FileSystemItem(e.FullPath, true, modified: e.LastWriteTimeUtc.ToLocalTime())
                : new FileSystemItem(e.FullPath, false, e.Size, e.LastWriteTimeUtc.ToLocalTime()));
        }
        return result;
    }

    /// <summary>
    /// Синхронное перечисление директории. Устарело — используйте <see cref="EnumerateDirectoryAsync"/>.
    /// Synchronous enumeration. Obsolete — use EnumerateDirectoryAsync.
    /// </summary>
    [Obsolete("Use EnumerateDirectoryAsync to avoid deadlocks")]
    public static IEnumerable<FileSystemItem> EnumerateDirectory(string path, bool showHidden)
        => Task.Run(() => EnumerateDirectoryAsync(path, showHidden)).GetAwaiter().GetResult();

    /// <summary>
    /// Проверяет, является ли путь корнем диска (например, C:\).
    /// Checks whether the path is a drive root (e.g. C:).
    /// </summary>
    private static bool IsDriveRoot(string path)
    {
        try { return Directory.GetDirectoryRoot(path).TrimEnd('\\') == path.TrimEnd('\\'); }
        catch { return false; }
    }

    /// <summary>
    /// Открывает файл программой по умолчанию (ShellExec).
    /// Opens a file with the default program (ShellExec).
    /// </summary>
    [Obsolete("UI-only helper; kept for backward compatibility")]
    public static void OpenWithDefault(string p)
    {
        try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
        catch (Exception ex) { LogService.Error($"Failed to open {p}", nameof(FileService), ex); }
    }

    /// <summary>
    /// Определяет, является ли файл текстовым по расширению.
    /// Determines whether a file is a text file by extension.
    /// Канонический список текстовых расширений: расширять синхронно с SyntaxHighlighter.
    /// Canonical list of text extensions: extend in sync with SyntaxHighlighter.
    /// </summary>
    public static bool IsTextFile(string p)
    {
        var e = Path.GetExtension(p).ToLowerInvariant();
        var t = new HashSet<string> { ".txt",".md",".cs",".c",".cpp",".h",".java",".py",".js",".ts",".html",".css",".xml",".json",".yaml",".yml",".sh",".ps1",".bat",".sql",".ini",".log",".gitignore",".toml",".rs",".go",".rb",".php" };
        return t.Contains(e) || string.IsNullOrEmpty(Path.GetExtension(p));
    }
}
