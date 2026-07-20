using System.IO;
using CoderCommander.Models;

namespace CoderCommander.Services;

/// <summary>
/// Сервис для построения дерева каталогов (ph5.6).
/// Service for building the directory tree (ph5.6).
/// Предоставляет корневые узлы (диски) и проверку доступности путей.
/// Provides root nodes (drives) and path accessibility checks.
/// </summary>
public static class DirectoryTreeService
{
    /// <summary>
    /// Возвращает список корневых узлов дерева (диски системы).
    /// Returns root tree nodes (system drives).
    /// </summary>
    /// <returns>Коллекция корневых узлов. / Collection of root nodes.</returns>
    public static IReadOnlyList<DirectoryTreeNode> GetRootNodes()
    {
        var roots = new List<DirectoryTreeNode>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                var name = drive.Name.TrimEnd('\\');
                if (!name.EndsWith(':'))
                    name += "\\";
                else
                    name += "\\";

                roots.Add(new DirectoryTreeNode(name, drive.DriveType));
            }
            catch { /* пропускаем недоступные диски / skip inaccessible drives */ }
        }
        return roots;
    }

    /// <summary>
    /// Возвращает корневой узел для указанного пути (диск, содержащий путь).
    /// Returns the root node for the specified path (the drive containing it).
    /// </summary>
    /// <param name="path">Любой путь в файловой системе. / Any file system path.</param>
    /// <param name="roots">Корневые узлы дерева. / Root tree nodes.</param>
    /// <returns>Корневой узел или null. / Root node or null.</returns>
    public static DirectoryTreeNode? FindRootForPath(string path, IReadOnlyList<DirectoryTreeNode> roots)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root)) return null;

        return roots.FirstOrDefault(r =>
            string.Equals(r.FullPath, root, StringComparison.OrdinalIgnoreCase));
    }
}
