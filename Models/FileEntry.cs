using System.IO;

namespace CoderCommander.FileSystem;

/// <summary>
/// Независимое от источника описание элемента файловой системы.
/// Source-agnostic description of a file system entry.
/// Заменяет связанный с UI <see cref="CoderCommander.Models.FileSystemItem"/> там,
/// где нужен чистый доступ к метаданным без WPF/MVVM-зависимостей.
/// Replaces the UI-bound FileSystemItem where pure metadata access is needed.
/// </summary>
public sealed class FileEntry
{
    /// <summary>Полный путь к элементу. / Full path to the entry.</summary>
    public string FullPath { get; }

    /// <summary>Имя файла или каталога без пути. / File or directory name without path.</summary>
    public string Name { get; }

    /// <summary>Является ли элемент каталогом. / Whether the entry is a directory.</summary>
    public bool IsDirectory { get; }

    /// <summary>Существует ли элемент (актуально после вызова Exists/GetFileInfo). / Whether the entry exists.</summary>
    public bool Exists { get; }

    /// <summary>Размер файла в байтах (0 для каталогов). / File size in bytes (0 for directories).</summary>
    public long Size { get; }

    /// <summary>Атрибуты файла. / File attributes.</summary>
    public FileAttributes Attributes { get; }

    /// <summary>Время создания (UTC). / Creation time (UTC).</summary>
    public DateTime CreatedTimeUtc { get; }

    /// <summary>Время последней записи (UTC). / Last write time (UTC).</summary>
    public DateTime LastWriteTimeUtc { get; }

    /// <summary>Время последнего доступа (UTC). / Last access time (UTC).</summary>
    public DateTime LastAccessTimeUtc { get; }

    /// <summary>Время последней записи в локальном часовом поясе. / Last write time in local time zone.</summary>
    public DateTime LastWriteTime => LastWriteTimeUtc.ToLocalTime();

    /// <summary>
    /// Инициализирует описание элемента.
    /// Initializes a file entry description.
    /// </summary>
    public FileEntry(
        string fullPath,
        bool isDirectory,
        bool exists = true,
        long size = 0,
        FileAttributes attributes = default,
        DateTime createdTimeUtc = default,
        DateTime lastWriteTimeUtc = default,
        DateTime lastAccessTimeUtc = default)
    {
        FullPath = fullPath;
        Name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        IsDirectory = isDirectory;
        Exists = exists;
        Size = isDirectory ? 0 : size;
        Attributes = attributes;
        CreatedTimeUtc = createdTimeUtc;
        LastWriteTimeUtc = lastWriteTimeUtc;
        LastAccessTimeUtc = lastAccessTimeUtc;
    }

    /// <summary>Возвращает имя для отображения. / Returns the display name.</summary>
    public override string ToString() => Name;
}
