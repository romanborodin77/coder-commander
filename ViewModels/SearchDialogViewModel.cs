using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления диалога поиска (ph2.1 / exp.yml).
/// View-model for the search dialog (ph2.1).
/// Хранит критерии, результаты (<see cref="ObservableCollection{SearchResult}"/>) и команды
/// (поиск / отмена / очистка / открытие файла). Результаты поступают из
/// <see cref="SearchOperation"/> через <see cref="Progress{T}"/> (поток UI).
/// Holds criteria, results and commands (search/cancel/clear/open). Results arrive from
/// SearchOperation via Progress{T} (UI thread).
/// </summary>
public partial class SearchDialogViewModel : ObservableObject
{
    /// <summary>Единицы измерения размера. / Size units.</summary>
    public enum SearchSizeUnit { B, KB, MB, GB }

    private CancellationTokenSource? _cts;

    /// <summary>Корневая папка поиска. / Root search folder.</summary>
    [ObservableProperty] private string _rootPath = Environment.CurrentDirectory;

    /// <summary>Маски имён (через «;»). / Name masks (separated by ';').</summary>
    [ObservableProperty] private string _nameMasks = "*.*";

    /// <summary>Режим regex для имён. / Regex mode for names.</summary>
    [ObservableProperty] private bool _nameRegexMode;

    /// <summary>Искомое содержимое (regex). / Content regex.</summary>
    [ObservableProperty] private string _contentPattern = "";

    /// <summary>Учитывать регистр. / Case-sensitive.</summary>
    [ObservableProperty] private bool _matchCase;

    /// <summary>Включать вложенные папки. / Recurse subfolders.</summary>
    [ObservableProperty] private bool _recurseSubdirectories = true;

    /// <summary>Минимальный размер (текст). / Minimum size (text).</summary>
    [ObservableProperty] private string _minSizeText = "";

    /// <summary>Максимальный размер (текст). / Maximum size (text).</summary>
    [ObservableProperty] private string _maxSizeText = "";

    /// <summary>Единица размера. / Size unit.</summary>
    [ObservableProperty] private SearchSizeUnit _sizeUnit = SearchSizeUnit.B;

    /// <summary>Минимальная дата изменения. / Minimum last-write date.</summary>
    [ObservableProperty] private DateTime? _dateFrom;

    /// <summary>Максимальная дата изменения. / Maximum last-write date.</summary>
    [ObservableProperty] private DateTime? _dateTo;

    /// <summary>Требуемый атрибут «только чтение». / Require read-only attribute.</summary>
    [ObservableProperty] private bool _attrReadOnly;
    /// <summary>Требуемый атрибут «скрытый». / Require hidden attribute.</summary>
    [ObservableProperty] private bool _attrHidden;
    /// <summary>Требуемый атрибут «системный». / Require system attribute.</summary>
    [ObservableProperty] private bool _attrSystem;
    /// <summary>Требуемый атрибут «архивный». / Require archive attribute.</summary>
    [ObservableProperty] private bool _attrArchive;

    /// <summary>
    /// Искать содержимое внутри архивов (ZIP, 7Z, RAR и т.д.) (ph4.1).
    /// Search content inside archives (ZIP, 7Z, RAR, etc.) (ph4.1).
    /// </summary>
    [ObservableProperty] private bool _searchInArchives;

    /// <summary>Ключ кодировки (auto/utf8/ansi/koi8r/utf16/utf32). / Encoding key.</summary>
    [ObservableProperty] private string _selectedEncodingKey = "auto";

    /// <summary>Результаты поиска. / Search results.</summary>
    public ObservableCollection<SearchResult> Results { get; } = new();

    /// <summary>Число проверенных файлов. / Scanned file count.</summary>
    [ObservableProperty] private int _scanned;

    /// <summary>Число найденных совпадений. / Found match count.</summary>
    [ObservableProperty] private int _found;

    /// <summary>Признак выполнения поиска. / Whether a search is running.</summary>
    [ObservableProperty] private bool _isRunning;

    /// <summary>Статусная строка. / Status text.</summary>
    [ObservableProperty] private string _statusText = "Готово / Ready";

    /// <summary>Варианты единиц размера для ComboBox. / Size unit options for ComboBox.</summary>
    public IReadOnlyList<SearchSizeUnit> SizeUnitOptions { get; } =
        new[] { SearchSizeUnit.B, SearchSizeUnit.KB, SearchSizeUnit.MB, SearchSizeUnit.GB };

    /// <summary>Варианты кодировок для ComboBox. / Encoding options for ComboBox.</summary>
    public IReadOnlyList<string> EncodingOptions { get; } = new[] { "auto", "utf8", "ansi", "koi8r", "utf16", "utf32" };

    /// <summary>Колбэк открытия файла в редакторе (задаётся из MainWindow). / File-open callback (set by MainWindow).</summary>
    public Action<string>? OpenFileRequest { get; set; }

    /// <summary>Колбэк подачи результатов в файловую панель (ph2.2, задаётся из MainWindow). / Send-results-to-panel callback (set by MainWindow).</summary>
    public Action<IEnumerable<SearchResult>>? ShowInPanelRequest { get; set; }

