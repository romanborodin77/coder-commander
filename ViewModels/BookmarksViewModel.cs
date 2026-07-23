using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Views;
using Microsoft.Win32;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel для управления закладками: добавление, удаление, навигация.
/// ViewModel for bookmark management: add, remove, navigate.
/// </summary>
public partial class BookmarksViewModel : ObservableObject
{
    private readonly BookmarkService _service = BookmarkService.Current;

    public BookmarksViewModel()
    {
        _service.Bookmarks.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(BookmarksCountDisplay));
    }

    /// <summary>Коллекция закладок для привязки к UI. / Bookmarks collection for UI binding.</summary>
    public ObservableCollection<BookmarkItem> Bookmarks => _service.Bookmarks;

    /// <summary>
    /// Отображаемое число закладок (форматированная строка).
    /// Display bookmark count (formatted string).
    /// </summary>
    public string BookmarksCountDisplay =>
        string.Format(LocalizationService.Current.GetString("Bookmark.Count"), Bookmarks.Count);

    /// <summary>Выделенная закладка. / Selected bookmark.</summary>
    [ObservableProperty]
    private BookmarkItem? _selectedBookmark;

    /// <summary>
    /// Команда: добавить текущую папку активной панели в закладки.
    /// Command: add active panel's current folder to bookmarks.
    /// </summary>
    [RelayCommand]
    private void AddBookmark()
    {
        // Путь берётся из активной панели (через MainWindow.DataContext).
        // Path taken from active panel (via MainWindow.DataContext).
        if (Application.Current.MainWindow?.DataContext is not MainViewModel vm)
            return;

        var path = vm.ActivePanel.CurrentPath;
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
            name = path; // Корень диска — полный путь.

        if (!_service.Add(name, path))
        {
            vm.StatusText = LocalizationService.Current.GetString("Bookmark.AlreadyExists");
        }
    }

    /// <summary>
    /// Команда: удалить выделенную закладку.
    /// Command: remove the selected bookmark.
    /// </summary>
    [RelayCommand]
    private void RemoveBookmark()
    {
        if (SelectedBookmark is null) return;
        _service.Remove(SelectedBookmark);
        SelectedBookmark = null;
    }

    /// <summary>
    /// Команда: переименовать выделенную закладку (через диалог).
    /// Command: rename selected bookmark (via dialog).
    /// </summary>
    [RelayCommand]
    private void RenameBookmark()
    {
        if (SelectedBookmark is null) return;

        var newName = Prompt(
            LocalizationService.Current.GetString("Bookmark.Rename"),
            LocalizationService.Current.GetString("Bookmark.Name"),
            SelectedBookmark.Name);

        if (!string.IsNullOrWhiteSpace(newName))
            _service.Rename(SelectedBookmark, newName);
    }

    /// <summary>
    /// Команда: перейти к выделенной закладке (переключить панель).
    /// Command: navigate to selected bookmark (switch panel).
    /// </summary>
    [RelayCommand]
    public async Task NavigateToBookmarkAsync()
    {
        if (SelectedBookmark is null) return;
        if (Application.Current.MainWindow?.DataContext is not MainViewModel vm) return;
        if (System.IO.Directory.Exists(SelectedBookmark.Path))
            await vm.ActivePanel.NavigateToAsync(SelectedBookmark.Path);
    }

    /// <summary>
    /// Команда: перейти к закладке по пути.
    /// Command: navigate to bookmark by path.
    /// </summary>
    [RelayCommand]
    private async Task NavigateToPathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (Application.Current.MainWindow?.DataContext is not MainViewModel vm) return;
        if (System.IO.Directory.Exists(path))
            await vm.ActivePanel.NavigateToAsync(path);
    }

    /// <summary>
    /// Команда: экспорт закладок в JSON-файл.
    /// Command: export bookmarks to a JSON file.
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            DefaultExt = ".json",
            FileName = "bookmarks.json"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            await _service.ExportAsync(dlg.FileName);
            if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
                vm.StatusText = string.Format(
                    LocalizationService.Current.GetString("Bookmark.ExportDone"),
                    Bookmarks.Count);
        }
        catch (Exception ex)
        {
            StyledMessageBoxWindow.Show(ex.Message, LocalizationService.Current.GetString("Error.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Команда: импорт закладок из JSON-файла с дедупликацией.
    /// Command: import bookmarks from a JSON file with deduplication.
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json",
            DefaultExt = ".json"
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            var (added, skipped) = await _service.ImportAsync(dlg.FileName);
            if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
                vm.StatusText = string.Format(
                    LocalizationService.Current.GetString("Bookmark.ImportResult"),
                    added, skipped);
        }
        catch (Exception ex)
        {
            StyledMessageBoxWindow.Show(ex.Message, LocalizationService.Current.GetString("Error.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Команда: переместить выделенную закладку вверх.
    /// Command: move selected bookmark up.
    /// </summary>
    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedBookmark is null) return;
        var idx = Bookmarks.IndexOf(SelectedBookmark);
        if (idx > 0)
        {
            _service.Reorder(idx, idx - 1);
            SelectedBookmark = Bookmarks[idx - 1];
        }
    }

    /// <summary>
    /// Команда: переместить выделенную закладку вниз.
    /// Command: move selected bookmark down.
    /// </summary>
    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedBookmark is null) return;
        var idx = Bookmarks.IndexOf(SelectedBookmark);
        if (idx >= 0 && idx < Bookmarks.Count - 1)
        {
            _service.Reorder(idx, idx + 1);
            SelectedBookmark = Bookmarks[idx + 1];
        }
    }

    /// <summary>
    /// Диалог ввода строки (InputBox). Возвращает текст или null при отмене.
    /// String input dialog (InputBox). Returns text or null on cancel.
    /// </summary>
    private static string? Prompt(string title, string prompt, string def = "")
    {
        var w = new Window
        {
            Title = title, Width = 400, Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            Owner = Application.Current.MainWindow,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["BgPanelBrush"]
        };

        var root = new System.Windows.Controls.DockPanel();

        var titleBar = new System.Windows.Controls.Border
        {
            Height = 36, Background = (System.Windows.Media.Brush)Application.Current.Resources["TitleBarBgBrush"]
        };
        System.Windows.Controls.DockPanel.SetDock(titleBar, System.Windows.Controls.Dock.Top);
        var titleSp = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleSp.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = title,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["FgLightBrush"],
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        titleBar.Child = titleSp;
        titleBar.MouseLeftButtonDown += (_, _) => w.DragMove();
        root.Children.Add(titleBar);

        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(15, 10, 15, 10) };
        sp.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["FgLightBrush"],
            Margin = new Thickness(0, 0, 0, 8)
        });
        var tb = new System.Windows.Controls.TextBox
        {
            Text = def,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["BgInputBrush"],
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["FgLightBrush"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"]
        };
        sp.Children.Add(tb);

        var btns = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new System.Windows.Controls.Button
        {
            Content = LocalizationService.Current.GetString("MsgBox.OK"),
            Width = 80, IsDefault = true,
            Style = (System.Windows.Style)Application.Current.Resources["AccentButtonStyle"]
        };
        var cn = new System.Windows.Controls.Button
        {
            Content = LocalizationService.Current.GetString("Dialog.Cancel"),
            Width = 80, IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0),
            Style = (System.Windows.Style)Application.Current.Resources["DefaultButtonStyle"]
        };
        ok.Click += (_, _) => w.DialogResult = true;
        cn.Click += (_, _) => w.DialogResult = false;
        btns.Children.Add(ok);
        btns.Children.Add(cn);
        sp.Children.Add(btns);

        root.Children.Add(sp);
        w.Content = root;
        tb.SelectAll();
        tb.Focus();
        return w.ShowDialog() == true ? tb.Text : null;
    }
}
