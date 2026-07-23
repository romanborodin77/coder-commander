using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;
using CoderCommander.Views;
using OverwritePolicy = CoderCommander.Operations.OverwritePolicy;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel диалога копирования/перемещения.
/// ViewModel for the copy/move dialog.
/// </summary>
public partial class CopyMoveDialogViewModel : ObservableObject
{
    private readonly Views.CopyMoveDialog _window;
    private readonly List<string> _sourcePaths = new();

    /// <summary>Исходный путь. / Source path.</summary>
    [ObservableProperty] private string _sourcePath = "";

    /// <summary>Целевой путь. / Destination path.</summary>
    [ObservableProperty] private string _destinationPath = "";

    /// <summary>Количество файлов. / File count.</summary>
    [ObservableProperty] private int _fileCount;

    /// <summary>Общий размер. / Total size.</summary>
    [ObservableProperty] private string _totalSizeText = "";

    /// <summary>true = копирование, false = перемещение. / true = copy, false = move.</summary>
    [ObservableProperty] private bool _isCopyMode = true;

    /// <summary>Заголовок окна. / Window title.</summary>
    [ObservableProperty] private string _title = "";

    /// <summary>Политика перезаписи. / Overwrite policy.</summary>
    [ObservableProperty] private OverwritePolicy _selectedOverwritePolicy = OverwritePolicy.Overwrite;

    /// <summary>Копировать атрибуты. / Copy attributes.</summary>
    [ObservableProperty] private bool _copyAttributes = true;

    /// <summary>Копировать временные метки. / Copy timestamps.</summary>
    [ObservableProperty] private bool _copyTimestamps = true;

    /// <summary>Копировать NTFS ACL. / Copy NTFS permissions.</summary>
    [ObservableProperty] private bool _copyNtfsPermissions;

    /// <summary>Предупреждение: источник = назначение. / Warning: source = destination.</summary>
    [ObservableProperty] private string _selfCopyWarning = "";

    /// <summary>Добавить в очередь вместо немедленного выполнения. / Add to queue instead of immediate execution.</summary>
    [ObservableProperty] private bool _addToQueue;

    /// <summary>Колбэк вызывается при нажатии OK после валидации. / Callback invoked on OK after validation.</summary>
    public Action? ExecuteRequested { get; set; }

    /// <summary>Список элементов (имя + иконка) для отображения. / Items list for display.</summary>
    public ObservableCollection<SourceItemInfo> SourceItems { get; } = new();

    /// <summary>
    /// Варианты политики перезаписи для ComboBox.
    /// Overwrite policy options for ComboBox.
    /// </summary>
    public IReadOnlyList<OverwritePolicy> OverwritePolicyOptions { get; } = new[]
    {
        OverwritePolicy.Overwrite,
        OverwritePolicy.Skip,
        OverwritePolicy.OverwriteOlder,
        OverwritePolicy.OverwriteSmaller,
        OverwritePolicy.AutoRename,
        OverwritePolicy.Ask,
    };

    /// <summary>
    /// Создаёт ViewModel. / Creates the ViewModel.
    /// </summary>
    public CopyMoveDialogViewModel(Views.CopyMoveDialog window)
    {
        _window = window;
        var s = SettingsService.Load();
        _copyAttributes = s.CopyAttributes;
        _copyTimestamps = s.CopyTimestamps;
        _selectedOverwritePolicy = s.DefaultOverwritePolicy switch
        {
            "Always" => OverwritePolicy.Overwrite,
            "Never" => OverwritePolicy.Skip,
            "OverwriteOlder" => OverwritePolicy.OverwriteOlder,
            "OverwriteSmaller" => OverwritePolicy.OverwriteSmaller,
            "AutoRename" => OverwritePolicy.AutoRename,
            "Ask" => OverwritePolicy.Ask,
            _ => OverwritePolicy.Overwrite,
        };
    }

