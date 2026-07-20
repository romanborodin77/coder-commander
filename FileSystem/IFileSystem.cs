using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.FileSystem;

/// <summary>
/// Единая абстракция источника файлов (Virtual File System).
/// Single abstraction over a file source (Virtual File System).
/// Позволяет в будущем реализовать альтернативные источники (SFTP, архивы)
/// без изменения движка операций и UI.
/// Enables alternative sources (SFTP, archives) later without touching the
/// operation engine or the UI.
/// </summary>
public interface IFileSystem
{
    /// <summary>Человекочитаемое имя источника (например, "Local"). / Human-readable source name.</summary>
    string Name { get; }

    /// <summary>
    /// Перечисляет содержимое каталога (без служебной ссылки "..").
    /// Enumerates directory contents (without the ".." pseudo entry).
    /// </summary>
    /// <param name="path">Путь к каталогу. / Directory path.</param>
    /// <param name="includeHidden">Включать скрытые/системные. / Include hidden/system entries.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden = false, CancellationToken ct = default);

    /// <summary>
    /// Возвращает метаданные элемента или <c>null</c>, если он не существует.
    /// Returns entry metadata, or null if it does not exist.
    /// </summary>
    Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default);

    /// <summary>Проверяет существование пути (файла или каталога). / Checks whether a path exists.</summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct = default);

    /// <summary>Копирует файл (примитив низкого уровня, без прогресса). / Copies a file (low-level primitive, no progress).</summary>
    Task CopyAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default);

    /// <summary>Перемещает/переименовывает файл или каталог. / Moves/renames a file or directory.</summary>
    Task MoveAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default);

    /// <summary>Удаляет файл или (рекурсивно) каталог. / Deletes a file or (recursively) a directory.</summary>
    Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default);

    /// <summary>Создаёт каталог (без ошибки, если уже существует). / Creates a directory (no-op if exists).</summary>
    Task CreateDirectoryAsync(string path, CancellationToken ct = default);

    /// <summary>Устанавливает атрибуты файла. / Sets file attributes.</summary>
    Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default);

    /// <summary>Создаёт жёсткую ссылку. / Creates a hard link.</summary>
    Task CreateHardlinkAsync(string source, string linkPath, CancellationToken ct = default);

    /// <summary>Создаёт символическую ссылку. / Creates a symbolic link.</summary>
    Task CreateSymbolicLinkAsync(string target, string linkPath, bool isDirectory = false, CancellationToken ct = default);
}
