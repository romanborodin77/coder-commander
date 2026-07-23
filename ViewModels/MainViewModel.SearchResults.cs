using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Models;

namespace CoderCommander.ViewModels;

/// <summary>
/// Часть MainViewModel: подача результатов поиска в панель (ph2.2 / exp.yml).
/// Partial MainViewModel: feeding search results into a panel (ph2.2).
/// Команды: <see cref="ShowSearchResultsCommand"/> (показать результаты в
/// активной панели как виртуальный источник) и <see cref="BackToFolderCommand"/>
/// (вернуться к обычной папке). Точка интеграции — кнопка «Показать
/// результаты в панели» в диалоге поиска и пункт меню.
/// Commands: ShowSearchResultsCommand (show results in the active panel as a
/// virtual source) and BackToFolderCommand (return to the regular folder).
/// </summary>
public partial class MainViewModel
{
    /// <summary>Последние показанные результаты поиска (для повтора из меню). / Last shown results (for the menu re-trigger).</summary>
    private List<SearchResult>? _lastSearchResults;

    /// <summary>
    /// Показывает результаты поиска в активной панели как виртуальный источник.
    /// Shows the search results in the active panel as a virtual source.
    /// </summary>
    [RelayCommand]
    public void ShowSearchResults(IEnumerable<SearchResult>? results)
    {
        var list = results?.ToList() ?? new List<SearchResult>();
        if (list.Count == 0) { StatusText = L10n("Search.NoResults"); return; }
        _lastSearchResults = list;
        ActivePanel.EnterVirtualMode(new SearchResultSource(list));
        StatusText = string.Format(L10n("Search.ResultsFormat"), list.Count);
    }

    /// <summary>
    /// Повторно показывает последние результаты поиска (пункт меню).
    /// Re-shows the last search results (menu item).
    /// </summary>
    [RelayCommand]
    public void ShowLastSearchResults()
    {
        if (_lastSearchResults is null || _lastSearchResults.Count == 0)
        { StatusText = L10n("Search.NoSavedResults"); return; }
        ShowSearchResults(_lastSearchResults);
    }

    /// <summary>
    /// Возвращает активную панель из режима результатов поиска к обычной папке.
    /// Returns the active panel from search-results mode to the regular folder.
    /// </summary>
    [RelayCommand]
    public void BackToFolder() => ActivePanel.ExitVirtualMode();

    /// <summary>
    /// Синхронизирует виртуальную панель с реальной ФС после операций
    /// (удаление / перенос убирают файл из результатов; переименование меняет путь).
    /// Syncs the virtual panel with the real FS after operations (delete/move
    /// remove a file from results; rename changes its path).
    /// </summary>
    private async Task SyncActiveVirtualPanelAsync(string? renamedOld = null, string? renamedNew = null)
    {
        if (ActivePanel.VirtualFileSystem is ISearchResultSource s)
        {
            if (renamedOld != null && renamedNew != null)
                s.UpdatePath(renamedOld, renamedNew);
            else
                s.SyncWithFileSystem();
            await ActivePanel.RefreshAsync();
        }
    }
}
