using System;
using System.Windows;
using System.Windows.Input;
using CoderCommander.Services;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

/// <summary>
/// Модальное окно управления закладками (избранными папками). DragDrop для reorder, двойной клик — переход.
/// Modal bookmarks management window. Drag-drop reorder, double-click — navigate.
/// </summary>
public partial class BookmarksWindow : Window
{
    /// <summary>
    /// Создаёт окно управления закладками. / Creates bookmarks management window.
    /// </summary>
    public BookmarksWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is BookmarksViewModel vm)
                BookmarksList.Focus();
        };
    }

    /// <summary>
    /// Двойной клик: перейти к закладке и закрыть окно.
    /// Double-click: navigate to bookmark and close window.
    /// </summary>
    // FIXED: Added try/catch to prevent unhandled exception crash in async void event handler.
    private async void BookmarksList_DblClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (DataContext is BookmarksViewModel vm && vm.SelectedBookmark is not null)
            {
                await vm.NavigateToBookmarkAsync();
                DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Navigate to bookmark failed: {ex.Message}", nameof(BookmarksWindow), ex);
        }
    }

    /// <summary>
    /// Enter: перейти к закладке; Escape: закрыть окно.
    /// Enter: navigate to bookmark; Escape: close window.
    /// </summary>
    // FIXED: Added try/catch to prevent unhandled exception crash in async void event handler.
    private async void BookmarksList_KeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (DataContext is not BookmarksViewModel vm) return;

            if (e.Key == Key.Enter && vm.SelectedBookmark is not null)
            {
                await vm.NavigateToBookmarkAsync();
                DialogResult = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            LogService.Error($"Navigate to bookmark failed: {ex.Message}", nameof(BookmarksWindow), ex);
        }
    }

    /// <summary>
    /// Закрыть окно. / Close window.
    /// </summary>
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
