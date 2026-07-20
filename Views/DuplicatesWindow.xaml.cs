using System.Collections.Generic;
using System.Collections.ObjectModel;
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
/// Критерий поиска дубликатов (ph2.4, exp.yml section «Поиск дубликатов»).
/// Duplicate search criterion (ph2.4): size / size+name / hash / content.
/// </summary>
public enum DuplicateCriterion
{
    /// <summary>Группировка только по размеру. / Group by size only.</summary>
    Size,
    /// <summary>Группировка по размеру и имени. / Group by size and name.</summary>
    SizeAndName,
    /// <summary>Группировка по SHA-256 хешу (после фильтрации по размеру). / Group by SHA-256 hash.</summary>
    Hash,
    /// <summary>Хеш + побайтовое подтверждение (FileStream SequenceEqual). / Hash plus byte-level confirmation.</summary>
    Content
}

/// <summary>
/// Вариант критерия для выпадающего списка (отображаемое имя + значение перечисления).
/// Criterion option for the combo box (display name + enum value).
/// </summary>
public sealed class CriterionOption
{
    /// <summary>Значение перечисления. / Enum value.</summary>
    public DuplicateCriterion Value { get; }
    /// <summary>Отображаемое имя. / Display name.</summary>
    public string Display { get; }
    /// <summary>Создаёт вариант. / Creates an option.</summary>
    public CriterionOption(DuplicateCriterion value, string display) { Value = value; Display = display; }
    /// <summary>Возвращает отображаемое имя (для ComboBox). / Returns the display name (for ComboBox).</summary>
    public override string ToString() => Display;
}

/// <summary>
/// Один файл-кандидат в группе дубликатов. / A single candidate file inside a duplicate group.
/// </summary>
public sealed partial class DuplicateItem : ObservableObject
{
    /// <summary>Полный путь к файлу. / Full path to the file.</summary>
    public string Path { get; }
    /// <summary>Размер файла в байтах. / File size in bytes.</summary>
    public long Size { get; }
    /// <summary>SHA-256 хеш (заполняется при критерии Hash/Content). / SHA-256 hash (filled for Hash/Content).</summary>
    [ObservableProperty] private string _hash = "";
    /// <summary>Отмечен ли файл для удаления. / Whether the file is marked for deletion.</summary>
    [ObservableProperty] private bool _isMarkedForDelete;
    /// <summary>Примечание (например, «отличается» при побайтовой проверке). / Note (e.g. "differs" after byte check).</summary>
    [ObservableProperty] private string _note = "";

    /// <summary>Создаёт элемент. / Creates an item.</summary>
    public DuplicateItem(string path, long size) { Path = path; Size = size; }
}

/// <summary>
/// Группа дубликатов (общий ключ: размер / имя / хеш). / A duplicate group (shared key: size / name / hash).
/// </summary>
public sealed partial class DuplicateGroup : ObservableObject
{
    /// <summary>Ключ группы (для отображения). / Group key (for display).</summary>
    [ObservableProperty] private string _key;
    /// <summary>Размер файлов группы (байты). / File size of the group (bytes).</summary>
    [ObservableProperty] private long _size;
    /// <summary>Элементы группы. / Group items.</summary>
    public ObservableCollection<DuplicateItem> Items { get; } = new();
    /// <summary>Число элементов в группе. / Number of items in the group.</summary>
    public int Count => Items.Count;

    /// <summary>Создаёт группу. / Creates a group.</summary>
    public DuplicateGroup(string key, long size) { Key = key; Size = size; }
}

/// <summary>
/// ViewModel окна поиска дубликатов (ph2.4, exp.yml).
/// Duplicate search window ViewModel (ph2.4).
/// Алгоритм: сбор файлов → группировка по размеру (и имени) → хеширование SHA-256
/// через существующий <see cref="ChecksumHelper"/> параллельно (Parallel.ForEachAsync)
/// → опциональное побайтовое подтверждение (FileStream SequenceEqual).
/// Algorithm: gather files → group by size (and name) → SHA-256 hashing via the existing
/// ChecksumHelper in parallel (Parallel.ForEachAsync) → optional byte-level confirmation.
/// Удаление отмеченных файлов выполняется через <see cref="IFileSystem.DeleteAsync"/> с подтверждением.
/// Deletion of marked files is performed via IFileSystem.DeleteAsync with confirmation.
/// </summary>
public partial class DuplicatesWindowViewModel : ObservableObject
{
    private readonly IFileSystem _fs = LocalFileSystem.Instance;
    private CancellationTokenSource? _cts;

