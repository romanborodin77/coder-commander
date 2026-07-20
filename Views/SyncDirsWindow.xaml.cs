using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.Views;

/// <summary>
/// ViewModel окна синхронизации папок (ph3.3, exp.yml).
/// Folder synchronization window ViewModel (ph3.3).
/// Сравнивает две папки через <see cref="DirectorySyncEngine"/>, предлагает действия для каждой пары
/// и применяет их через CopyOperation/DeleteOperation с прогрессом и отменой.
/// Compares two folders via DirectorySyncEngine, suggests actions per pair, and applies them via
/// CopyOperation/DeleteOperation with progress and cancellation.
/// </summary>
public sealed partial class SyncDirsWindowViewModel : ObservableObject
{
    private readonly IFileSystem _fs = LocalFileSystem.Instance;
    private CancellationTokenSource? _cts;

    /// <summary>Левая папка (из левой панели). / Left folder (from the left panel).</summary>
    [ObservableProperty] private string _leftFolder = "";
    /// <summary>Правая папка (из правой панели). / Right folder (from the right panel).</summary>
    [ObservableProperty] private string _rightFolder = "";
    /// <summary>Маска имён (через «;»). / Name mask (separated by ";").</summary>
    [ObservableProperty] private string _mask = "";
    /// <summary>Включать ли подпапки. / Include subfolders.</summary>
    [ObservableProperty] private bool _includeSubfolders = true;
    /// <summary>Режим сравнения. / Comparison mode.</summary>
    [ObservableProperty] private SyncCompareMode _compareMode = SyncCompareMode.SizeMtime;
    /// <summary>Направление синхронизации по умолчанию. / Default sync direction.</summary>
    [ObservableProperty] private SyncDefaultDirection _direction = SyncDefaultDirection.CopyToNewer;
    /// <summary>Учитывать только выделенное в панелях. / Restrict to items selected in the panels.</summary>
    [ObservableProperty] private bool _onlySelected;
    /// <summary>
    /// Асимметричный режим: копировать только из левой панели, правые пропускать.
    /// Asymmetric mode: copy from left panel only, skip right-only files.
    /// </summary>
    [ObservableProperty] private bool _asymmetric;
    /// <summary>Идёт ли сравнение. / Whether a comparison is running.</summary>
    [ObservableProperty] private bool _isComparing;
    /// <summary>Идёт ли применение действий. / Whether actions are being applied.</summary>
    [ObservableProperty] private bool _isApplying;
    /// <summary>Прогресс (0-100). / Progress (0-100).</summary>
    [ObservableProperty] private int _progressPercent;
    /// <summary>Статус для UI. / Status text for UI.</summary>
    [ObservableProperty] private string _status = LocalizationService.Current.GetString("Sync.Ready");
    /// <summary>Число пар всего. / Total pair count.</summary>
    [ObservableProperty] private int _pairCount;
    /// <summary>Число пар, требующих действия. / Pairs requiring an action.</summary>
    [ObservableProperty] private int _actionCount;

    /// <summary>Список пар для отображения. / Pairs for display.</summary>
    public ObservableCollection<SyncPair> Pairs { get; } = new();

    /// <summary>Все возможные действия (для ComboBox в строках). / All possible actions (for row ComboBoxes).</summary>
    public IReadOnlyList<SyncAction> Actions =>
        Asymmetric
            ? new[] { SyncAction.None, SyncAction.Equal, SyncAction.CopyLeft, SyncAction.CopyRight }
            : System.Enum.GetValues<SyncAction>().ToList();

    /// <summary>Полные пути выделенных элементов в панелях (для фильтра «только выделенное»). / Selected item paths from the panels.</summary>
    private readonly IReadOnlyList<string>? _selectedPaths;

    /// <summary>Запрос на обновление панелей после применения (делегируется View). / Request to refresh panels after apply (delegated to View).</summary>
    public System.Action? RequestPanelsRefresh { get; set; }

    /// <summary>Создаёт VM для двух папок и выделенных путей. / Creates the VM for two folders and selected paths.</summary>
    public SyncDirsWindowViewModel(string leftFolder, string rightFolder, IReadOnlyList<string>? selectedPaths = null)
    {
        LeftFolder = leftFolder;
        RightFolder = rightFolder;
        _selectedPaths = selectedPaths;
    }

