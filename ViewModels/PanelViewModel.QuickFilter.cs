using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CoderCommander.Models;
using CoderCommander.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.ViewModels;

/// <summary>
/// Часть PanelViewModel, реализующая быстрый фильтр/поиск в панели (ph1.1, exp.yml).
/// Partial part of PanelViewModel implementing the panel quick filter / quick search (ph1.1).
/// Реализовано отдельным файлом, чтобы не переписывать PanelViewModel.cs целиком
/// (минимум конфликтов слияния с параллельными сессиями).
/// Implemented as a separate file to avoid rewriting PanelViewModel.cs entirely.
/// </summary>
public partial class PanelViewModel
{
    /// <summary>Область действия фильтра/поиска: все объекты, только файлы, только папки.</summary>
    public enum QuickFilterScope { All, Files, Folders }

    private CancellationTokenSource? _qfCts;
    private List<FileSystemItem> _quickMatches = new();
    private ICollectionView? _itemsView;

    /// <summary>
    /// Представление коллекции <see cref="Items"/> с применённым ICollectionView-фильтром.
    /// View over <see cref="Items"/> with the applied ICollectionView filter.
    /// Используется ListBox вместо прямой привязки к Items (скрывает несовпавшие без ре-перечисления ФС).
    /// Used by the ListBox instead of a direct Items binding (hides non-matching without re-enumerating the FS).
    /// </summary>
    public ICollectionView ItemsView => _itemsView ??= CollectionViewSource.GetDefaultView(Items);

    /// <summary>Текст инкрементального фильтра (qsFilter): скрывает несовпадающие элементы.</summary>
    [ObservableProperty] private string _quickFilterText = "";
    /// <summary>Учитывать регистр при фильтре/поиске.</summary>
    [ObservableProperty] private bool _quickMatchCase;
    /// <summary>Совпадение только в начале имени (иначе — везде в имени).</summary>
    [ObservableProperty] private bool _quickMatchStart;
    /// <summary>Область действия: все / только файлы / только папки.</summary>
    [ObservableProperty] private QuickFilterScope _quickScope = QuickFilterScope.All;
    /// <summary>Активен ли режим быстрого поиска-навигации (qsSearch).</summary>
    [ObservableProperty] private bool _isQuickSearchActive;
    /// <summary>Текущая строка быстрого поиска (qsSearch).</summary>
    [ObservableProperty] private string _quickSearchText = "";
    /// <summary>Статусная строка фильтра/поиска для UI (например, «Фильтр: 3/12»).</summary>
    [ObservableProperty] private string _quickStatus = "";
    /// <summary>Индекс текущего совпадения при поиске (для «N/M»).</summary>
    [ObservableProperty] private int _quickMatchIndex = -1;

    /// <summary>Текстовая метка области действия для UI («Все»/«Файлы»/«Папки»).</summary>
    public string QuickScopeText => QuickScope switch
    {
        QuickFilterScope.Files => "Файлы",
        QuickFilterScope.Folders => "Папки",
        _ => "Все"
    };

    // ───────────────────────── Изменения свойств ─────────────────────────

    /// <summary>
    /// При изменении текста фильтра — отложенный (debounce ~150 мс) пересчёт ICollectionView.Filter.
    /// On filter text change — debounced (~150 ms) recompute of ICollectionView.Filter.
    /// </summary>
    partial void OnQuickFilterTextChanged(string value) => ScheduleQuickFilter();

    partial void OnQuickMatchCaseChanged(bool value) => ReapplyQuick();
    partial void OnQuickMatchStartChanged(bool value) => ReapplyQuick();

    partial void OnQuickScopeChanged(QuickFilterScope value)
    {
        OnPropertyChanged(nameof(QuickScopeText));
        ReapplyQuick();
    }

    /// <summary>
    /// Смена папки сбрасывает быстрый ПОИСК (он транзитный), но сохраняет ФИЛЬТР
    /// (фильтр намеренно переживает навигацию, как в Double Commander).
    /// Folder change resets the quick SEARCH (transient) but keeps the FILTER (persists across navigation, DC-style).
    /// </summary>
    partial void OnCurrentPathChanged(string value) => ResetQuickSearchOnly();