    /// <summary>Корневая папка поиска (из активной панели). / Root search folder (from the active panel).</summary>
    [ObservableProperty] private string _rootFolder = "";
    /// <summary>Включать ли подпапки. / Include subfolders.</summary>
    [ObservableProperty] private bool _includeSubfolders = true;
    /// <summary>Выбранный критерий поиска. / Selected search criterion.</summary>
    [ObservableProperty] private DuplicateCriterion _criterion = DuplicateCriterion.Hash;
    /// <summary>Статус/сообщение для UI. / Status message for UI.</summary>
    [ObservableProperty] private string _status = LocalizationService.Current.GetString("Dup.Ready");
    /// <summary>Прогресс (0-100). / Progress (0-100).</summary>
    [ObservableProperty] private int _progressPercent;
    /// <summary>Идёт ли поиск (блокирует кнопку «Найти», показывает «Отмена»). / Whether a search is running.</summary>
    [ObservableProperty] private bool _isSearching;
    /// <summary>Число найденных групп. / Number of found groups.</summary>
    [ObservableProperty] private int _groupCount;
    /// <summary>Общее число продублированных файлов. / Total number of duplicated files.</summary>
    [ObservableProperty] private int _duplicateCount;

    /// <summary>Группы дубликатов для отображения. / Duplicate groups for display.</summary>
    public ObservableCollection<DuplicateGroup> Groups { get; } = new();

    /// <summary>Варианты критерия для ComboBox. / Criterion options for the combo box.</summary>
    public IReadOnlyList<CriterionOption> Criteria { get; } = new List<CriterionOption>
    {
        new(DuplicateCriterion.Size, LocalizationService.Current.GetString("Dup.Criterion.Size")),
        new(DuplicateCriterion.SizeAndName, LocalizationService.Current.GetString("Dup.Criterion.SizeName")),
        new(DuplicateCriterion.Hash, LocalizationService.Current.GetString("Dup.Criterion.Hash")),
        new(DuplicateCriterion.Content, LocalizationService.Current.GetString("Dup.Criterion.Content")),
    };

    /// <summary>Запрос подтверждения удаления (делегируется окну, чтобы VM не зависела от WPF).
    /// Deletion confirmation callback (delegated to the window so the VM stays WPF-agnostic).</summary>
    public System.Func<int, string, bool>? RequestDeleteConfirmation { get; set; }

    /// <summary>Создаёт ViewModel для заданной начальной папки. / Creates the VM for a start folder.</summary>
    public DuplicatesWindowViewModel(string startFolder) => RootFolder = startFolder;

    private static string L10n(string key) => LocalizationService.Current.GetString(key);

    /// <summary>Пересчитывает доступность команды «Найти» при смене флага поиска.
    /// Recomputes the "Find" command availability when the searching flag changes.</summary>
    partial void OnIsSearchingChanged(bool value) => SearchCommand.NotifyCanExecuteChanged();

    private bool CanSearch() => !IsSearching;

    /// <summary>
    /// Запускает поиск дубликатов: собирает файлы (рекурсивно при необходимости), группирует по
    /// размеру/имени, затем при необходимости хеширует (SHA-256) параллельно и подтверждает побайтово.
    /// Runs the duplicate search: gathers files (recursively if needed), groups by size/name,
    /// then if needed hashes in parallel (SHA-256) and confirms byte-by-byte.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(RootFolder) || !Directory.Exists(RootFolder))
        { Status = L10n("Dup.Status.NoFolder"); return; }

        Groups.Clear();
        GroupCount = 0;
        DuplicateCount = 0;
        ProgressPercent = 0;
        IsSearching = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            // 1. Сбор всех файлов под папкой (рекурсивно опционально).
            // 1. Gather all files under the folder (recursively if requested).
            Status = L10n("Dup.Status.Gather");
            var all = new List<FileEntry>();
            await CollectFilesAsync(RootFolder, IncludeSubfolders, all, ct);

            // 2. Группировка по размеру; оставляем только группы из >1 файла.
            // 2. Group by size; keep only groups with more than one file.
            var bySize = all.Where(f => f.Size > 0)
                           .GroupBy(f => f.Size)
                           .Where(g => g.Count() > 1)
                           .ToList();

            if (Criterion is DuplicateCriterion.Size or DuplicateCriterion.SizeAndName)
            {
                BuildGroupsBySizeOrName(bySize);
            }
            else
            {
                // 3. Хеширование SHA-256 кандидатов параллельно, затем группировка по хешу.
                // 3. Parallel SHA-256 hashing of candidates, then group by hash.
                Status = L10n("Dup.Status.Hashing");
                await BuildGroupsByHashAsync(bySize, ct);
            }

