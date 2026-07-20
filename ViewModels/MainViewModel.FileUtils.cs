using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Operations;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: утилиты файловой системы — безопасное удаление (Wipe, ph1.4)
/// и диалог свойств (ph1.5). Дополняет MainViewModel без перезаписи основного файла.
/// Partial ViewModel: file-system utilities — secure wipe (ph1.4) and the properties
/// dialog (ph1.5). Extends MainViewModel without rewriting the main file.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Число проходов перезаписи для Wipe (по умолчанию 3). / Wipe overwrite passes (default 3).</summary>
    [ObservableProperty] private int _wipePasses = 3;

    /// <summary>Статус выполнения Wipe для строки состояния. / Wipe status for the status bar.</summary>
    [ObservableProperty] private string _wipeStatus = "";

    /// <summary>
    /// Безопасное удаление выделенных элементов (DoD 5220.22-M): переименование →
    /// перезапись проходами → truncate → delete. Подтверждение, затем запуск операции.
    /// Secure-delete the selected items (DoD 5220.22-M): rename → overwrite passes →
    /// truncate → delete. Confirms first, then runs the operation.
    /// </summary>
    [RelayCommand]
    public async Task WipeAsync()
    {
        if (IsBusy) return;
        var it = ActivePanel.GetSelectionOrCurrent().ToList();
        if (it.Count == 0) return;
        if (StyledMessageBoxWindow.Show(
                string.Format(L10n("Wipe.Confirm"), it.Count),
                L10n("Wipe.Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        IsBusy = true; ProgressValue = 0; ProgressText = "Wipe: " + it[0].Name;
        using var cts = new CancellationTokenSource();
        try
        {
            var paths = it.Select(i => i.FullPath).ToList();
            var op = new WipeOperation(paths, null, WipePasses);
            await Task.Run(() => op.ExecuteAsync(cts.Token)).ConfigureAwait(true);
            WipeStatus = op.Failed == 0
                ? string.Format(L10n("Wipe.Done"), op.Wiped)
                : string.Format(L10n("Wipe.DoneErrors"), op.Wiped, op.Failed);
            StatusText = WipeStatus;
            await ActivePanel.RefreshAsync();
            await (ActivePanel == LeftPanel ? RightPanel : LeftPanel).RefreshAsync();
            await SyncActiveVirtualPanelAsync(); // ph2.2: вытертые выпадают из результатов
        }
        catch (System.OperationCanceledException) { StatusText = L10n("Wipe.Cancelled"); }
        finally { IsBusy = false; ProgressValue = 0; ProgressText = ""; }
    }

    /// <summary>
    /// Открывает диалог свойств для выбранного файла/папки (Alt+Enter). Для папок
    /// размер считается рекурсивно. Если ничего не выбрано — свойства текущей папки.
    /// Opens the properties dialog for the selected file/folder (Alt+Enter). For folders
    /// the size is computed recursively. With no selection, shows the panel's folder.
    /// </summary>
    [RelayCommand]
    public void ShowProperties()
    {
        var i = ActivePanel.SelectedItem;
        if (i is null || i.IsParent)
        {
            FilePropertiesWindow.ShowFor(ActivePanel.CurrentPath, true);
            return;
        }
        FilePropertiesWindow.ShowFor(i.FullPath, i.IsDirectory);
    }
}
