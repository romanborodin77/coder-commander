using System.Collections.Generic;
using CoderCommander.Models;

namespace CoderCommander.FileSystem;

/// <summary>
/// Виртуальный источник результатов поиска (ph2.2 / exp.yml).
/// Virtual search-result source (ph2.2).
/// Реализует контракт <see cref="IFileSystem"/>, поэтому подаётся в файловую
/// панель как обычный источник (аналог SearchResultFileSource в Double Commander),
/// но вместо реальной папки отдаёт плоский список найденных файлов.
/// Implements the IFileSystem contract so it can be fed into a file panel like any
/// other source, returning the flat list of found files instead of a real folder.
/// </summary>
public interface ISearchResultSource : IFileSystem
{
    /// <summary>Список результатов поиска (для отображения совпавших строк). / Search results.</summary>
    IReadOnlyList<SearchResult> Results { get; }

    /// <summary>
    /// Перестраивает внутренний кэш, перечитывая реальное состояние файлов
    /// (файлы, которых уже нет на диске, исключаются). Вызывается после
    /// операций в панели (удаление/перенос).
    /// Rebuilds the internal cache by re-reading the real file state (missing files
    /// are dropped). Called after panel operations (delete/move).
    /// </summary>
    void SyncWithFileSystem();

    /// <summary>
    /// Обновляет путь результата после переименования на месте.
    /// Updates a result path after an in-place rename.
    /// </summary>
    void UpdatePath(string oldPath, string newPath);
}