    /// <summary>
    /// Вычисляет суммарный размер файлов. / Computes total size of files.
    /// </summary>
    public void CalculateTotalSize(IEnumerable<string> paths)
    {
        long total = 0;
        int count = 0;
        SourceItems.Clear();
        _sourcePaths.Clear();
        foreach (var p in paths)
        {
            _sourcePaths.Add(p);
            var name = Path.GetFileName(p.TrimEnd('/', '\\'));
            var isCloud = p.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase);
            var isDir = isCloud ? p.TrimEnd('/').EndsWith("/") || !Path.HasExtension(p) : Directory.Exists(p);
            SourceItems.Add(new SourceItemInfo(name, isDir));

            if (isCloud)
            {
                // Для облачных путей размер неизвестен до скачивания — считаем только количество.
                // For cloud paths, size is unknown without download — count only.
                count++;
            }
            else if (File.Exists(p))
            {
                total += new FileInfo(p).Length;
                count++;
            }
            else if (Directory.Exists(p))
            {
                var (bytes, files) = CalculateDirSize(p);
                total += bytes;
                count += files;
            }
        }
        FileCount = count;
        TotalSizeText = count > 0 && total == 0 ? "" : FormatSize(total);
    }

    partial void OnDestinationPathChanged(string value)
    {
        CheckSelfCopy();
    }

    private void CheckSelfCopy()
    {
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            SelfCopyWarning = "";
            return;
        }

        // cloud:// и другие виртуальные пути — пропускаем проверку self-copy
        // (Path.GetFullPath не работает с не-локальными путями).
        // cloud:// and other virtual paths — skip self-copy check
        // (Path.GetFullPath doesn't work with non-local paths).
        if (DestinationPath.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase))
        {
            SelfCopyWarning = "";
            return;
        }

        try
        {
            var destNorm = Path.GetFullPath(DestinationPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var src in _sourcePaths)
            {
                if (src.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase)) continue;
                var srcNorm = Path.GetFullPath(src).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(srcNorm, destNorm, StringComparison.OrdinalIgnoreCase))
                {
                    SelfCopyWarning = LocalizationService.Current.GetString("CopyMove.SelfCopyWarning");
                    return;
                }
            }
        }
        catch
        {
            // Path.GetFullPath может бросить для нестандартных путей — просто пропускаем.
            // Path.GetFullPath may throw for non-standard paths — just skip.
        }
        SelfCopyWarning = "";
    }

    private static (long Bytes, int Files) CalculateDirSize(string dir)
    {
        long bytes = 0;
        int files = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { bytes += new FileInfo(f).Length; } catch { }
                files++;
            }
        }
        catch { }
        return (bytes, files);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    /// <summary>Обзор целевой папки. / Browse destination folder.</summary>
    [RelayCommand]
    private void BrowseDestination()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationService.Current.GetString("CopyMove.SelectFolder")
        };
        if (dialog.ShowDialog() == true)
            DestinationPath = dialog.FolderName;
    }

    /// <summary>OK — начать операцию. / OK — start operation.</summary>
    [RelayCommand]
    private void OK()
    {
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            StyledMessageBoxWindow.Show(
                LocalizationService.Current.GetString("CopyMove.SelectFolder"),
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrEmpty(SelfCopyWarning))
        {
            StyledMessageBoxWindow.Show(
                SelfCopyWarning,
                Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExecuteRequested?.Invoke();
        _window.Close();
    }

    /// <summary>Отмена. / Cancel.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _window.Close();
    }
}

/// <summary>
/// Элемент списка источников в диалоге копирования/перемещения.
/// Source item entry in the copy/move dialog.
/// </summary>
public sealed class SourceItemInfo
{
    public string Name { get; }
    public bool IsDirectory { get; }
    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE7C3";

    public SourceItemInfo(string name, bool isDirectory)
    {
        Name = name;
        IsDirectory = isDirectory;
    }
}
