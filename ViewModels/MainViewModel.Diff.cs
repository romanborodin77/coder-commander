using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel главного окна: команда сравнения текстовых файлов (ph3.1).
/// Partial MainViewModel: file comparison command (ph3.1).
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Сравнить два файла в окне DiffWindow. Берёт два выделенных файла активной панели,
    /// либо текущий файл активной панели и файл из другой панели (по имени или выделению).
    /// Compare two files in the DiffWindow. Uses two selected files of the active panel,
    /// or the current file of the active panel and a file from the other panel (by name or selection).
    /// </summary>
    [RelayCommand]
    public void CompareFiles()
    {
        var (left, right) = ResolveDiffPair();
        if (left is null || right is null)
        {
            StatusText = L10n("Diff.SelectTwoFiles");
            return;
        }
        if (left.IsDirectory || right.IsDirectory)
        {
            StatusText = L10n("Diff.DirCompareNotSupported");
            return;
        }

        try
        {
            var w = new DiffWindow(left.FullPath, right.FullPath)
            {
                Owner = Application.Current.MainWindow
            };
            ActivePanel.SaveFocus();
            w.Closed += (_, _) => ActivePanel.RestoreFocus();
            w.Show();
            StatusText = string.Format(L10n("Diff.Comparing"), left.Name, right.Name);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n("Diff.CompareError"), ex.Message);
        }
    }

    /// <summary>
    /// Определяет пару файлов для сравнения согласно правилам выделения панелей.
    /// Resolves the file pair for comparison according to panel selection rules.
    /// </summary>
    private (FileSystemItem? left, FileSystemItem? right) ResolveDiffPair()
    {
        var other = ActivePanel == LeftPanel ? RightPanel : LeftPanel;
        var activeFiles = ActivePanel.GetSelectionOrCurrent()
            .Where(i => !i.IsParent && !i.IsDirectory)
            .ToList();

        // 1) Два выделенных файла в активной панели
        if (activeFiles.Count >= 2)
            return (activeFiles[0], activeFiles[1]);

        // 2) Один файл в активной панели — ищем пару в другой панели
        if (activeFiles.Count == 1)
        {
            var a = activeFiles[0];

            // 2a) Выделенный файл в другой панели
            var otherFiles = other.GetSelectionOrCurrent()
                .Where(i => !i.IsParent && !i.IsDirectory)
                .ToList();
            if (otherFiles.Count >= 1)
                return (a, otherFiles[0]);

            // 2b) Совпадение по имени в другой панели
            var byName = other.Items
                .FirstOrDefault(i => !i.IsParent && !i.IsDirectory && i.Name == a.Name);
            if (byName is not null)
                return (a, byName);

            // 2c) Первый файл другой панели
            var firstOther = other.Items
                .FirstOrDefault(i => !i.IsParent && !i.IsDirectory);
            if (firstOther is not null)
                return (a, firstOther);
        }

        return (null, null);
    }
}
