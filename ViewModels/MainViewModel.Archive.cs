using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Operations;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel главного окна: команды управления архивами (ph5.1, exp.yml).
/// Partial MainViewModel: archive management commands (ph5.1).
/// Точка интеграции: меню «Инструменты ▸ Создать архив / Извлечь из архива» (MainWindow.xaml)
/// и контекстное меню панели (FilePanel.xaml). Биндятся <c>PackCommand</c> / <c>ExtractCommand</c>.
/// Integration points: "Tools ▸ Create archive / Extract from archive" menu (MainWindow.xaml)
/// and panel context menu (FilePanel.xaml). Bind PackCommand / ExtractCommand.
/// Делегат <see cref="OpenArchiveRequest"/> для подключения в MainWindow.xaml.cs.
/// Delegate OpenArchiveRequest for MainWindow.xaml.cs hookup.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Запрос открытия окна архивов (делегируется View).
    /// Request to open archive window (delegated to View).
    /// </summary>
    public Action<string, IReadOnlyList<string>?, string?>? OpenArchiveRequest;

    /// <summary>
    /// Определяет, является ли файл архивом по расширению (ZIP, 7Z, RAR, TAR и т.д.).
    /// Determines whether a file is an archive by extension (ZIP, 7Z, RAR, TAR, etc.).
    /// </summary>
    private static bool IsArchiveFile(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        return ArchiveHelper.ArchiveExtensions.Contains(ext);
    }

    /// <summary>
    /// Создаёт архив из выделенных файлов и папок активной панели.
    /// Creates archive from selected files and folders in the active panel.
    /// Открывает окно <see cref="ArchiveWindow"/> в режиме создания.
    /// Opens ArchiveWindow in create mode.
    /// </summary>
    [RelayCommand]
    private void Pack()
    {
        var items = ActivePanel.Items
            .Where(i => i.IsSelected && !i.IsParent)
            .ToList();

        if (items.Count == 0)
        {
            StatusText = L10n("Archive.NoFiles");
            return;
        }

        var files = new List<string>();
        foreach (var item in items)
        {
            if (item.IsDirectory)
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(item.FullPath, "*", SearchOption.AllDirectories));
                }
                catch (Exception ex)
                {
                    LogService.Warn($"Failed to enumerate directory: {item.FullPath}: {ex.Message}",
                        nameof(MainViewModel));
                }
            }
            else
            {
                files.Add(item.FullPath);
            }
        }

        if (files.Count == 0)
        {
            StatusText = L10n("Archive.NoFiles");
            return;
        }

        OpenArchiveRequest?.Invoke("create", files, null);
    }

    /// <summary>
    /// Извлекает выделенный архив активной панели.
    /// Extracts the selected archive from the active panel.
    /// Если выделен архив (.zip/.7z/.tar/.gz/.rar/.bz2/.xz), открывает окно в режиме извлечения.
    /// If an archive is selected, opens the window in extract mode.
    /// </summary>
    [RelayCommand]
    private void Extract()
    {
        // Ищем первый выделенный файл, являющийся архивом.
        // Find the first selected file that is an archive.
        var archiveItem = ActivePanel.Items
            .FirstOrDefault(i => i.IsSelected && !i.IsParent && !i.IsDirectory && IsArchiveFile(i.FullPath));

        if (archiveItem is null)
        {
            StatusText = L10n("Archive.NoArchiveSelected");
            return;
        }

        OpenArchiveRequest?.Invoke("extract", null, archiveItem.FullPath);
    }
}
