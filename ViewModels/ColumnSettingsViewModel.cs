using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel диалога настройки колонок: два списка (доступные/активные), перемещение, порядок, ширина, сортировка.
/// ViewModel for column settings dialog: two lists (available/active), move, reorder, width, sorting.
/// </summary>
public partial class ColumnSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<ColumnDefinition> _availableColumns = new();

    [ObservableProperty]
    private ObservableCollection<ColumnDefinition> _activeColumns = new();

    [ObservableProperty]
    private ColumnDefinition? _selectedAvailable;

    [ObservableProperty]
    private ColumnDefinition? _selectedActive;

    /// <summary>
    /// Ключ колонки, выбранной для сортировки в этом диалоге.
    /// Column key selected for sorting in this dialog.
    /// </summary>
    [ObservableProperty]
    private string _selectedSortKey = "";

    /// <summary>
    /// Направление сортировки, выбранное в этом диалоге.
    /// Sort direction selected in this dialog.
    /// </summary>
    [ObservableProperty]
    private SortDirectionOption? _selectedSortDirection;

    /// <summary>
    /// Направление сортировки (для совместимости с Apply).
    /// Sort direction (for Apply compatibility).
    /// </summary>
    public ListSortDirection SortDirection
    {
        get => SelectedSortDirection?.Direction ?? ListSortDirection.Ascending;
        set => SelectedSortDirection = SortDirections.FirstOrDefault(d => d.Direction == value);
    }

    /// <summary>
    /// Доступные варианты направления сортировки для ComboBox (обёртки с локализацией).
    /// Available sort direction options for ComboBox (localized wrappers).
    /// </summary>
    public List<SortDirectionOption> SortDirections { get; } = new()
    {
        new(ListSortDirection.Ascending),
        new(ListSortDirection.Descending)
    };

    /// <summary>Локализованное название для направления сортировки.</summary>
    public static string SortDirDisplay(ListSortDirection dir) =>
        dir == ListSortDirection.Ascending
            ? LocalizationService.Current.GetString("Columns.SortAscending")
            : LocalizationService.Current.GetString("Columns.SortDescending");

    /// <summary>
    /// Обёртка направления сортировки для отображения в ComboBox.
    /// Wrapper for sort direction to display in ComboBox.
    /// </summary>
    public class SortDirectionOption
    {
        public ListSortDirection Direction { get; }
        public string Display => SortDirDisplay(Direction);

        public SortDirectionOption(ListSortDirection dir)
        {
            Direction = dir;
        }

        public override string ToString() => Display;
    }

    /// <summary>
    /// Создаёт экземпляр ColumnSettingsViewModel, загружая текущую конфигурацию.
    /// Creates ColumnSettingsViewModel instance, loading current configuration.
    /// </summary>
    public ColumnSettingsViewModel()
    {
        RefreshLists();
    }

    private void RefreshLists()
    {
        AvailableColumns.Clear();
        ActiveColumns.Clear();

        var activeKeys = new HashSet<string>(ColumnConfigService.ActiveColumns.Select(c => c.Key));

        // Build a lookup of localized headers from AllColumns
        var localizedHeaders = ColumnDefinition.AllColumns.ToDictionary(c => c.Key, c => c.Header);

        foreach (var col in ColumnDefinition.AllColumns)
        {
            if (activeKeys.Contains(col.Key))
            {
                var existing = ColumnConfigService.ActiveColumns.First(c => c.Key == col.Key);
                var cloned = existing.Clone();
                // Force localized header
                if (localizedHeaders.TryGetValue(cloned.Key, out var localizedHeader))
                    cloned.Header = localizedHeader;
                ActiveColumns.Add(cloned);
            }
            else
            {
                AvailableColumns.Add(col.Clone());
            }
        }

        // Восстанавливаем сохранённую сортировку
        var sortedKey = ColumnConfigService.SortedColumnKey;
        if (!string.IsNullOrEmpty(sortedKey) && activeKeys.Contains(sortedKey))
        {
            SelectedSortKey = sortedKey;
            var sortCol = ColumnConfigService.ActiveColumns.FirstOrDefault(c => c.Key == sortedKey);
            if (sortCol is not null)
                SortDirection = sortCol.SortDirection;
        }
    }

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

    [RelayCommand]
    private void Remove()
    {
        if (SelectedActive is null || SelectedActive.IsRequired) return;
        AvailableColumns.Add(SelectedActive.Clone());
        ActiveColumns.Remove(SelectedActive);
        SelectedActive = ActiveColumns.FirstOrDefault();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedActive is null) return;
        var idx = ActiveColumns.IndexOf(SelectedActive);
        if (idx <= 0) return;
        ActiveColumns.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedActive is null) return;
        var idx = ActiveColumns.IndexOf(SelectedActive);
        if (idx < 0 || idx >= ActiveColumns.Count - 1) return;
        ActiveColumns.Move(idx, idx + 1);
    }

    [RelayCommand]
    private void Reset()
    {
        ColumnConfigService.ResetToDefault();
        RefreshLists();
    }

    [RelayCommand]
    private void Apply()
    {
        ColumnConfigService.ActiveColumns.Clear();
        foreach (var col in ActiveColumns)
            ColumnConfigService.ActiveColumns.Add(col);

        // Сохраняем выбранную сортировку
        if (!string.IsNullOrEmpty(SelectedSortKey) &&
            ActiveColumns.Any(c => c.Key == SelectedSortKey))
        {
            ColumnConfigService.SortedColumnKey = SelectedSortKey;
            var sortCol = ColumnConfigService.ActiveColumns.First(c => c.Key == SelectedSortKey);
            sortCol.SortDirection = SortDirection;
        }

        ColumnConfigService.Save();
        ColumnConfigService.RaiseColumnsChanged();
    }
}
