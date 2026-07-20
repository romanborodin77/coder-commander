using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel диалога настройки колонок: два списка (доступные/активные), перемещение, порядок, ширина.
/// ViewModel for column settings dialog: two lists (available/active), move, reorder, width.
/// </summary>
public partial class ColumnSettingsViewModel : ObservableObject
{
    /// <summary>
    /// Доступные колонки (ещё не добавленные в активные).
    /// Available columns (not yet added to active).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ColumnDefinition> _availableColumns = new();

    /// <summary>
    /// Активные колонки (отображаемые в панели).
    /// Active columns (displayed in panel).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<ColumnDefinition> _activeColumns = new();

    /// <summary>
    /// Выбранная доступная колонка.
    /// Selected available column.
    /// </summary>
    [ObservableProperty]
    private ColumnDefinition? _selectedAvailable;

    /// <summary>
    /// Выбранная активная колонка.
    /// Selected active column.
    /// </summary>
    [ObservableProperty]
    private ColumnDefinition? _selectedActive;

    /// <summary>
    /// Создаёт экземпляр ColumnSettingsViewModel, загружая текущую конфигурацию.
    /// Creates ColumnSettingsViewModel instance, loading current configuration.
    /// </summary>
    public ColumnSettingsViewModel()
    {
        RefreshLists();
    }

    /// <summary>
    /// Обновляет списки доступных/активных колонок из сервиса.
    /// Refreshes available/active column lists from service.
    /// </summary>
    private void RefreshLists()
    {
        AvailableColumns.Clear();
        ActiveColumns.Clear();

        var activeKeys = new HashSet<string>(ColumnConfigService.ActiveColumns.Select(c => c.Key));

        foreach (var col in ColumnDefinition.AllColumns)
        {
            if (activeKeys.Contains(col.Key))
            {
                var existing = ColumnConfigService.ActiveColumns.First(c => c.Key == col.Key);
                ActiveColumns.Add(existing.Clone());
            }
            else
            {
                AvailableColumns.Add(col.Clone());
            }
        }
    }

    /// <summary>
    /// Перемещает выбранную доступную колонку в активные.
    /// Moves selected available column to active.
    /// </summary>
    [RelayCommand]
    private void Add()
    {
        if (SelectedAvailable is null) return;
        var col = SelectedAvailable.Clone();
        col.IsVisible = true;
        ActiveColumns.Add(col);
        AvailableColumns.Remove(SelectedAvailable);
        SelectedAvailable = AvailableColumns.FirstOrDefault();
    }

    /// <summary>
    /// Перемещает выбранную активную колонку в доступные (если не обязательная).
    /// Moves selected active column to available (if not required).
    /// </summary>
    [RelayCommand]
    private void Remove()
    {
        if (SelectedActive is null || SelectedActive.IsRequired) return;
        AvailableColumns.Add(SelectedActive.Clone());
        ActiveColumns.Remove(SelectedActive);
        SelectedActive = ActiveColumns.FirstOrDefault();
    }

    /// <summary>
    /// Перемещает выбранную активную колонку вверх по порядку.
    /// Moves selected active column up in order.
    /// </summary>
    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedActive is null) return;
        var idx = ActiveColumns.IndexOf(SelectedActive);
        if (idx <= 0) return; // Name всегда первый
        ActiveColumns.Move(idx, idx - 1);
    }

    /// <summary>
    /// Перемещает выбранную активную колонку вниз по порядку.
    /// Moves selected active column down in order.
    /// </summary>
    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedActive is null) return;
        var idx = ActiveColumns.IndexOf(SelectedActive);
        if (idx < 0 || idx >= ActiveColumns.Count - 1) return;
        ActiveColumns.Move(idx, idx + 1);
    }

    /// <summary>
    /// Сбрасывает колонки к умолчанию.
    /// Resets columns to default.
    /// </summary>
    [RelayCommand]
    private void Reset()
    {
        ColumnConfigService.ResetToDefault();
        RefreshLists();
    }

    /// <summary>
    /// Применяет изменения и сохраняет конфигурацию.
    /// Applies changes and saves configuration.
    /// </summary>
    [RelayCommand]
    private void Apply()
    {
        ColumnConfigService.ActiveColumns.Clear();
        foreach (var col in ActiveColumns)
            ColumnConfigService.ActiveColumns.Add(col);
        ColumnConfigService.Save();
        ColumnConfigService.RaiseColumnsChanged();
    }
}
