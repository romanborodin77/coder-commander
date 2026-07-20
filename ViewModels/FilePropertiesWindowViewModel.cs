using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel диалога свойств файла/папки. Содержит метаданные, команды копирования пути,
/// открытия папки/файла, редактирования атрибутов и меток времени, и (для папок) рекурсивный подсчёт статистики.
/// Properties dialog ViewModel. Contains metadata, copy-path / open-folder / open-file commands,
/// attribute/timestamp editing, and (for folders) recursive statistics computation.
/// </summary>
public partial class FilePropertiesWindowViewModel : ObservableObject
{
    private const FileAttributes Togglable =
        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _type = "";
    [ObservableProperty] private string _sizeDisplay = "—";
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private long _fileCount;
    [ObservableProperty] private long _directoryCount;
    [ObservableProperty] private long _symlinkCount;
    [ObservableProperty] private string _created = "";
    [ObservableProperty] private string _modified = "";
    [ObservableProperty] private string _accessed = "";
    [ObservableProperty] private string _attributes = "";
    [ObservableProperty] private bool _isFolder;
    [ObservableProperty] private bool _isComputing;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private long _processedItems;
    [ObservableProperty] private double _sizeProgressPercent;

    [ObservableProperty] private bool _isReadOnly;
    [ObservableProperty] private bool _isHidden;
    [ObservableProperty] private bool _isSystem;
    [ObservableProperty] private bool _isArchive;

    [ObservableProperty] private string _typeIconGlyph = "\uE8A5";

    [ObservableProperty] private bool _editReadOnly;
    [ObservableProperty] private bool _editHidden;
    [ObservableProperty] private bool _editSystem;
    [ObservableProperty] private bool _editArchive;

    [ObservableProperty] private DateTime? _editCreatedDate;
    [ObservableProperty] private string _editCreatedTime = "00:00:00";
    [ObservableProperty] private DateTime? _editModifiedDate;
    [ObservableProperty] private string _editModifiedTime = "00:00:00";
    [ObservableProperty] private DateTime? _editAccessedDate;
    [ObservableProperty] private string _editAccessedTime = "00:00:00";

    [ObservableProperty] private bool _applyCreated = true;
    [ObservableProperty] private bool _applyModified = true;
    [ObservableProperty] private bool _applyAccessed = true;

    public ObservableCollection<AttrEntry> Items { get; } = new();
    [ObservableProperty] private bool _isMulti;
    [ObservableProperty] private string _itemsTitle = "";

    private string _applyPath = "";
    private bool _applyIsDir;

    public Action? RequestClose { get; set; }

    public FilePropertiesWindowViewModel() { }

    /// <summary>
    /// Загружает метаданные из файловой системы и (для папок) запускает рекурсивный подсчёт.
    /// Loads metadata from the file system and (for folders) starts recursive statistics.
    /// </summary>
    public async Task LoadAsync(string itemPath, bool isDirectory, CancellationToken ct)
    {
        Path = itemPath;
        Name = System.IO.Path.GetFileName(itemPath.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar)) is { Length: > 0 } n ? n : itemPath;
        IsFolder = isDirectory;
        Type = isDirectory
            ? LocalizationService.Current.GetString("Props.Folder")
            : LocalizationService.Current.GetString("Props.File");

        TypeIconGlyph = isDirectory ? "\uE8B7" : ResolveFileIcon(itemPath);

        _applyPath = itemPath;
        _applyIsDir = isDirectory;

        FileSystemInfo info = isDirectory ? new DirectoryInfo(itemPath) : new FileInfo(itemPath);
        if (info.Exists)
        {
            if (!isDirectory)
            {
                SizeBytes = ((FileInfo)info).Length;
                SizeDisplay = FormatBytes(SizeBytes);
                SizeProgressPercent = ComputeSizePercent(SizeBytes);
            }
            Created = info.CreationTime.ToString("g");
            Modified = info.LastWriteTime.ToString("g");
            Accessed = info.LastAccessTime.ToString("g");

            var attr = info.Attributes;
            IsReadOnly = attr.HasFlag(FileAttributes.ReadOnly);
            IsHidden = attr.HasFlag(FileAttributes.Hidden);
            IsSystem = attr.HasFlag(FileAttributes.System);
            IsArchive = attr.HasFlag(FileAttributes.Archive);
            Attributes = attr.ToString();

            EditReadOnly = IsReadOnly;
            EditHidden = IsHidden;
            EditSystem = IsSystem;
            EditArchive = IsArchive;

            EditCreatedDate = info.CreationTime.Date;
            EditCreatedTime = info.CreationTime.ToString("HH:mm:ss");
            EditModifiedDate = info.LastWriteTime.Date;
            EditModifiedTime = info.LastWriteTime.ToString("HH:mm:ss");
            EditAccessedDate = info.LastAccessTime.Date;
            EditAccessedTime = info.LastAccessTime.ToString("HH:mm:ss");
        }

