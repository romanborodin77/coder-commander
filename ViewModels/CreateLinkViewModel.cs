using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel диалога создания символьных/жёстких ссылок.
/// ViewModel for the symbolic/hard link creation dialog.
/// </summary>
public sealed partial class CreateLinkViewModel : ObservableObject
{
    /// <summary>Запрашивает закрытие окна (Cancel/OK). / Requests window close (Cancel/OK).</summary>
    public Action<bool>? RequestClose { get; set; }

    /// <summary>Путь к исходному файлу/папке. / Path to the source file/folder.</summary>
    [ObservableProperty] private string _targetPath = "";

    /// <summary>Имя создаваемой ссылки (редактируемое). / Name of the link being created (editable).</summary>
    [ObservableProperty] private string _linkName = "";

    /// <summary>Полный путь ссылки (формируется автоматически). / Full link path (auto-generated).</summary>
    [ObservableProperty] private string _linkPath = "";

    /// <summary>Тип ссылки: true = жёсткая, false = символическая. / Link type: true = hard, false = symbolic.</summary>
    [ObservableProperty] private bool _isHardlink;

    /// <summary>Является ли источник папкой. / Whether the source is a directory.</summary>
    [ObservableProperty] private bool _isDirectory;

    /// <summary>Заголовок окна. / Window title.</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>Название типа ссылки для отображения. / Link type display name.</summary>
    [ObservableProperty] private string _typeName = "";

    /// <summary>Целевая папка (где создаётся ссылка). / Target folder (where the link is created).</summary>
    [ObservableProperty] private string _targetFolder = "";

    /// <summary>Статус/лог операций. / Status/operation log.</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>Список файлов для массового создания ссылок (если несколько). / File list for batch link creation (if multiple).</summary>
    [ObservableProperty] private List<LinkEntry> _linkEntries = new();

    /// <summary>Режим нескольких файлов. / Multiple-files mode.</summary>
    [ObservableProperty] private bool _isMultiMode;

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Конструктор для одного файла.
    /// Constructor for a single file.
    /// </summary>
    public CreateLinkViewModel(string targetPath, bool isHardlink, bool isDirectory, string targetFolder)
    {
        TargetPath = targetPath;
        IsHardlink = isHardlink;
        IsDirectory = isDirectory;
        TargetFolder = targetFolder;
        IsMultiMode = false;

        Title = isHardlink
            ? LocalizationService.Current.GetString("Link.Title.Hardlink")
            : LocalizationService.Current.GetString("Link.Title.Symlink");

        TypeName = isHardlink
            ? LocalizationService.Current.GetString("Link.Type.Hardlink")
            : LocalizationService.Current.GetString("Link.Type.Symlink");

        LinkName = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
        UpdateLinkPath();
    }

