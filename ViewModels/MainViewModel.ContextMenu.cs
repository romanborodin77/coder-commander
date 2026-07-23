using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Частичная ViewModel главного окна: дополнительные команды контекстного меню панели (ph6.2).
/// Partial MainViewModel: extended file panel context menu commands (ph6.2).
/// </summary>
public partial class MainViewModel
{
    // ═══════════════════════════════════════════
    // КОНТЕКСТНОЕ МЕНЮ — КОПИРОВАНИЕ ПУТИ (ph6.2)
    // CONTEXT MENU — COPY PATH (ph6.2)
    // ═══════════════════════════════════════════

    /// <summary>Скопировать полный путь выделенного элемента в буфер обмена.</summary>
    [RelayCommand]
    private void CopyFullPath()
    {
        var item = ActivePanel.SelectedItem;
        if (item is null) return;
        // FIXED: Clipboard.SetText can throw ExternalException when clipboard is locked by another process.
        try { Clipboard.SetText(item.FullPath); StatusText = L10n("Ctx.PathCopied"); }
        catch (System.Runtime.InteropServices.ExternalException) { StatusText = L10n("Clipboard.Busy"); }
    }

    /// <summary>Скопировать имя файла в буфер обмена.</summary>
    [RelayCommand]
    private void CopyFileName()
    {
        var item = ActivePanel.SelectedItem;
        if (item is null) return;
        try { Clipboard.SetText(item.Name); StatusText = L10n("Ctx.NameCopied"); }
        catch (System.Runtime.InteropServices.ExternalException) { StatusText = L10n("Clipboard.Busy"); }
    }

    /// <summary>Скопировать путь без расширения в буфер обмена.</summary>
    [RelayCommand]
    private void CopyPathNoExtension()
    {
        var item = ActivePanel.SelectedItem;
        if (item is null) return;
        try { Clipboard.SetText(Path.ChangeExtension(item.FullPath, null)); StatusText = L10n("Ctx.PathCopied"); }
        catch (System.Runtime.InteropServices.ExternalException) { StatusText = L10n("Clipboard.Busy"); }
    }

    /// <summary>Скопировать имя файла без расширения в буфер обмена.</summary>
    [RelayCommand]
    private void CopyNameNoExtension()
    {
        var item = ActivePanel.SelectedItem;
        if (item is null) return;
        try { Clipboard.SetText(Path.GetFileNameWithoutExtension(item.FullPath)); StatusText = L10n("Ctx.NameCopied"); }
        catch (System.Runtime.InteropServices.ExternalException) { StatusText = L10n("Clipboard.Busy"); }
    }

    // ═══════════════════════════════════════════
    // КОНТЕКСТНОЕ МЕНЮ — ПРОВОДНИК / ТЕРМИНАЛ (ph6.2)
    // CONTEXT MENU — EXPLORER / TERMINAL (ph6.2)
    // ═══════════════════════════════════════════

    /// <summary>Открыть Windows Explorer с выделенным файлом/папкой.</summary>
    [RelayCommand]
    private void OpenInExplorer()
    {
        var item = ActivePanel.SelectedItem;
        var path = item?.FullPath ?? ActivePanel.CurrentPath;
        if (item is not null && !item.IsDirectory)
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else
            Process.Start("explorer.exe", $"\"{path}\"");
    }

    /// <summary>Открыть cmd.exe в текущей директории.</summary>
    [RelayCommand]
    private void OpenInTerminal()
    {
        var item = ActivePanel.SelectedItem;
        var dir = item?.IsDirectory == true ? item.FullPath : ActivePanel.CurrentPath;
        Process.Start("cmd.exe", $"/k cd /d \"{dir}\"");
    }

    // ═══════════════════════════════════════════
    // КОНТЕКСТНОЕ МЕНЮ — КОПИРОВАНИЕ/ПЕРЕМЕЩЕНИЕ В ДРУГУЮ ПАНЕЛЬ (ph6.2)
    // CONTEXT MENU — COPY/MOVE TO OTHER PANEL (ph6.2)
    // ═══════════════════════════════════════════

    /// <summary>Скопировать выделенные элементы в другую панель (с диалогом).</summary>
    [RelayCommand]
    private void CopyToOtherPanel()
    {
        var other = ActivePanel == LeftPanel ? RightPanel : LeftPanel;
        var selected = ActivePanel.GetSelectionOrCurrent().ToList();
        if (selected.Count == 0) return;
        ShowCopyMoveDialog(selected, other.CurrentPath, false);
    }

    /// <summary>Переместить выделенные элементы в другую панель (с диалогом).</summary>
    [RelayCommand]
    private void MoveToOtherPanel()
    {
        var other = ActivePanel == LeftPanel ? RightPanel : LeftPanel;
        var selected = ActivePanel.GetSelectionOrCurrent().ToList();
        if (selected.Count == 0) return;
        ShowCopyMoveDialog(selected, other.CurrentPath, true);
    }

    // ═══════════════════════════════════════════
    // КОНТЕКСТНОЕ МЕНЮ — КОНТРОЛЬНАЯ СУММА (ph6.2)
    // CONTEXT MENU — CHECKSUM (ph6.2)
    // ═══════════════════════════════════════════

    /// <summary>Вычислить SHA-256 для выделенных файлов и показать результат.</summary>
    [RelayCommand]
    private void ShowChecksum()
    {
        var selected = ActivePanel.GetSelectionOrCurrent()
            .Where(i => !i.IsDirectory).ToList();
        if (selected.Count == 0) return;
        var sb = new StringBuilder();
        foreach (var item in selected)
        {
            try
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(item.FullPath);
                var hash = sha.ComputeHash(stream);
                sb.AppendLine($"{Convert.ToHexString(hash)}  {item.Name}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"ERROR: {item.Name}: {ex.Message}");
            }
        }
        StyledMessageBoxWindow.Show(sb.ToString(), L10n("Ctx.Checksum"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ═══════════════════════════════════════════
    private static void CopyDirRecursive(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
