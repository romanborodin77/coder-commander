using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команда «Синхронизация папок» (ph3.3, exp.yml).
/// Partial ViewModel: "Synchronize folders" command (ph3.3).
/// Точка интеграции: пункт меню «Команды ▸ Синхронизация папок…» в MainWindow.xaml биндится на
/// <c>SyncDirsCommand</c>. Команда открывает <see cref="SyncDirsWindow"/>, сравнивая левую и правую
/// панели; выделенные элементы обеих панелей передаются как фильтр «только выделенное».
/// Integration point: the "Commands ▸ Synchronize folders…" menu item in MainWindow.xaml binds to
/// SyncDirsCommand, which opens SyncDirsWindow comparing the left and right panels; the selected
/// items from both panels are passed as the "only selected" filter.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Запрос на открытие окна синхронизации папок (делегируется View). / Request to open the folder sync window (delegated to the View).</summary>
    public System.Action<string, string, IReadOnlyList<string>?>? OpenSyncDirsRequest;

    /// <summary>
    /// Открывает окно синхронизации папок для левой и правой панелей.
    /// Opens the folder sync window for the left and right panels.
    /// </summary>
    [RelayCommand]
    private void SyncDirs()
    {
        var left = LeftPanel.CurrentPath;
        var right = RightPanel.CurrentPath;
        var selected = LeftPanel.Items.Where(i => i.IsSelected && !i.IsParent)
                          .Select(i => i.FullPath)
                          .Concat(RightPanel.Items.Where(i => i.IsSelected && !i.IsParent).Select(i => i.FullPath))
                          .Distinct()
                          .ToList();
        OpenSyncDirsRequest?.Invoke(left, right, selected.Count > 0 ? selected : null);
    }
}