        InitItems(new List<string> { itemPath });

        if (isDirectory)
        {
            IsComputing = true;
            Status = LocalizationService.Current.GetString("Props.Computing");
            var progress = new Progress<OperationProgress>(p =>
            {
                ProcessedItems = p.FilesDone;
                Status = string.Format(LocalizationService.Current.GetString("Props.Processed"), p.FilesDone);
            });
            try
            {
                var op = new CalculateStatisticsOperation(itemPath, progress);
                await op.ExecuteAsync(ct).ConfigureAwait(false);
                var r = op.Result;
                FileCount = r.FileCount;
                DirectoryCount = r.DirectoryCount;
                SymlinkCount = r.SymlinkCount;
                SizeDisplay = r.TotalSizeDisplay;
                SizeBytes = r.TotalSize;
                SizeProgressPercent = ComputeSizePercent(r.TotalSize);
                Status = string.Format(LocalizationService.Current.GetString("Props.DoneTime"),
                    r.Elapsed.TotalSeconds.ToString("0.##"));
            }
            catch (Exception ex)
            {
                Status = string.Format(LocalizationService.Current.GetString("Props.Error"), ex.Message);
            }
            finally { IsComputing = false; }
        }
        else
        {
            Status = LocalizationService.Current.GetString("Props.Done");
        }
    }

    public async Task LoadMultipleAsync(List<string> paths, CancellationToken ct)
    {
        InitItems(paths);

        var first = Items.FirstOrDefault(i => i.Error is null) ?? Items[0];
        _applyPath = first.FullPath;
        _applyIsDir = first.IsDirectory;

        EditReadOnly = first.Attributes.HasFlag(FileAttributes.ReadOnly);
        EditHidden = first.Attributes.HasFlag(FileAttributes.Hidden);
        EditSystem = first.Attributes.HasFlag(FileAttributes.System);
        EditArchive = first.Attributes.HasFlag(FileAttributes.Archive);

        if (first.Created != default) { EditCreatedDate = first.Created.Date; EditCreatedTime = first.Created.ToString("HH:mm:ss"); }
        if (first.Modified != default) { EditModifiedDate = first.Modified.Date; EditModifiedTime = first.Modified.ToString("HH:mm:ss"); }
        if (first.Accessed != default) { EditAccessedDate = first.Accessed.Date; EditAccessedTime = first.Accessed.ToString("HH:mm:ss"); }

        Name = Items[0].Name;
        Path = Items[0].FullPath;
        IsFolder = Items[0].IsDirectory;
        Type = Items[0].IsDirectory
            ? LocalizationService.Current.GetString("Props.Folder")
            : LocalizationService.Current.GetString("Props.File");
        TypeIconGlyph = Items[0].IsDirectory ? "\uE8B7" : ResolveFileIcon(Items[0].FullPath);

        if (Items.Count > 0)
        {
            var fsInfo = Items[0].IsDirectory
                ? (FileSystemInfo)new DirectoryInfo(Items[0].FullPath)
                : new FileInfo(Items[0].FullPath);
            if (fsInfo.Exists)
            {
                if (!Items[0].IsDirectory)
                {
                    SizeBytes = ((FileInfo)fsInfo).Length;
                    SizeDisplay = FormatBytes(SizeBytes);
                    SizeProgressPercent = ComputeSizePercent(SizeBytes);
                }
                Created = fsInfo.CreationTime.ToString("g");
                Modified = fsInfo.LastWriteTime.ToString("g");
                Accessed = fsInfo.LastAccessTime.ToString("g");
                var attr = fsInfo.Attributes;
                IsReadOnly = attr.HasFlag(FileAttributes.ReadOnly);
                IsHidden = attr.HasFlag(FileAttributes.Hidden);
                IsSystem = attr.HasFlag(FileAttributes.System);
                IsArchive = attr.HasFlag(FileAttributes.Archive);
                Attributes = attr.ToString();
            }
        }

        if (Items[0].IsDirectory && Items.Count == 1)
        {
            IsComputing = true;
            Status = LocalizationService.Current.GetString("Props.Computing");
            var progress = new Progress<OperationProgress>(p =>
            {
                ProcessedItems = p.FilesDone;
                Status = string.Format(LocalizationService.Current.GetString("Props.Processed"), p.FilesDone);
            });
            try
            {
                var op = new CalculateStatisticsOperation(Items[0].FullPath, progress);
                await op.ExecuteAsync(ct).ConfigureAwait(false);
                var r = op.Result;
                FileCount = r.FileCount;
                DirectoryCount = r.DirectoryCount;
                SymlinkCount = r.SymlinkCount;
                SizeDisplay = r.TotalSizeDisplay;
                SizeBytes = r.TotalSize;
                SizeProgressPercent = ComputeSizePercent(r.TotalSize);
                Status = string.Format(LocalizationService.Current.GetString("Props.DoneTime"),
                    r.Elapsed.TotalSeconds.ToString("0.##"));
            }
            catch (Exception ex)
            {
                Status = string.Format(LocalizationService.Current.GetString("Props.Error"), ex.Message);
            }
            finally { IsComputing = false; }
        }
        else
        {
            Status = Items.Count > 1
                ? string.Format(LocalizationService.Current.GetString("Attr.FilesSelected"), Items.Count)
                : LocalizationService.Current.GetString("Props.Done");
        }
    }

    private void InitItems(List<string> paths)
    {
        Items.Clear();
        foreach (var p in paths)
        {
            try
            {
                var isDir = Directory.Exists(p) && !File.Exists(p);
                FileAttributes a = isDir ? new DirectoryInfo(p).Attributes : new FileInfo(p).Attributes;
                var fi = isDir ? (FileSystemInfo)new DirectoryInfo(p) : new FileInfo(p);
                Items.Add(new AttrEntry(p, isDir, a, fi.CreationTime, fi.LastWriteTime, fi.LastAccessTime));
            }
            catch (Exception ex)
            {
                Items.Add(new AttrEntry(p, false, 0, default, default, default) { Error = ex.Message });
            }
        }
        ItemsTitle = Items.Count > 1
            ? string.Format(LocalizationService.Current.GetString("Attr.FilesSelected"), Items.Count)
            : string.Format(LocalizationService.Current.GetString("Attr.Single"), Items.Count > 0 ? Items[0].Name : "");
        IsMulti = Items.Count > 1;
    }

    [RelayCommand]
    private void Apply()
    {
        int done = 0;
        var errors = new List<string>();
        var desired = FileAttributes.Normal;
        if (EditReadOnly) desired |= FileAttributes.ReadOnly;
        if (EditHidden) desired |= FileAttributes.Hidden;
        if (EditSystem) desired |= FileAttributes.System;
        if (EditArchive) desired |= FileAttributes.Archive;

        var createdUtc = BuildUtc(EditCreatedDate, EditCreatedTime);
        var modifiedUtc = BuildUtc(EditModifiedDate, EditModifiedTime);
        var accessedUtc = BuildUtc(EditAccessedDate, EditAccessedTime);

        foreach (var it in Items)
        {
            if (it.Error is not null) { errors.Add($"{it.Name}: {it.Error}"); continue; }
            try
            {
                var cur = File.GetAttributes(it.FullPath);
                var next = (cur & ~Togglable) | (desired & Togglable);
                File.SetAttributes(it.FullPath, next);

                if (ApplyCreated && createdUtc.HasValue) File.SetCreationTimeUtc(it.FullPath, createdUtc.Value);
                if (ApplyModified && modifiedUtc.HasValue) File.SetLastWriteTimeUtc(it.FullPath, modifiedUtc.Value);
                if (ApplyAccessed && accessedUtc.HasValue) File.SetLastAccessTimeUtc(it.FullPath, accessedUtc.Value);
                done++;
            }
            catch (Exception ex)
            {
                errors.Add($"{it.Name}: {ex.Message}");
            }
        }

        Status = errors.Count == 0
            ? string.Format(LocalizationService.Current.GetString("Attr.AttrApplied"), done)
            : string.Format(LocalizationService.Current.GetString("Attr.AttrApplied"), done) + $" ({errors.Count} {LocalizationService.Current.GetString("Attr.ErrSuffix")})";
    }

    [RelayCommand]
    private void Close() => RequestClose?.Invoke();

    private static DateTime? BuildUtc(DateTime? date, string timeText)
    {
        if (date is null) return null;
        var t = TimeSpan.Zero;
        if (!string.IsNullOrWhiteSpace(timeText))
        {
            if (TimeSpan.TryParse(timeText, out var ts)) t = ts;
            else if (DateTime.TryParse(timeText, out var dt)) t = dt.TimeOfDay;
            else return null;
        }
        var local = new DateTime(date.Value.Year, date.Value.Month, date.Value.Day,
            t.Hours, t.Minutes, t.Seconds, DateTimeKind.Local);
        return local.ToUniversalTime();
    }

    /// <summary>
    /// Копирует полный путь в буфер обмена.
    /// Copies the full path to clipboard.
    /// </summary>
    [RelayCommand]
    private void CopyPath()
    {
        if (string.IsNullOrEmpty(Path)) return;
        try
        {
            Clipboard.SetText(Path);
            Status = LocalizationService.Current.GetString("Props.Copied");
        }
        catch { /* clipboard may be unavailable */ }
    }

    /// <summary>
    /// Открывает проводник с выделенным файлом/папкой.
    /// Opens Explorer with the file/folder selected.
    /// </summary>
    [RelayCommand]
    private void OpenContainingFolder()
    {
        try
        {
            if (IsFolder)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path}\"") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path}\"") { UseShellExecute = true });
            }
        }
        catch { /* explorer may fail */ }
    }

    /// <summary>
    /// Открывает файл ассоциированным приложением (только для файлов).
    /// Opens the file with the associated application (files only).
    /// </summary>
    [RelayCommand]
    private void OpenFile()
    {
        if (IsFolder || string.IsNullOrEmpty(Path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true });
        }
        catch { /* shell open may fail */ }
    }

    public static string FormatBytes(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.##} {u[i]}";
    }

    private static double ComputeSizePercent(long bytes)
    {
        const long oneGb = 1073741824L;
        if (bytes <= 0) return 0;
        if (bytes >= oneGb) return 100;
        return (double)bytes / oneGb * 100;
    }

    private static string ResolveFileIcon(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath)?.ToLowerInvariant() ?? "";
        return ext switch
        {
            ".txt" or ".md" or ".log" or ".rtf" => "\uE8A5",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".ico" or ".webp" => "\uE91B",
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".aac" or ".wma" => "\uE8D6",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".webm" => "\uE8D7",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" => "\uE8B7",
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => "\uECAA",
            ".cs" or ".vb" or ".fs" or ".py" or ".js" or ".ts" or ".java" or ".cpp" or ".c" or ".h" or ".rs" or ".go" => "\uE943",
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" or ".ini" or ".cfg" => "\uE9F5",
            ".pdf" => "\uEA90",
            ".html" or ".htm" or ".css" => "\uE8A5",
            ".dll" or ".lib" or ".so" => "\uE943",
            _ => "\uE8A5"
        };
    }
}

/// <summary>
/// Строка списка выбранных элементов в диалоге свойств.
/// A row of the selected-items list in the properties dialog.
/// </summary>
public sealed class AttrEntry
{
    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    public FileAttributes Attributes { get; }
    public DateTime Created { get; }
    public DateTime Modified { get; }
    public DateTime Accessed { get; }
    public string? Error { get; set; }

    public string AttributeText
        => Error is not null ? "?" :
           string.Join("", Attributes.ToString().Split(',')
               .Select(s => s.Trim() switch
               {
                   "ReadOnly" => "R", "Hidden" => "H", "System" => "S", "Archive" => "A",
                   "Directory" => "D", "Normal" => "", _ => ""
               })).Trim();

    public AttrEntry(string fullPath, bool isDirectory, FileAttributes attributes,
                     DateTime created, DateTime modified, DateTime accessed)
    {
        FullPath = fullPath;
        Name = System.IO.Path.GetFileName(fullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        IsDirectory = isDirectory;
        Attributes = attributes;
        Created = created;
        Modified = modified;
        Accessed = accessed;
    }
}
