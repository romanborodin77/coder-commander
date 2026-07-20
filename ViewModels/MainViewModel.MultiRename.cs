using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel: команда «Мульти-переименование» (ph2.3, exp.yml).
/// Partial ViewModel: the "Multi-rename" command (ph2.3).
/// Открывает <c>MultiRenameWindow</c> для выделенных элементов активной панели и,
/// при успехе, обновляет панель.
/// Opens <c>MultiRenameWindow</c> for the active panel's selected items and refreshes
/// the panel on success.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Мульти-переименование выделенных элементов активной панели.
    /// Multi-rename the selected items of the active panel.
    /// </summary>
    [RelayCommand]
    private async Task MultiRenameAsync()
    {
        // Выделение активной панели (без «..»); при пустой выделении — текущий элемент.
        // Active panel selection (no ".."); if empty, fall back to the current item.
        var items = ActivePanel.Items
            .Where(i => i.IsSelected && !i.IsParent)
            .ToList();
        if (items.Count == 0)
        {
            var cur = ActivePanel.SelectedItem;
            if (cur is not null && !cur.IsParent) items.Add(cur);
        }
        if (items.Count == 0) { StatusText = L10n("MultiRename.NoSelection"); return; }

        var files = items.Select(ToSourceFile).ToList();

        ActivePanel.SaveFocus();
        var win = new MultiRenameWindow(files) { Owner = Application.Current.MainWindow };
        if (win.ShowDialog() == true)
        {
            StatusText = string.Format(L10n("MultiRename.Applied"), files.Count);
            await ActivePanel.RefreshAsync().ConfigureAwait(false);
        }
        ActivePanel.RestoreFocus();
    }

    /// <summary>Строит <see cref="MultiRenameEngine.SourceFile"/> из <see cref="FileSystemItem"/>. / Builds a SourceFile from a FileSystemItem.</summary>
    private static MultiRenameEngine.SourceFile ToSourceFile(FileSystemItem item)
    {
        var nm = item.Name;
        var dotExt = Path.GetExtension(nm);                 // ".jpg" или "" / ".jpg" or ""
        var extNoDot = dotExt.Length > 0 ? dotExt.Substring(1) : "";
        var nameNoExt = dotExt.Length > 0 ? nm.Substring(0, nm.Length - dotExt.Length) : nm;
        return new MultiRenameEngine.SourceFile(
            item.FullPath,
            nm,
            nameNoExt,
            extNoDot,
            item.Modified,
            item.IsDirectory);
    }
}
