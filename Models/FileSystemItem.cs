using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CoderCommander.Services;

namespace CoderCommander.Models;

/// <summary>
/// Модель элемента файловой системы (файл или каталог) с поддержкой отслеживания изменений.
/// File system item model (file or directory) with change tracking support.
/// </summary>
public partial class FileSystemItem : ObservableObject
{
    /// <summary>
    /// Полный путь к файлу или каталогу.
    /// Full path to the file or directory.
    /// </summary>
    public string FullPath { get; }

    /// <summary>
    /// Имя файла или каталога (без пути). Если задан DisplayName — возвращает его (для Flat View).
    /// File or directory name (without path). Returns DisplayName if set (for Flat View).
    /// </summary>
    public string Name => DisplayName ?? (Path.GetFileName(FullPath) ?? FullPath);

    /// <summary>
    /// Пользовательское отображаемое имя (для Flat View — относительный путь).
    /// Custom display name (for Flat View — relative path).
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Является ли элемент каталогом.
    /// Whether the item is a directory.
    /// </summary>
    public bool IsDirectory { get; }

    /// <summary>
    /// Является ли элемент ссылкой на родительский каталог ("..").
    /// Whether the item is a parent directory reference ("..").
    /// </summary>
    public bool IsParent { get; }

    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Размер файла в байтах (для каталогов значение 0).
    /// File size in bytes (0 for directories).
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// Дата и время последнего изменения.
    /// Last modified date and time.
    /// </summary>
    public DateTime Modified { get; }

    /// <summary>
    /// Дата и время создания.
    /// Creation date and time.
    /// </summary>
    public DateTime CreatedDate { get; }

    /// <summary>
    /// Атрибуты файла.
    /// File attributes.
    /// </summary>
    public string Attributes { get; }

    /// <summary>
    /// Расширение файла в нижнем регистре (для каталогов — пустая строка).
    /// File extension in lowercase (empty string for directories).
    /// </summary>
    public string Extension { get; }

    [ObservableProperty] private GitState _gitState = GitState.Unchanged;

    /// <summary>
    /// Инициализирует новый экземпляр FileSystemItem.
    /// Initializes a new instance of FileSystemItem.
    /// </summary>
    public FileSystemItem(string fullPath, bool isDirectory, long size = 0, DateTime? modified = null, bool isParent = false, GitState gitState = GitState.Unchanged, string? displayName = null)
    {
        FullPath = fullPath; IsDirectory = isDirectory; IsParent = isParent; Size = size; _gitState = gitState;
        Modified = modified ?? DateTime.Now;
        CreatedDate = isParent ? DateTime.MinValue : GetSafeCreationTime(fullPath, isDirectory);
        Attributes = isParent ? "" : GetSafeAttributes(fullPath, isDirectory);
        Extension = isDirectory ? "" : Path.GetExtension(fullPath).ToLowerInvariant();
        DisplayName = displayName;
        SizeDisplay = isDirectory ? "<DIR>" : FormatSize(size);
        ModifiedDisplay = Modified.ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>
    /// Отображаемый размер: "&lt;DIR&gt;" для каталогов или человекочитаемый формат для файлов.
    /// Display size: "&lt;DIR&gt;" for directories or human-readable format for files.
    /// </summary>
    public string SizeDisplay { get; }

    /// <summary>
    /// Отображаемая дата изменения в формате "yyyy-MM-dd HH:mm".
    /// Display modified date in "yyyy-MM-dd HH:mm" format.
    /// </summary>
    public string ModifiedDisplay { get; }

    private static string FormatSize(long bytes)
    {
        if (bytes < 0) return "--";
        string[] u = ["B","KB","MB","GB","TB"]; double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }

    private static DateTime GetSafeCreationTime(string path, bool isDirectory)
    {
        if (isDirectory) return DateTime.MinValue;
        try { return File.GetCreationTime(path); }
        catch { return DateTime.MinValue; }
    }

    private static string GetSafeAttributes(string path, bool isDirectory)
    {
        if (isDirectory) return "";
        try { return File.GetAttributes(path).ToString(); }
        catch { return ""; }
    }
}