    private static string L10n(string key) => LocalizationService.Current.GetString(key);

    partial void OnIsComparingChanged(bool value) => CompareCommand.NotifyCanExecuteChanged();
    partial void OnIsApplyingChanged(bool value)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        CompareCommand.NotifyCanExecuteChanged();
    }
    partial void OnAsymmetricChanged(bool value) => OnPropertyChanged(nameof(Actions));

    private bool CanCompare() => !IsComparing && !IsApplying
        && Directory.Exists(LeftFolder) && Directory.Exists(RightFolder);

    /// <summary>
    /// Запускает сравнение двух папок: параллельное сканирование обеих сторон, сопоставление
    /// по относительному пути, сравнение (size+mtime или content). Результат — список пар.
    /// Runs the folder comparison: parallel scan of both sides, match by relative path,
    /// compare (size+mtime or content). Result is the list of pairs.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        Pairs.Clear();
        PairCount = 0;
        ActionCount = 0;
        ProgressPercent = 0;
        IsComparing = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var opt = new SyncOptions
        {
            Mode = CompareMode,
            Direction = Direction,
            IncludeSubfolders = IncludeSubfolders,
            Mask = string.IsNullOrWhiteSpace(Mask) ? null : Mask,
            SelectedPaths = OnlySelected ? _selectedPaths : null,
            Asymmetric = Asymmetric,
        };

        try
        {
            Status = L10n("Sync.Status.Scan");
            var pairs = await DirectorySyncEngine.CompareAsync(_fs, LeftFolder, RightFolder, opt,
                new Progress<double>(p => ProgressPercent = (int)p), ct);

            foreach (var p in pairs)
            {
                p.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SyncPair.Action)) NotifyActionChanged();
                };
                Pairs.Add(p);
            }
            PairCount = Pairs.Count;
            ActionCount = Pairs.Count(p => p.Apply && p.Action is not (SyncAction.Equal or SyncAction.None));
            Status = string.Format(L10n("Sync.Status.Found"), PairCount, ActionCount);
        }
        catch (OperationCanceledException)
        {
            Status = L10n("Sync.Status.Cancelled");
        }
        catch (Exception ex)
        {
            LogService.Error($"Sync compare failed: {ex.Message}", nameof(SyncDirsWindowViewModel), ex);
            Status = string.Format(L10n("Status.Error"), ex.Message);
        }
        finally
        {
            IsComparing = false;
        }
    }

    /// <summary>Отменяет текущее сравнение или применение. / Cancels the running compare or apply.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool CanApply() => !IsComparing && !IsApplying && Pairs.Any(p => p.Apply && p.Action is not (SyncAction.Equal or SyncAction.None));

    /// <summary>
    /// Применяет выбранные действия ко всем парам через DirectorySyncEngine.ApplyAsync
    /// (CopyOperation/DeleteOperation) с прогрессом и отменой. / Applies the chosen actions to all
    /// pairs via DirectorySyncEngine.ApplyAsync (CopyOperation/DeleteOperation) with progress and cancellation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsApplying = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        ProgressPercent = 0;

        try
        {
            Status = L10n("Sync.Status.Applying");
            var result = await DirectorySyncEngine.ApplyAsync(_fs, Pairs, LeftFolder, RightFolder,
                new Progress<SyncApplyProgress>(p =>
                {
                    ProgressPercent = p.Percent;
                    Status = string.Format(L10n("Sync.Status.ApplyingFile"), p.CurrentFile);
                }), ct);

            Status = string.Format(L10n("Sync.Status.Applied"), result.Succeeded, result.Failed);
            if (result.Failed > 0)
                LogService.Warn($"Sync apply failures: {result.Failed}", nameof(SyncDirsWindowViewModel));

            // Обновляем панели и пересравниваем, чтобы отразить изменения.
            // Refresh panels and re-compare to reflect the changes.
            RequestPanelsRefresh?.Invoke();
            await CompareAsync();
        }
        catch (OperationCanceledException)
        {
            Status = L10n("Sync.Status.Cancelled");
        }
        catch (Exception ex)
        {
            LogService.Error($"Sync apply failed: {ex.Message}", nameof(SyncDirsWindowViewModel), ex);
            Status = string.Format(L10n("Status.Error"), ex.Message);
        }
        finally
        {
            IsApplying = false;
        }
    }

    /// <summary>Отмечает все пары как включённые в применение. / Marks all pairs as included.</summary>
    [RelayCommand]
    private void SelectAll() { foreach (var p in Pairs) p.Apply = true; UpdateCounts(); ApplyCommand.NotifyCanExecuteChanged(); }

    /// <summary>Снимает отметку со всех пар. / Clears inclusion marks from all pairs.</summary>
    [RelayCommand]
    private void SelectNone() { foreach (var p in Pairs) p.Apply = false; UpdateCounts(); ApplyCommand.NotifyCanExecuteChanged(); }

    /// <summary>Для всех отмеченных пар устанавливает действие «копировать в левую». / Sets Copy-to-left for all marked pairs.</summary>
    [RelayCommand]
    private void SetCopyLeft() { foreach (var p in Pairs.Where(p => p.Apply)) p.Action = SyncAction.CopyLeft; }

    /// <summary>Для всех отмеченных пар устанавливает действие «копировать в правую». / Sets Copy-to-right for all marked pairs.</summary>
    [RelayCommand]
    private void SetCopyRight() { foreach (var p in Pairs.Where(p => p.Apply)) p.Action = SyncAction.CopyRight; }

    /// <summary>Для всех отмеченных пар устанавливает действие «удалить на обеих». / Sets Delete-both for all marked pairs.</summary>
    [RelayCommand]
    private void SetDeleteBoth() { foreach (var p in Pairs.Where(p => p.Apply)) p.Action = SyncAction.DeleteBoth; }

    /// <summary>Для всех отмеченных пар устанавливает действие «без действия». / Sets None for all marked pairs.</summary>
    [RelayCommand]
    private void SetNone() { foreach (var p in Pairs.Where(p => p.Apply)) p.Action = SyncAction.None; }

    /// <summary>Пересчитывает число пар, требующих действия. / Recomputes the count of pairs requiring an action.</summary>
    private void UpdateCounts() =>
        ActionCount = Pairs.Count(p => p.Apply && p.Action is not (SyncAction.Equal or SyncAction.None));

    /// <summary>Вызывается при изменении действия в строке — обновляет счётчик. / Called when a row action changes — updates the counter.</summary>
    public void NotifyActionChanged() { UpdateCounts(); ApplyCommand.NotifyCanExecuteChanged(); }
}