    // ───────────────────────── Инкрементальный фильтр (qsFilter) ─────────────────────────

    /// <summary>
    /// Предикат соответствия элемента условиям фильтра/поиска.
    /// Match predicate against the current filter/search options.
    /// </summary>
    /// <param name="item">Элемент ФС / File system item.</param>
    /// <param name="term">Искомая подстрока / Search substring.</param>
    private bool Matches(FileSystemItem item, string term)
    {
        if (item.IsParent) return true; // родитель «..» всегда видим в фильтре / parent is always visible
        if (QuickScope == QuickFilterScope.Files && item.IsDirectory) return false;
        if (QuickScope == QuickFilterScope.Folders && !item.IsDirectory) return false;
        if (string.IsNullOrEmpty(term)) return true;
        var name = QuickMatchCase ? item.Name : item.Name.ToLowerInvariant();
        var t = QuickMatchCase ? term : term.ToLowerInvariant();
        return QuickMatchStart
            ? name.StartsWith(t, System.StringComparison.Ordinal)
            : name.Contains(t, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Планирует применение фильтра с debounce ~150 мс (чтобы не дёргать View на каждый символ).
    /// Schedules filter application with ~150 ms debounce (avoids updating the View per keystroke).
    /// </summary>
    private void ScheduleQuickFilter()
    {
        _qfCts?.Cancel();
        _qfCts?.Dispose();
        var cts = _qfCts = new CancellationTokenSource();
        var sync = SynchronizationContext.Current;
        _ = Task.Delay(150, cts.Token).ContinueWith(_ =>
        {
            if (cts.IsCancellationRequested) return;
            (sync ?? SynchronizationContext.Current)?.Post(_ => ApplyQuickFilter(), null);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Применяет (или снимает) ICollectionView-фильтр, скрывая несовпадающие элементы текущей панели.
    /// Applies (or clears) the ICollectionView filter, hiding non-matching items of the current panel.
    /// Не перечитывает ФС — работает только поверх уже загруженной коллекции Items.
    /// Does NOT re-enumerate the FS — operates only on the already-loaded Items collection.
    /// </summary>
    public void ApplyQuickFilter()
    {
        var view = ItemsView;
        if (string.IsNullOrWhiteSpace(QuickFilterText))
        {
            if (view.Filter != null) { view.Filter = null; view.Refresh(); }
            UpdateQuickStatus();
            return;
        }

        view.Filter = o => o is FileSystemItem fi && Matches(fi, QuickFilterText);
        view.Refresh();

        // Если текущий выделенный элемент скрыт фильтром — переносим выделение на первый видимый.
        // If the current selection is hidden by the filter — move selection to the first visible item.
        if (SelectedItem is FileSystemItem sel && !view.Contains(sel))
            SelectedItem = Items.FirstOrDefault(view.Contains);

        UpdateQuickStatus();
    }

    // ───────────────────────── Быстрый поиск-навигация (qsSearch) ─────────────────────────

    /// <summary>
    /// Дополняет строку быстрого поиска и перескакивает к первому/следующему совпадению по имени.
    /// Extends the quick-search string and jumps to the first/next name match.
    /// </summary>
    /// <param name="text">Введённые символы / Typed characters.</param>
    public void ExtendQuickSearch(string text)
    {
        QuickSearchText = string.IsNullOrEmpty(QuickSearchText) ? text : QuickSearchText + text;
        IsQuickSearchActive = true;
        RecomputeQuickMatches();
        if (_quickMatches.Count > 0)
        {
            QuickMatchIndex = 0;
            SelectedItem = _quickMatches[0];
        }
        else
        {
            QuickMatchIndex = -1;
        }
        UpdateQuickStatus();
    }

    private void RecomputeQuickMatches()
        => _quickMatches = Items.Where(i => !i.IsParent && Matches(i, QuickSearchText)).ToList();

    /// <summary>Переход к следующему совпадению поиска (стрелка Вниз).</summary>
    public void QuickSearchNext()
    {
        if (_quickMatches.Count == 0) return;
        QuickMatchIndex = (QuickMatchIndex + 1) % _quickMatches.Count;
        SelectedItem = _quickMatches[QuickMatchIndex];
        UpdateQuickStatus();
    }

    /// <summary>Переход к предыдущему совпадению поиска (стрелка Вверх).</summary>
    public void QuickSearchPrev()
    {
        if (_quickMatches.Count == 0) return;
        QuickMatchIndex = (QuickMatchIndex - 1 + _quickMatches.Count) % _quickMatches.Count;
        SelectedItem = _quickMatches[QuickMatchIndex];
        UpdateQuickStatus();
    }

    /// <summary>
    /// Завершает поиск: оставляет выделение на найденном элементе, гасит режим поиска.
    /// Finalizes search: keeps the selection on the found item, turns off search mode.
    /// </summary>
    public void FinalizeQuickSearch()
    {
        IsQuickSearchActive = false;
        QuickSearchText = "";
        QuickMatchIndex = -1;
        UpdateQuickStatus();
    }

    /// <summary>
    /// Полный сброс: очищает фильтр и поиск, показывает все элементы (Esc).
    /// Full reset: clears filter and search, shows all items (Esc).
    /// </summary>
    public void ResetQuick()
    {
        QuickFilterText = "";
        ResetQuickSearchOnly();
        if (ItemsView.Filter != null) { ItemsView.Filter = null; ItemsView.Refresh(); }
        UpdateQuickStatus();
    }

    /// <summary>Сбрасывает только транзитный поиск (фильтр сохраняется).</summary>
    private void ResetQuickSearchOnly()
    {
        IsQuickSearchActive = false;
        QuickSearchText = "";
        QuickMatchIndex = -1;
        _quickMatches.Clear();
        UpdateQuickStatus();
    }

    /// <summary>Циклически переключает область действия: все → файлы → папки → все.</summary>
    public void CycleScope()
    {
        QuickScope = QuickScope == QuickFilterScope.All ? QuickFilterScope.Files
                   : QuickScope == QuickFilterScope.Files ? QuickFilterScope.Folders
                   : QuickFilterScope.All;
    }

    // ───────────────────────── Общая переприменялка опций ─────────────────────────

    /// <summary>
    /// Переприменяет фильтр и/или пересчитывает совпадения поиска при смене опций
    /// (регистр, начало/везде, область действия).
    /// Re-applies filter and/or recomputes search matches when options change.
    /// </summary>
    private void ReapplyQuick()
    {
        if (!string.IsNullOrWhiteSpace(QuickFilterText)) ApplyQuickFilter();
        if (IsQuickSearchActive)
        {
            RecomputeQuickMatches();
            if (_quickMatches.Count > 0)
            {
                if (QuickMatchIndex >= _quickMatches.Count) QuickMatchIndex = _quickMatches.Count - 1;
                SelectedItem = _quickMatches[System.Math.Max(0, QuickMatchIndex)];
            }
            UpdateQuickStatus();
        }
    }

    /// <summary>Обновляет статусную строку QuickStatus для отображения в UI.</summary>
    private void UpdateQuickStatus()
    {
        if (IsQuickSearchActive)
        {
            QuickStatus = _quickMatches.Count == 0
                ? string.Format(LocalizationService.Current.GetString("Quick.NoMatch"), QuickSearchText)
                : string.Format(LocalizationService.Current.GetString("Quick.MatchStatus"), QuickMatchIndex + 1, _quickMatches.Count, QuickSearchText);
        }
        else if (!string.IsNullOrWhiteSpace(QuickFilterText))
        {
            var visible = Items.Count(i => Matches(i, QuickFilterText));
            QuickStatus = string.Format(LocalizationService.Current.GetString("Quick.FilterStatus"), visible, Items.Count);
        }
        else
        {
            QuickStatus = "";
        }
    }
}
