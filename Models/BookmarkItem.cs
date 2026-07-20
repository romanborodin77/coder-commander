using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.Models;

/// <summary>
/// Модель закладки (избранной папки).
/// Bookmark model (favorite directory).
/// </summary>
public partial class BookmarkItem : ObservableObject
{
    /// <summary>Отображаемое имя закладки. / Display name of the bookmark.</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>Путь к папке. / Path to the directory.</summary>
    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>Иконка (тип папки/диска). / Icon (folder/drive type).</summary>
    [ObservableProperty]
    private string _icon = "\xE8B7"; // FolderIcon Segoe MDL2

    /// <summary>Дата создания закладки. / Bookmark creation date.</summary>
    [ObservableProperty]
    private DateTime _createdAt = DateTime.Now;

    /// <summary>
    /// Создаёт закладку с указанным именем и путём.
    /// Creates a bookmark with the specified name and path.
    /// </summary>
    public BookmarkItem() { }

    /// <summary>
    /// Создаёт закладку с указанным именем и путём.
    /// Creates a bookmark with the specified name and path.
    /// </summary>
    public BookmarkItem(string name, string path)
    {
        Name = name;
        Path = path;
    }

    /// <summary>
    /// Создаёт закладку с именем, путём и иконкой.
    /// Creates a bookmark with name, path and icon.
    /// </summary>
    public BookmarkItem(string name, string path, string icon) : this(name, path)
    {
        Icon = icon;
    }
}
