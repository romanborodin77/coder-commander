using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CoderCommander.FileSystem;
using CoderCommander.Models;

namespace CoderCommander.ViewModels;

/// <summary>
/// Часть PanelViewModel: виртуальный режим «результаты поиска» (ph2.2 / exp.yml).
/// Partial part of PanelViewModel: the "search results" virtual mode (ph2.2).
/// Панель переключается на <see cref="ISearchResultSource"/> (виртуальный
/// IFileSystem), который отдаёт плоский список найденных файлов; обычные
/// операции (открыть / копировать / удалить / переименовать) работают по
/// реальным путям, так как FileSystemItem хранит полный путь файла.
/// The panel switches to an ISearchResultSource (virtual IFileSystem) that yields
/// the flat list of found files; open/copy/delete/rename operate on the real
/// paths because the FileSystemItem keeps the full file path.
/// </summary>
public partial class PanelViewModel
{
    /// <summary>Виртуальный источник (null в обычном режиме). / Virtual source (null in normal mode).</summary>
    [ObservableProperty] private IFileSystem? _virtualFileSystem;

    /// <summary>Путь к папке, из которой был вызван поиск (для возврата). / Folder to return to after leaving virtual mode.</summary>
    [ObservableProperty] private string _virtualReturnPath = "";

    /// <summary>Находится ли панель в виртуальном режиме результатов поиска. / Whether the panel is in virtual search-results mode.</summary>
    public bool IsVirtual => VirtualFileSystem is not null;

    partial void OnVirtualFileSystemChanged(IFileSystem? value)
        => OnPropertyChanged(nameof(IsVirtual));

    /// <summary>
    /// Переводит панель в виртуальный режим результатов поиска.
    /// Switches the panel into virtual search-results mode.
    /// </summary>
    public void EnterVirtualMode(IFileSystem source)
    {
        VirtualReturnPath = CurrentPath;
        VirtualFileSystem = source;
        // Служебный заголовок вместо реального пути (навигация заблокирована).
        // Sentinel title instead of a real path (navigation is blocked).
        CurrentPath = "Результаты поиска";
        _ = RefreshAsync();
    }

    /// <summary>
    /// Возвращает панель в обычный режим и открывает исходную папку.
    /// Returns the panel to normal mode and reopens the originating folder.
    /// </summary>
    public void ExitVirtualMode()
    {
        var ret = VirtualReturnPath;
        VirtualFileSystem = null;
        VirtualReturnPath = "";
        if (!string.IsNullOrEmpty(ret) && Directory.Exists(ret))
            _ = NavigateToAsync(ret);
        else
        {
            CurrentPath = ret ?? "C:\\";
            _ = RefreshAsync();
        }
    }

    /// <summary>
    /// Перечисление в виртуальном режиме: данные берутся из VirtualFileSystem,
    /// без обращения к реальной папке. Поддерживается фильтр папки и
    /// восстановление выделения.
    /// Virtual-mode enumeration: pulls from VirtualFileSystem without touching a
    /// real folder. Honors the folder filter and restores selection.
    /// </summary>
    private async Task RefreshVirtualAsync()
    {
        var selPath = SelectedItem?.FullPath;
        var selPaths = Items.Where(i => i.IsSelected && !i.IsParent).Select(i => i.FullPath).ToHashSet();
        Items.Clear();
        _itemsView = null;
        try
        {
            // Определяем внутренний путь для IFileSystem.
            // For cloud:// paths, extract the path after the profileId segment.
            var fsPath = CurrentPath;
            if (CurrentPath.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase))
            {
                fsPath = ExtractCloudPath(CurrentPath);
            }

            var entries = VirtualFileSystem is not null
                ? await VirtualFileSystem.EnumerateAsync(fsPath, ShowHidden, _cts.Token)
                : Enumerable.Empty<FileEntry>();
            System.Diagnostics.Debug.WriteLine($"[RefreshVirtual] fsPath={fsPath} entries={((System.Collections.IList)entries).Count} CurrentPath={CurrentPath}");
            foreach (var e in entries)
            {
                if (_cts.IsCancellationRequested) return;
                if (!string.IsNullOrEmpty(Filter) && !e.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)) continue;

                // Для облачных путей строим виртуальный полный путь.
                var fullPath = CurrentPath.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase)
                    ? CurrentPath.TrimEnd('/') + "/" + e.Name
                    : e.FullPath;
                System.Diagnostics.Debug.WriteLine($"[RefreshVirtual] e.Name={e.Name} e.FullPath={e.FullPath} → fullPath={fullPath}");

                var item = new FileSystemItem(fullPath, e.IsDirectory, e.Size, e.LastWriteTimeUtc.ToLocalTime());
                Items.Add(item);
                if (selPaths.Contains(item.FullPath)) item.IsSelected = true;
            }

            // Добавляем ".." для облачных путей (не в корне).
            if (CurrentPath.StartsWith("cloud://", StringComparison.OrdinalIgnoreCase))
            {
                var cloudPath = ExtractCloudPath(CurrentPath);
                if (cloudPath != "/")
                {
                    Items.Insert(0, new FileSystemItem(CurrentPath + "/..", isDirectory: true, isParent: true));
                }
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Ошибка виртуальной панели: {ex.Message}"); }

        if (selPath != null)
            SelectedItem = Items.FirstOrDefault(i => i.FullPath == selPath);
    }
}