            if (Groups.Count == 0)
                Status = L10n("Dup.Status.None");
            else
                Status = string.Format(L10n("Dup.Status.Found"), GroupCount, DuplicateCount);
            ProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            Status = LocalizationService.Current.GetString("Dup.Cancelled");
        }
        catch (Exception ex)
        {
            LogService.Error($"Duplicate search failed: {ex.Message}", nameof(DuplicatesWindowViewModel), ex);
            Status = string.Format(L10n("Status.Error"), ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Отменяет текущий поиск. / Cancels the running search.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>
    /// Отмечает для удаления все файлы группы, кроме первого (типичный сценарий очистки дублей).
    /// Marks all but the first file in each group for deletion (typical dedup cleanup scenario).
    /// </summary>
    [RelayCommand]
    private void MarkExceptFirst()
    {
        foreach (var g in Groups)
            foreach (var it in g.Items.Skip(1))
                it.IsMarkedForDelete = true;
    }

    /// <summary>Снимает все отметки удаления. / Clears all deletion marks.</summary>
    [RelayCommand]
    private void ClearMarks()
    {
        foreach (var g in Groups)
            foreach (var it in g.Items)
                it.IsMarkedForDelete = false;
    }

    /// <summary>
    /// Удаляет отмеченные файлы через <see cref="IFileSystem.DeleteAsync"/> (с подтверждением).
    /// Deletes the marked files via IFileSystem.DeleteAsync (with confirmation).
    /// </summary>
    [RelayCommand]
    private async Task DeleteMarkedAsync()
    {
        var marked = Groups.SelectMany(g => g.Items).Where(i => i.IsMarkedForDelete).ToList();
        if (marked.Count == 0) { Status = L10n("Dup.NothingMarked"); return; }

        if (RequestDeleteConfirmation?.Invoke(marked.Count, RootFolder) != true) return;

        int ok = 0;
        var removed = new List<DuplicateItem>();
        foreach (var item in marked)
        {
            try
            {
                await _fs.DeleteAsync(item.Path, recursive: false, CancellationToken.None);
                ok++;
                removed.Add(item);
            }
            catch (Exception ex)
            {
                LogService.Error($"Delete duplicate failed: {item.Path}: {ex.Message}", nameof(DuplicatesWindowViewModel), ex);
            }
        }

        // Удаляем удалённые элементы; группы с ≤1 элементом больше не являются дубликатами.
        // Remove deleted items; groups with ≤1 element are no longer duplicates.
        foreach (var g in Groups.ToList())
        {
            foreach (var it in removed) g.Items.Remove(it);
            if (g.Items.Count <= 1) Groups.Remove(g);
        }
        UpdateCounts();
        Status = string.Format(L10n("Dup.Status.Deleted"), ok, marked.Count - ok);
    }

    // ──────────────────────────────────────────────────────────────
    // Внутренние helpers / Internal helpers
    // ──────────────────────────────────────────────────────────────

    private void BuildGroupsBySizeOrName(IEnumerable<IGrouping<long, FileEntry>> bySize)
    {
        foreach (var g in bySize)
        {
            if (Criterion == DuplicateCriterion.Size)
            {
                AddGroup(FormatSize(g.Key), g.Key, g.Select(f => new DuplicateItem(f.FullPath, f.Size)));
            }
            else
            {
                // Дополнительно группируем по имени внутри размера.
                // Additionally group by name within the size.
                foreach (var ng in g.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
                    AddGroup($"{ng.Key}  ({FormatSize(g.Key)})", g.Key, ng.Select(f => new DuplicateItem(f.FullPath, f.Size)));
            }
        }
    }

    private async Task BuildGroupsByHashAsync(IEnumerable<IGrouping<long, FileEntry>> bySize, CancellationToken ct)
    {
        var candidates = bySize.SelectMany(g => g).ToList();
        int total = candidates.Count;
        int processed = 0;

        foreach (var g in bySize)
        {
            ct.ThrowIfCancellationRequested();
            var items = g.ToList();
            var results = new (FileEntry f, string hash)[items.Count];

            await Parallel.ForEachAsync(items.Select((f, i) => (f, i)),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount), CancellationToken = ct },
                async (x, cti) =>
                {
                    string h = "";
                    try { h = await ChecksumHelper.ComputeHashAsync(x.f.FullPath, ChecksumAlgorithm.SHA256, null, cti); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    { LogService.Warn($"Hash failed: {x.f.FullPath}: {ex.Message}", nameof(DuplicatesWindowViewModel)); }
                    results[x.i] = (x.f, h);
                    int p = Interlocked.Increment(ref processed);
                    ProgressPercent = total > 0 ? p * 100 / total : 0;
                }).ConfigureAwait(false);

            var valid = results.Where(r => !string.IsNullOrEmpty(r.hash)).ToList();
            foreach (var hg in valid.GroupBy(r => r.hash, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            {
                var members = hg.Select(r => new DuplicateItem(r.f.FullPath, r.f.Size) { Hash = r.hash }).ToList();

                // Побайтовое подтверждение для критерия «по содержимому».
                // Byte-level confirmation for the "by content" criterion.
                if (Criterion == DuplicateCriterion.Content && members.Count > 1)
                {
                    var reference = members[0];
                    foreach (var m in members.Skip(1).ToList())
                    {
                        bool equal;
                        try { equal = await ContentsEqualAsync(reference.Path, m.Path, ct); }
                        catch { equal = false; }
                        if (!equal) { m.Note = L10n("Dup.Differ"); members.Remove(m); }
                    }
                }

                if (members.Count > 1)
                    AddGroup(hg.Key, g.Key, members);
            }
        }
    }

    private void AddGroup(string key, long size, IEnumerable<DuplicateItem> items)
    {
        var grp = new DuplicateGroup(key, size);
        foreach (var it in items) grp.Items.Add(it);
        Groups.Add(grp);
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        GroupCount = Groups.Count;
        DuplicateCount = Groups.Sum(g => g.Items.Count);
    }

    private static async Task CollectFilesAsync(string folder, bool recursive, List<FileEntry> sink, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<FileEntry> entries;
        try { entries = await LocalFileSystem.Instance.EnumerateAsync(folder, includeHidden: true, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { LogService.Warn($"Enumerate failed: {folder}: {ex.Message}", nameof(DuplicatesWindowViewModel)); return; }

        foreach (var e in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (e.IsDirectory)
            {
                if (recursive) await CollectFilesAsync(e.FullPath, true, sink, ct);
            }
            else sink.Add(e);
        }
    }

    /// <summary>
    /// Побайтовое сравнение двух файлов порциями (чанками). / Byte-by-byte comparison of two files in chunks.
    /// </summary>
    private static async Task<bool> ContentsEqualAsync(string a, string b, CancellationToken ct)
    {
        const int buf = 1 << 20; // 1 МБ
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        if (sa.Length != sb.Length) return false;

        var ba = new byte[buf];
        var bb = new byte[buf];
        int ra, rb;
        while ((ra = await sa.ReadAsync(ba.AsMemory(0, buf), ct)) > 0)
        {
            rb = await sb.ReadAsync(bb.AsMemory(0, buf), ct);
            if (ra != rb) return false;
            if (!ba.AsSpan(0, ra).SequenceEqual(bb.AsSpan(0, rb))) return false;
        }
        return true;
    }

    private static string FormatSize(long bytes)
        => bytes >= 1 << 30 ? $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
         : bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024):F2} MB"
         : bytes >= 1 << 10 ? $"{bytes / 1024.0:F2} KB"
         : $"{bytes} B";
}

/// <summary>
/// Окно поиска дубликатов (ph2.4, exp.yml). Модальное, с кастомным заголовком (WindowStyle=None).
/// Duplicate search window (ph2.4). Modal, with a custom title bar (WindowStyle=None).
/// Связывается с <see cref="DuplicatesWindowViewModel"/> через DataContext.
/// Binds to DuplicatesWindowViewModel via DataContext.
/// </summary>
public partial class DuplicatesWindow : Window
{
    private readonly DuplicatesWindowViewModel _vm;

    /// <summary>Создаёт окно для заданной начальной папки. / Creates the window for a start folder.</summary>
    public DuplicatesWindow(string startFolder)
    {
        InitializeComponent();
        _vm = new DuplicatesWindowViewModel(startFolder);
        // Делегируем подтверждение удаления окну (MessageBox — забота View).
        // Delegate the deletion confirmation to the window (MessageBox is a View concern).
        _vm.RequestDeleteConfirmation = (count, folder) =>
            StyledMessageBoxWindow.Show(
                string.Format(LocalizationService.Current.GetString("Dup.Status.DeleteConfirm"), count, folder),
                LocalizationService.Current.GetString("Dup.DeleteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
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

    #region Обзор папки / Folder browse
    /// <summary>
    /// Открывает диалог выбора папки (BCL, Microsoft.Win32.OpenFileDialog с отключённой валидацией имён).
    /// Opens a folder picker (BCL, Microsoft.Win32.OpenFileDialog with name validation disabled).
    /// </summary>
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            ValidateNames = false,
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "folder",
            Title = LocalizationService.Current.GetString("Dup.Browse"),
        };
        if (dlg.ShowDialog(this) == true)
        {
            var path = dlg.FileName;
            if (File.Exists(path)) path = Path.GetDirectoryName(path)!;
            if (Directory.Exists(path)) _vm.RootFolder = path;
        }
    }
    #endregion
}