/// <summary>
/// Окно синхронизации папок (ph3.3, exp.yml). Модальное, с кастомным заголовком (WindowStyle=None).
/// Folder synchronization window (ph3.3). Modal, with a custom title bar (WindowStyle=None).
/// Связывается с <see cref="SyncDirsWindowViewModel"/> через DataContext.
/// Binds to SyncDirsWindowViewModel via DataContext.
/// </summary>
public sealed partial class SyncDirsWindow : Window
{
    private readonly SyncDirsWindowViewModel _vm;

    /// <summary>
    /// Создаёт окно для двух папок, выделенных путей и колбэка обновления панелей.
    /// Creates the window for two folders, selected paths and a panel-refresh callback.
    /// </summary>
    public SyncDirsWindow(string leftFolder, string rightFolder, IReadOnlyList<string>? selectedPaths = null, System.Action? refreshCallback = null)
    {
        InitializeComponent();
        _vm = new SyncDirsWindowViewModel(leftFolder, rightFolder, selectedPaths)
        {
            RequestPanelsRefresh = refreshCallback
        };
        DataContext = _vm;
    }

    #region Заголовок окна / Window chrome handlers
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else DragMove();
        }
    }
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    #endregion

    /// <summary>
    /// Открывает DiffWindow для сравнения левого и правого файлов при двойном клике по строке.
    /// Opens DiffWindow to compare left and right files on double-click.
    /// </summary>
    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid dg) return;
        if (dg.SelectedItem is not SyncPair pair) return;
        if (pair.LeftPath is null || pair.RightPath is null) return;
        var dlg = new DiffWindow(pair.LeftPath, pair.RightPath) { Owner = this };
        dlg.ShowDialog();
    }
}
