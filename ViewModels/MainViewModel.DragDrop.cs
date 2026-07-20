using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel главного окна: обработка перетаскивания файлов между панелями (ph6.3).
/// Partial MainViewModel: drag-and-drop file transfer between panels (ph6.3).
/// Shift = перемещение, Alt = создание ярлыка (symlink), Ctrl+Alt = hardlink, иначе = копирование.
/// Shift = move, Alt = create symlink, Ctrl+Alt = hardlink, otherwise = copy.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Обработать Drop файлов из другой панели или внешнего приложения (ph6.3).
    /// Handle file drop from another panel or external application (ph6.3).
    /// </summary>
    public Task DropFilesAsync(string[]? paths, string targetDir, bool isMove, string? sourceDir = null, bool isSymlink = false, bool isHardlink = false)
    {
        if (IsBusy || paths is null || paths.Length == 0) return Task.CompletedTask;
        if (!Directory.Exists(targetDir)) return Task.CompletedTask;

        var items = paths
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Select(p =>
            {
                long size = 0;
                try { if (File.Exists(p)) size = new FileInfo(p).Length; } catch { }
                return new FileSystemItem(p, Directory.Exists(p), size);
            })
            .ToList();

        if (items.Count == 0) return Task.CompletedTask;

        if (isSymlink || isHardlink)
        {
            ShowCreateLinkWindow(items, targetDir, isHardlink);
            return Task.CompletedTask;
        }

        var srcDir = sourceDir ?? (items.Count > 0 ? Path.GetDirectoryName(items[0].FullPath) ?? "" : "");
        ShowCopyMoveDialog(items, targetDir, isMove, srcDir);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Показать окно создания ссылки (CreateLinkWindow).
    /// Show the link creation window (CreateLinkWindow).
    /// </summary>
    private void ShowCreateLinkWindow(List<FileSystemItem> items, string targetDir, bool isHardlink)
    {
        CreateLinkWindow window;

        if (items.Count == 1)
        {
            var item = items[0];
            window = new CreateLinkWindow(item.FullPath, isHardlink, item.IsDirectory, targetDir);
        }
        else
        {
            var files = items.Select(i => (i.FullPath, i.IsDirectory)).ToList();
            window = new CreateLinkWindow(files, isHardlink, targetDir);
        }

        window.Owner = Application.Current.MainWindow;
        var result = window.ShowDialog();

        if (result == true)
        {
            _ = ActivePanel.RefreshAsync();
            _ = (ActivePanel == LeftPanel ? RightPanel : LeftPanel).RefreshAsync();
        }
    }
}
