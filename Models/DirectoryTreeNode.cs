using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.Models;

/// <summary>
/// Узел дерева каталогов для навигации (ph5.6).
/// Tree node for directory navigation (ph5.6).
/// Поддерживает ленивую загрузку подпапок: дочерние элементы загружаются
/// только при первом раскрытии узла.
/// Supports lazy loading: child items are loaded only on first expand.
/// </summary>
public partial class DirectoryTreeNode : ObservableObject
{
    /// <summary>Полный путь каталога. / Full directory path.</summary>
    public string FullPath { get; }

    /// <summary>Отображаемое имя (имя каталога или буква диска). / Display name (folder name or drive letter).</summary>
    public string DisplayName { get; }

    /// <summary>Является ли узел корневым (диск). / Whether this is a root (drive) node.</summary>
    public bool IsDrive { get; }

    /// <summary>Тип диска (для корневых узлов). / Drive type (for root nodes).</summary>
    public DriveType DriveType { get; }

    /// <summary>Загружены ли дочерние элементы. / Whether children have been loaded.</summary>
    public bool IsLoaded { get; set; }

    /// <summary>Признак загрузки подпапок (для отображения индикатора). / Loading indicator.</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>Развёрнут ли узел в TreeView. / Whether the node is expanded in TreeView.</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>Дочерние узлы (подпапки). / Child nodes (subdirectories).</summary>
    public ObservableCollection<DirectoryTreeNode> Children { get; } = [];

    /// <summary>
    /// Создаёт корневой узел диска.
    /// Creates a root drive node.
    /// </summary>
    /// <param name="driveName">Буква диска (например, "C:\"). / Drive letter (e.g., "C:\").</param>
    /// <param name="driveType">Тип диска. / Drive type.</param>
    public DirectoryTreeNode(string driveName, DriveType driveType)
    {
        FullPath = driveName;
        DisplayName = driveName;
        IsDrive = true;
        DriveType = driveType;
    }

    /// <summary>
    /// Создаёт узел каталога.
    /// Creates a directory node.
    /// </summary>
    /// <param name="path">Путь к каталогу. / Directory path.</param>
    public DirectoryTreeNode(string path)
    {
        FullPath = path;
        DisplayName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(DisplayName))
            DisplayName = path; // корень диска, например "C:\"
        IsDrive = false;
    }

    /// <summary>
    /// Ленивая загрузка подпапок при первом раскрытии узла.
    /// Lazy-loads subdirectories on first expand.
    /// </summary>
    public async Task LoadChildrenAsync()
    {
        if (IsLoaded) return;
        IsLoading = true;

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    var opt = new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
                    };
                    var dirs = Directory.EnumerateDirectories(FullPath, "*", opt)
                        .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);

                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Children.Clear();
                        foreach (var d in dirs)
                            Children.Add(new DirectoryTreeNode(d));
                    }));
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
                catch (InvalidOperationException) { }
            });

            IsLoaded = true;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
