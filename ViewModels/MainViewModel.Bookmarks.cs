using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: управление закладками (ph5.3).
/// Partial ViewModel: bookmarks management (ph5.3).
/// Открывает <c>BookmarksWindow</c> для управления, навигирует по закладкам.
/// Opens <c>BookmarksWindow</c> for management, navigates to bookmarks.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Делегат открытия окна управления закладками (подключается в MainWindow.xaml.cs).
    /// Delegate for opening bookmarks management window (connected in MainWindow.xaml.cs).
    /// </summary>
    public Action? OpenBookmarksRequest;

    /// <summary>
    /// Команда: добавить текущую папку активной панели в закладки.
    /// Command: add active panel's current folder to bookmarks.
    /// </summary>
    [RelayCommand]
    private void AddBookmark()
    {
        var path = ActivePanel.CurrentPath;
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
            name = path;

        if (BookmarkService.Current.Add(name, path))
        {
            StatusText = string.Format(L10n("Bookmark.Added"), name);
        }
        else
        {
            StatusText = L10n("Bookmark.AlreadyExists");
        }
    }

    /// <summary>
    /// Команда: перейти к закладке по пути.
    /// Command: navigate to bookmark by path.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToBookmarkAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (System.IO.Directory.Exists(path))
            await ActivePanel.NavigateToAsync(path);
    }

    /// <summary>
    /// Команда: открыть окно управления закладками.
    /// Command: open bookmarks management window.
    /// </summary>
    [RelayCommand]
    private void ManageBookmarks()
    {
        OpenBookmarksRequest?.Invoke();
    }

    /// <summary>
    /// Команда: добавить или удалить закладку (горячая клавиша Ctrl+B).
    /// Если текущий путь уже в закладках — удаляет её. Иначе — добавляет.
    /// Command: add or remove bookmark (Ctrl+B hotkey).
    /// If current path is already bookmarked — removes it. Otherwise — adds it.
    /// </summary>
    [RelayCommand]
    private void ToggleBookmark()
    {
        var path = ActivePanel.CurrentPath;

        // Проверяем, есть ли уже закладка на этот путь — если да, удаляем.
        // Check if bookmark for this path already exists — if so, remove it.
        foreach (var bm in BookmarkService.Current.Bookmarks)
        {
            if (string.Equals(bm.Path.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'),
                System.StringComparison.OrdinalIgnoreCase))
            {
                BookmarkService.Current.Remove(bm);
                StatusText = L10n("Bookmark.Removed");
                return;
            }
        }

        // Добавляем.
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
            name = path;

        if (BookmarkService.Current.Add(name, path))
            StatusText = string.Format(L10n("Bookmark.Added"), name);
    }
}