    /// <summary>Доступность кнопки «Показать результаты в панели» (есть ли что показывать). / Whether the "show in panel" button is enabled.</summary>
    [ObservableProperty] private bool _canShowInPanel;

    private readonly IProgress<IReadOnlyList<SearchResult>> _resultProgress;
    private readonly IProgress<SearchProgress> _statusProgress;

    public SearchDialogViewModel()
    {
        // Progress<T> захватывает SynchronizationContext создания (поток UI) — добавление в
        // ObservableCollection безопасно. / Captures UI sync context, so collection updates are safe.
        _resultProgress = new Progress<IReadOnlyList<SearchResult>>(batch =>
        {
            foreach (var r in batch) Results.Add(r);
        });
        _statusProgress = new Progress<SearchProgress>(p =>
        {
            Scanned = p.Scanned;
            Found = p.Found;
            IsRunning = p.IsRunning;
        });
    }

    /// <summary>Запускает поиск. / Starts the search.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsRunning) return;
        Results.Clear();
        Scanned = 0; Found = 0;
        SearchCriteria criteria;
        try { criteria = BuildCriteria(); }
        catch (Exception ex) { StatusText = string.Format(LocalizationService.Current.GetString("Search.CriteriaError"), ex.Message); return; }

        var cts = new CancellationTokenSource();
        _cts = cts;
        IsRunning = true;
        StatusText = LocalizationService.Current.GetString("Search.SearchingStatus");
        try
        {
            var op = new SearchOperation(criteria, _statusProgress, _resultProgress);
            await Task.Run(() => op.ExecuteAsync(cts.Token), cts.Token);
            StatusText = Found > 0
                ? string.Format(LocalizationService.Current.GetString("Search.FoundScanned"), Found, Scanned)
                : LocalizationService.Current.GetString("Search.NothingFoundStatus");
            CanShowInPanel = Found > 0; // ph2.2: кнопка «Показать в панели»
        }
        catch (OperationCanceledException) { StatusText = LocalizationService.Current.GetString("Search.CancelledStatus"); }
        catch (Exception ex) { StatusText = string.Format(LocalizationService.Current.GetString("Status.Error"), ex.Message); }
        finally { IsRunning = false; _cts = null; }
    }

    /// <summary>Отменяет текущий поиск. / Cancels the running search.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Очищает результаты. / Clears the results.</summary>
    [RelayCommand]
    private void Clear() { Results.Clear(); CanShowInPanel = false; }

    /// <summary>Передаёт результаты поиска в файловую панель (ph2.2). / Sends search results to the file panel (ph2.2).</summary>
    [RelayCommand]
    private void ShowInPanel()
    {
        if (Results.Count == 0) return;
        ShowInPanelRequest?.Invoke(Results);
    }

    /// <summary>Открывает файл результата в редакторе. / Opens the result file in the editor.</summary>
    [RelayCommand]
    private void OpenResult(SearchResult? r)
    {
        if (r is null) return;
        OpenFileRequest?.Invoke(r.FullPath);
    }

    private SearchCriteria BuildCriteria()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var required = FileAttributes.None;
        var excluded = FileAttributes.None;
        if (AttrReadOnly) required |= FileAttributes.ReadOnly;
        if (AttrHidden) required |= FileAttributes.Hidden;
        if (AttrSystem) required |= FileAttributes.System;
        if (AttrArchive) required |= FileAttributes.Archive;

        Encoding? fallback = SelectedEncodingKey switch
        {
            "utf8" => Encoding.UTF8,
            "ansi" => Encoding.GetEncoding(1251),
            "koi8r" => Encoding.GetEncoding(20866),
            "utf16" => Encoding.Unicode,
            "utf32" => Encoding.UTF32,
            _ => null, // auto: BOM иначе UTF-8 / auto: BOM else UTF-8
        };

        return new SearchCriteria
        {
            RootPath = string.IsNullOrWhiteSpace(RootPath) ? Environment.CurrentDirectory : RootPath,
            NameMasks = string.IsNullOrWhiteSpace(NameMasks) ? "*.*" : NameMasks,
            NameRegexMode = NameRegexMode,
            ContentPattern = string.IsNullOrWhiteSpace(ContentPattern) ? null : ContentPattern,
            MatchCase = MatchCase,
            Recurse = RecurseSubdirectories,
            MinSize = ParseSize(MinSizeText),
            MaxSize = ParseSize(MaxSizeText),
            DateFrom = DateFrom,
            DateTo = DateTo,
            RequiredAttributes = required,
            ExcludedAttributes = excluded,
            FallbackEncoding = fallback,
            SearchInArchives = SearchInArchives, // ph4.1
        };
    }

    private long? ParseSize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!double.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
            return null;
        long mult = SizeUnit switch
        {
            SearchSizeUnit.KB => 1024L,
            SearchSizeUnit.MB => 1024L * 1024,
            SearchSizeUnit.GB => 1024L * 1024 * 1024,
            _ => 1L,
        };
        return (long)(value * mult);
    }
}
