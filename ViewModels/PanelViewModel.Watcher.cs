using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CoderCommander.ViewModels;

/// <summary>
/// Часть PanelViewModel: «умный» авто-обновляющий watcher панели (ph3.4, exp.yml).
/// Partial of PanelViewModel: smart auto-refreshing panel watcher (ph3.4).
/// Обёртка над System.IO.FileSystemWatcher (ReadDirectoryChangesW) + debounce ~200 мс:
/// при внешнем изменении открытой папки список перезагружается автоматически,
/// без ручного F5 и без ложных массовых обновлений при серии событий.
/// Wrapper over System.IO.FileSystemWatcher (ReadDirectoryChangesW) + ~200 ms debounce:
/// on external change of the open folder the list auto-reloads, no manual F5, and a
/// burst of events coalesces into a single refresh (no spurious mass updates).
/// </summary>
public partial class PanelViewModel
{
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _watcherDebounceCts;
    private readonly object _watcherLock = new();
    private SynchronizationContext? _uiSync;
    private const int WatcherDebounceMs = 200;

    /// <summary>Включено ли авто-обновление панели при внешних изменениях папки.</summary>
    [ObservableProperty] private bool _autoRefresh = true;

    /// <summary>
    /// Инициализирует watcher: сохраняет контекст UI-потока, подписывается на смену
    /// папки и (если включено в настройках) запускает наблюдение.
    /// Initializes the watcher: stores the UI-thread sync context, subscribes to folder
    /// changes and starts watching (if enabled in the settings).
    /// </summary>
    public void InitWatcher()
    {
        _uiSync = SynchronizationContext.Current;
        PropertyChanged += OnPanelPropertyChanged;
        AutoRefresh = SettingsService.Load().AutoRefresh;
        if (AutoRefresh) StartWatcher();
    }

    /// <summary>
    /// Реакция на изменение свойства AutoRefresh: запуск/остановка наблюдения.
    /// Reacts to AutoRefresh change: starts/stops watching.
    /// </summary>
    partial void OnAutoRefreshChanged(bool value)
    {
        if (value) StartWatcher();
        else StopWatcher();
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CurrentPath)) ResubscribeWatcher();
    }

    /// <summary>
    /// Переподписывает watcher на текущий путь (при смене папки). Не дублирует
    /// обновление, если наблюдение выключено.
    /// Re-subscribes the watcher to the current path (on folder change); no-op when disabled.
    /// </summary>
    private void ResubscribeWatcher()
    {
        if (!AutoRefresh) return;
        StartWatcher();
    }

    /// <summary>
    /// Запускает (или перезапускает) FileSystemWatcher на текущем пути панели.
    /// Starts (or restarts) the FileSystemWatcher for the panel's current path.
    /// Потокобезопасно; старый watcher корректно освобождается.
    /// Thread-safe; the previous watcher is disposed cleanly.
    /// </summary>
    private void StartWatcher()
    {
        lock (_watcherLock)
        {
            StopWatcherCore();
            if (_disposed) return;
            var path = CurrentPath;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            try
            {
                var w = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Size
                                 | NotifyFilters.Attributes,
                    InternalBufferSize = 64 * 1024,
                };
                w.Changed += OnWatched;
                w.Created += OnWatched;
                w.Deleted += OnWatched;
                w.Renamed += OnWatchedRenamed;
                w.Error += OnWatcherError;
                w.EnableRaisingEvents = true;
                _watcher = w;
                LogService.Debug($"Watcher запущен для {path}", "PanelWatcher");
            }
            catch (Exception ex)
            {
                LogService.Warn($"Не удалось запустить watcher для '{path}': {ex.Message}", "PanelWatcher");
            }
        }
    }

    /// <summary>Останавливает наблюдение и отменяет отложенное обновление.</summary>
    private void StopWatcher()
    {
        lock (_watcherLock)
        {
            StopWatcherCore();
            _watcherDebounceCts?.Cancel();
            _watcherDebounceCts?.Dispose();
            _watcherDebounceCts = null;
        }
    }

    /// <summary>Освобождает ресурсы текущего FileSystemWatcher (без трогания debounce-CTS).</summary>
    private void StopWatcherCore()
    {
        var w = _watcher;
        if (w == null) return;
        try { w.EnableRaisingEvents = false; } catch { }
        w.Changed -= OnWatched;
        w.Created -= OnWatched;
        w.Deleted -= OnWatched;
        w.Renamed -= OnWatchedRenamed;
        w.Error -= OnWatcherError;
        try { w.Dispose(); } catch { }
        _watcher = null;
    }

    private void OnWatched(object sender, FileSystemEventArgs e) => ScheduleRefresh();
    private void OnWatchedRenamed(object sender, RenamedEventArgs e) => ScheduleRefresh();

    /// <summary>
    /// Обработчик ошибок watcher (например, переполнение буфера): переподписываемся.
    /// Watcher error handler (e.g. buffer overflow): re-subscribe.
    /// </summary>
    private void OnWatcherError(object? sender, ErrorEventArgs e)
    {
        LogService.Warn($"Ошибка FileSystemWatcher (перезапуск): {e.GetException().Message}", "PanelWatcher");
        StartWatcher();
    }

    /// <summary>
    /// Планирует перезагрузку списка с debounce ~200 мс, чтобы серия событий
    /// (копирование/массовая запись) слилась в ОДНО обновление панели.
    /// Schedules a list reload with ~200 ms debounce so a burst of events
    /// (copy/mass write) coalesces into a SINGLE panel refresh.
    /// </summary>
    private void ScheduleRefresh()
    {
        if (!AutoRefresh || _disposed) return;
        lock (_watcherLock)
        {
            _watcherDebounceCts?.Cancel();
            _watcherDebounceCts?.Dispose();
            _watcherDebounceCts = new CancellationTokenSource();
            var cts = _watcherDebounceCts;
            var sync = _uiSync;
            _ = Task.Delay(WatcherDebounceMs, cts.Token).ContinueWith(_ =>
            {
                if (cts.IsCancellationRequested || _disposed || !AutoRefresh) return;
                try
                {
                    if (sync != null) sync.Post(_ => _ = RefreshAsync(), null);
                    else _ = RefreshAsync();
                }
                catch (Exception ex)
                {
                    LogService.Warn($"Ошибка авто-обновления панели: {ex.Message}", "PanelWatcher");
                }
            }, TaskScheduler.Default);
        }
    }
}
