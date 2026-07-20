using CommunityToolkit.Mvvm.Input;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команда «Поиск дубликатов» (ph2.4, exp.yml).
/// Partial ViewModel: "Find duplicates" command (ph2.4).
/// Точка интеграции: пункт меню «Инструменты ▸ Поиск дубликатов» в MainWindow.xaml
/// биндится на <c>FindDuplicatesCommand</c>. Команда открывает <see cref="DuplicatesWindow"/>
/// для текущей папки активной панели через делегат <see cref="OpenDuplicatesRequest"/>.
/// Integration point: the "Tools ▸ Find duplicates" menu item in MainWindow.xaml binds to
/// FindDuplicatesCommand, which opens the DuplicatesWindow for the active panel's folder
/// via the OpenDuplicatesRequest delegate.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Запрос на открытие окна поиска дубликатов (делегируется View). / Request to open the duplicates window (delegated to the View).</summary>
    public Action<string>? OpenDuplicatesRequest;

    /// <summary>
    /// Открывает окно поиска дубликатов для текущей папки активной панели.
    /// Opens the duplicate search window for the active panel's current folder.
    /// </summary>
    [RelayCommand]
    private void FindDuplicates()
    {
        var path = ActivePanel.CurrentPath;
        OpenDuplicatesRequest?.Invoke(path);
    }
}
