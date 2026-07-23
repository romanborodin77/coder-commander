using System.IO;
using CoderCommander.FileSystem;

namespace CoderCommander.Models;

/// <summary>
/// Модель диска для панели навигации: буква и тип (для выбора иконки).
/// Drive item model for the navigation panel: drive letter and type (for icon selection).
/// </summary>
public sealed class DriveItem
{
    /// <summary>
    /// Буква диска (например, "C:", "D:").
    /// Drive letter (e.g., "C:", "D:").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Тип диска (Fixed, Removable, Network, CDRom и т.д.).
    /// Drive type (Fixed, Removable, Network, CDRom, etc.).
    /// </summary>
    public DriveType Type { get; }

    /// <summary>
    /// Инициализирует новый экземпляр DriveItem с указанными буквой и типом диска.
    /// Initializes a new instance of DriveItem with the specified drive letter and type.
    /// </summary>
    /// <param name="name">Буква диска / Drive letter.</param>
    /// <param name="type">Тип диска / Drive type.</param>
    public DriveItem(string name, DriveType type)
    {
        Name = name;
        Type = type;
    }
}

/// <summary>
/// Модель облачного диска для панели навигации.
/// Cloud drive item model for the navigation panel.
/// </summary>
public sealed class CloudDriveItem
{
    /// <summary>
    /// Отображаемое имя (имя профиля).
    /// Display name (profile name).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Тип иконки (символ шрифта Segoe MDL2 Assets).
    /// Icon glyph (Segoe MDL2 Assets font character).
    /// </summary>
    public string Icon { get; }

    /// <summary>
    /// Идентификатор профиля.
    /// Profile identifier.
    /// </summary>
    public string ProfileId { get; }

    /// <summary>
    /// Подключённая облачная файловая система.
    /// Connected cloud file system.
    /// </summary>
    public CloudFileSystem? FileSystem { get; set; }

    /// <summary>
    /// Создаёт экземпляр CloudDriveItem.
    /// Creates a CloudDriveItem instance.
    /// </summary>
    public CloudDriveItem(string profileId, string name, string icon = "\uE944")
    {
        ProfileId = profileId;
        Name = name;
        Icon = icon;
    }
}