    /// <summary>
    /// Конструктор для нескольких файлов.
    /// Constructor for multiple files.
    /// </summary>
    public CreateLinkViewModel(List<(string Path, bool IsDir)> files, bool isHardlink, string targetFolder)
    {
        IsHardlink = isHardlink;
        TargetFolder = targetFolder;
        IsMultiMode = true;
        IsDirectory = false;

        Title = (isHardlink
            ? LocalizationService.Current.GetString("Link.Title.Hardlink")
            : LocalizationService.Current.GetString("Link.Title.Symlink"))
            + $" ({files.Count})";

        TypeName = isHardlink
            ? LocalizationService.Current.GetString("Link.Type.Hardlink")
            : LocalizationService.Current.GetString("Link.Type.Symlink");

        LinkEntries = files.Select(f => new LinkEntry
        {
            SourcePath = f.Path,
            LinkName = Path.GetFileName(f.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "",
            IsDirectory = f.IsDir
        }).ToList();

        TargetPath = files.Count > 0 ? files[0].Path : "";
    }

    /// <summary>
    /// При изменении LinkName — обновлять LinkPath.
    /// When LinkName changes — update LinkPath.
    /// </summary>
    partial void OnLinkNameChanged(string value)
    {
        if (!IsMultiMode)
            UpdateLinkPath();
    }

    /// <summary>Обновляет LinkPath на основе LinkName и TargetFolder. / Updates LinkPath from LinkName and TargetFolder.</summary>
    private void UpdateLinkPath()
    {
        if (string.IsNullOrWhiteSpace(LinkName))
        {
            LinkPath = "";
            return;
        }
        LinkPath = Path.Combine(TargetFolder, LinkName);
    }

    /// <summary>Создать ссылку (одна или несколько). / Create link(s) (single or multiple).</summary>
    [RelayCommand]
    private async Task Create()
    {
        var fs = FileService.FileSystem;

        if (IsMultiMode)
        {
            await CreateMultipleLinks(fs);
            return;
        }

        if (string.IsNullOrWhiteSpace(LinkName))
        {
            Status = LocalizationService.Current.GetString("Link.NameEmpty");
            return;
        }

        if (LinkName.IndexOfAny(InvalidNameChars) >= 0)
        {
            Status = LocalizationService.Current.GetString("Link.InvalidChars");
            return;
        }

        var fullLinkPath = Path.Combine(TargetFolder, LinkName);

        if (File.Exists(fullLinkPath) || Directory.Exists(fullLinkPath))
        {
            Status = string.Format(LocalizationService.Current.GetString("Link.AlreadyExists"), fullLinkPath);
            return;
        }

        try
        {
            if (IsHardlink)
            {
                if (IsDirectory)
                {
                    Status = LocalizationService.Current.GetString("Link.Error");
                    return;
                }
                await fs.CreateHardlinkAsync(TargetPath, fullLinkPath);
            }
            else
            {
                await fs.CreateSymbolicLinkAsync(TargetPath, fullLinkPath, IsDirectory);
            }

            Status = string.Format(LocalizationService.Current.GetString("Link.Success"), fullLinkPath);
            RequestClose?.Invoke(true);
        }
        catch (UnauthorizedAccessException)
        {
            Status = LocalizationService.Current.GetString("Link.AdminRequired");
        }
        catch (Exception ex)
        {
            Status = string.Format(LocalizationService.Current.GetString("Link.Error"), ex.Message);
        }
    }

    /// <summary>
    /// Создать ссылки для нескольких файлов.
    /// Create links for multiple files.
    /// </summary>
    private async Task CreateMultipleLinks(IFileSystem fs)
    {
        int ok = 0;
        int errors = 0;

        foreach (var entry in LinkEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.LinkName)) { errors++; continue; }
            if (entry.LinkName.IndexOfAny(InvalidNameChars) >= 0) { errors++; continue; }

            var fullLinkPath = Path.Combine(TargetFolder, entry.LinkName);

            try
            {
                if (File.Exists(fullLinkPath) || Directory.Exists(fullLinkPath))
                {
                    errors++;
                    continue;
                }

                if (IsHardlink)
                {
                    if (entry.IsDirectory) { errors++; continue; }
                    await fs.CreateHardlinkAsync(entry.SourcePath, fullLinkPath);
                }
                else
                {
                    await fs.CreateSymbolicLinkAsync(entry.SourcePath, fullLinkPath, entry.IsDirectory);
                }
                ok++;
            }
            catch
            {
                errors++;
            }
        }

        Status = string.Format(LocalizationService.Current.GetString("Link.Done"), ok, errors);
        if (ok > 0)
            RequestClose?.Invoke(true);
    }

    /// <summary>Отмена. / Cancel.</summary>
    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}

/// <summary>
/// Строка списка файлов для массового создания ссылок.
/// Row of the file list for batch link creation.
/// </summary>
public sealed partial class LinkEntry : ObservableObject
{
    /// <summary>Путь к исходному файлу. / Source file path.</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>Имя создаваемой ссылки (редактируемое). / Link name being created (editable).</summary>
    [ObservableProperty] private string _linkName = "";

    /// <summary>Является ли папкой. / Whether it is a directory.</summary>
    public bool IsDirectory { get; set; }
}
