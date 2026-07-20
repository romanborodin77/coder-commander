using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CoderCommander.FileSystem;

/// <summary>
/// Интерфейс провайдера содержимого для расширяемости VFS (ph4.3 / exp.yml).
/// Content provider interface for VFS extensibility (ph4.3).
/// Позволяет плагинам и расширениям предоставлять собственные источники данных:
/// архивы, удалённые ФС, облачные хранилища и т.д.
/// Enables plugins and extensions to provide custom data sources:
/// archives, remote filesystems, cloud storage, etc.
///
/// Реализации регистрируются через <see cref="ContentProviderRegistry"/>.
/// Implementations are registered via ContentProviderRegistry.
/// </summary>
public interface IContentProvider
{
    /// <summary>
    /// Человекочитаемое имя провайдера (например, "Local FS", "ZIP Archive").
    /// Human-readable provider name (e.g. "Local FS", "ZIP Archive").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Определяет, может ли провайдер обрабатывать указанный путь.
    /// Determines whether this provider can handle the specified path.
    /// Вызывается <see cref="ContentProviderRegistry.GetProvider"/> для автоматического
    /// выбора подходящего провайдера.
    /// Called by ContentProviderRegistry.GetProvider for automatic provider selection.
    /// </summary>
    /// <param name="path">Путь к файлу или каталогу. / Path to a file or directory.</param>
    /// <returns><c>true</c>, если провайдер способен работать с этим путём.</returns>
    bool CanHandle(string path);

    /// <summary>
    /// Асинхронно открывает поток содержимого файла.
    /// Asynchronously opens a stream for file content.
    /// Используется для чтения содержимого (просмотр, поиск, редактирование).
    /// Used for content reading (viewing, search, editing).
    /// </summary>
    /// <param name="path">Путь к файлу. / File path.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Поток содержимого или <c>null</c>, если файл недоступен. / Content stream or null if inaccessible.</returns>
    Task<Stream?> OpenContentAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Асинхронно перечисляет содержимое каталога (для виртуальных ФС).
    /// Asynchronously enumerates directory contents (for virtual filesystems).
    /// Для обычных файловых систем этот метод обычно не нужен —
    /// перечисление идёт через <see cref="IFileSystem.EnumerateAsync"/>.
    /// For real filesystems this method is usually unnecessary —
    /// enumeration goes through IFileSystem.EnumerateAsync.
    /// </summary>
    /// <param name="path">Путь к каталогу. / Directory path.</param>
    /// <param name="ct">Токен отмены. / Cancellation token.</param>
    /// <returns>Список элементов содержимого. / List of content entries.</returns>
    Task<IReadOnlyList<FileEntry>> EnumerateContentAsync(string path, CancellationToken ct = default);
}
