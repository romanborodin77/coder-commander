using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CoderCommander.ViewModels;

/// <summary>
/// Часть PanelViewModel: быстрый просмотр (QuickView) файлов в панели (ph5.5, exp.yml).
/// Partial PanelViewModel: Quick View file preview in panel (ph5.5).
/// Управляет видимостью панели QuickView и обновлением предпросмотра при смене выделения
/// с debounce ~200 мс (чтобы не нагружать систему при быстрой навигации стрелками).
/// Controls QuickView panel visibility and preview updates on selection change
/// with ~200ms debounce (to avoid overloading the system during rapid arrow navigation).
/// </summary>
public partial class PanelViewModel
{
    /// <summary>
    /// Флаг видимости панели быстрого просмотра (справа от списка файлов).
    /// Flag for quick view panel visibility (right of the file list).
    /// </summary>
    [ObservableProperty]
    private bool _isQuickViewOpen;

    /// <summary>
    /// Путь к текущему предпросматриваемому файлу.
    /// Path to the currently previewed file.
    /// </summary>
    [ObservableProperty]
    private string? _quickViewFilePath;

    private CancellationTokenSource? _quickViewCts;

    /// <summary>
    /// Переключает видимость панели быстрого просмотра (F11).
    /// Toggles quick view panel visibility (F11).
    /// </summary>
    [RelayCommand]
    private void ToggleQuickView()
    {
        IsQuickViewOpen = !IsQuickViewOpen;

        if (IsQuickViewOpen)
        {
            // Обновляем предпросмотр для текущего выделения
            UpdateQuickViewForSelection();
        }
        else
        {
            QuickViewFilePath = null;
        }
    }

    /// <summary>
    /// При смене выделенного элемента обновляем предпросмотр QuickView (debounce 200 мс).
    /// On selected item change, update QuickView preview (200ms debounce).
    /// </summary>
    partial void OnSelectedItemChanged(FileSystemItem? value)
    {
        if (!IsQuickViewOpen) return;

        _quickViewCts?.Cancel();
        _quickViewCts?.Dispose();
        _quickViewCts = new CancellationTokenSource();
        var ct = _quickViewCts.Token;

        _ = UpdateQuickViewDebouncedAsync(ct);
    }

    /// <summary>
    /// Debounce-обновление предпросмотра QuickView. / Debounced QuickView preview update.
    /// </summary>
    private async Task UpdateQuickViewDebouncedAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(200, ct);
            if (!ct.IsCancellationRequested)
            {
                UpdateQuickViewForSelection();
            }
        }
        catch (TaskCanceledException) { }
    }

    /// <summary>
    /// Устанавливает путь предпросмотра для текущего выделенного элемента.
    /// Sets the preview path for the currently selected item.
    /// </summary>
    private void UpdateQuickViewForSelection()
    {
        if (SelectedItem is not null && !SelectedItem.IsParent && !SelectedItem.IsDirectory)
        {
            QuickViewFilePath = SelectedItem.FullPath;
        }
        else
        {
            QuickViewFilePath = null;
        }
    }
}
