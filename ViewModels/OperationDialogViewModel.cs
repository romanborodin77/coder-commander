using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// ViewModel диалога прогресса файловой операции.
/// Управляет отображением прогресса, скорости, оставшегося времени и управления операцией (пауза/отмена).
/// ViewModel for the file operation progress dialog.
/// Manages progress display, speed, ETA, and operation control (pause/cancel).
/// </summary>
public partial class OperationDialogViewModel : ObservableObject, IDisposable
{
    private readonly IFileOperation _operation;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _elapsed = new();
    private readonly List<OperationProgress> _snapshots = new();
    private readonly object _lock = new();
    private readonly Dispatcher _uiDispatcher = Application.Current.Dispatcher;

    private long _lastTotalBytesDone;
    private DateTime _lastSnapshotTime;

    /// <summary>Заголовок операции (Копирование / Перенос / Удаление). / Operation title.</summary>
    [ObservableProperty] private string _operationTitle = "";

    /// <summary>Путь-источник. / Source path.</summary>
    [ObservableProperty] private string _sourcePath = "";

    /// <summary>Путь назначения. / Destination path.</summary>
    [ObservableProperty] private string _destPath = "";

    /// <summary>Текущий обрабатываемый файл. / Current file being processed.</summary>
    [ObservableProperty] private string _currentFile = "";

    /// <summary>Прогресс текущего файла (0..100). / Current file progress (0..100).</summary>
    [ObservableProperty] private double _fileProgress;

    /// <summary>Общий прогресс (0..100). / Overall progress (0..100).</summary>
    [ObservableProperty] private double _overallProgress;

    /// <summary>Текст скорости (напр. "12.5 МБ/с"). / Speed text (e.g. "12.5 MB/s").</summary>
    [ObservableProperty] private string _speedText = "";

    /// <summary>Текст оставшегося времени (напр. "Осталось: 2 мин 15 сек"). / ETA text.</summary>
    [ObservableProperty] private string _etaText = "";

    /// <summary>Текст статуса файлов (нapr. "15 из 42 файлов"). / Files status text.</summary>
    [ObservableProperty] private string _filesText = "";

    /// <summary>Текст состояния операции. / Operation state text.</summary>
    [ObservableProperty] private string _stateText = "";

    /// <summary>На паузе ли операция. / Whether operation is paused.</summary>
    [ObservableProperty] private bool _isPaused;

    /// <summary>Завершена ли операция. / Whether operation is complete.</summary>
    [ObservableProperty] private bool _isComplete;

    /// <summary>Текст кнопки паузы. / Pause button text.</summary>
    [ObservableProperty] private string _pauseButtonText = "\u23F8 Пауза";

    /// <summary>Команда отмены операции. / Cancel operation command.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts.Cancel();
        Pause();
    }

    /// <summary>Команда паузы/продолжения. / Pause/Resume toggle command.</summary>
    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused) Resume();
        else Pause();
    }

    /// <summary>Команда «Пропустить текущий файл». / Skip current file command.</summary>
    [RelayCommand]
    private void Skip()
    {
        RequestSkip();
        Resume();
    }

    private void Pause()
    {
        IsPaused = true;
        _pauseEvent.Reset();
        PauseButtonText = "\u25B6 " + LocalizationService.Current.GetString("OpDlg.Resume");
    }

    private void Resume()
    {
        IsPaused = false;
        _pauseEvent.Set();
        PauseButtonText = "\u23F8 " + LocalizationService.Current.GetString("OpDlg.Pause");
    }

    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _pauseEvent = new(true);
    private int _skipCurrentFile;

    /// <summary>Событие паузы/продолжения для операции. / Pause/resume event for the operation.</summary>
    public ManualResetEventSlim PauseEvent => _pauseEvent;

    /// <summary>
    /// Запрашивает пропуск текущего файла (потокобезопасно). / Requests skip of current file (thread-safe).
    /// </summary>
    public void RequestSkip()
    {
        System.Threading.Interlocked.Exchange(ref _skipCurrentFile, 1);
    }

    /// <summary>
    /// Проверяет и сбрасывает флаг пропуска (потокобезопасно, атомарно). / Checks and resets skip flag (thread-safe, atomic).
    /// </summary>
    /// <returns>True если нужно пропустить текущий файл. / True if current file should be skipped.</returns>
    public bool TrySkipCurrentFile()
    {
        return System.Threading.Interlocked.Exchange(ref _skipCurrentFile, 0) == 1;
    }

    /// <summary>Токен отмены для операции. / Cancellation token for the operation.</summary>
    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// Создаёт ViewModel диалога прогресса. / Creates the progress dialog ViewModel.
    /// </summary>
    /// <param name="operation">Файловая операция. / File operation.</param>
    /// <param name="title">Заголовок операции. / Operation title.</param>
    /// <param name="sourcePath">Путь-источник. / Source path.</param>
    /// <param name="destPath">Путь назначения. / Destination path.</param>
    public OperationDialogViewModel(IFileOperation operation, string title, string sourcePath, string destPath)
    {
        _operation = operation;
        _operationTitle = title;
        _sourcePath = sourcePath;
        _destPath = destPath;

        // Подписываемся на события операции.
        // Subscribe to operation events.
        _operation.ProgressChanged += OnProgressChanged;
        _operation.StateChanged += OnStateChanged;

        // Таймер обновления UI ~200 мс.
        // UI update timer ~200 ms.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        _elapsed.Start();
        _lastSnapshotTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Обработчик прогресса операции: сохраняет снимок для таймера.
    /// Handles operation progress: stores snapshot for timer.
    /// </summary>
    private void OnProgressChanged(object? sender, OperationProgress progress)
    {
        lock (_lock)
        {
            _snapshots.Add(progress);
        }
    }

    /// <summary>
    /// Обработчик смены состояния операции: обновляет UI.
    /// Handles operation state change: updates UI.
    /// </summary>
    private void OnStateChanged(object? sender, OperationState state)
    {
        try
        {
            _uiDispatcher.BeginInvoke(new Action(() =>
            {
                StateText = state switch
                {
                    OperationState.Running => LocalizationService.Current.GetString("OpDlg.State.Running"),
                    OperationState.Completed => LocalizationService.Current.GetString("OpDlg.State.Completed"),
                    OperationState.Canceled => LocalizationService.Current.GetString("OpDlg.State.Canceled"),
                    OperationState.Failed => LocalizationService.Current.GetString("OpDlg.State.Failed"),
                    _ => ""
                };

                if (state is OperationState.Completed or OperationState.Canceled or OperationState.Failed)
                {
                    IsComplete = true;
                    _timer.Stop();
                    _elapsed.Stop();
                    // Автоматически закрываем окно через 1.5 секунды после завершения.
                    // Automatically close window 1.5 seconds after completion.
                    _ = Task.Delay(1500).ContinueWith(_ =>
                    {
                        try
                        {
                            _uiDispatcher.BeginInvoke(new Action(() =>
                            {
                                if (Application.Current.Windows.Cast<Window>()
                                    .FirstOrDefault(w => w.DataContext == this) is Views.OperationDialogWindow dlg)
                                    dlg.Close();
                            }));
                        }
                        catch { /* Окно уже закрыто или приложение завершается */ }
                    });
                }
            }));
        }
        catch
        {
            // Диспетчер приостановлен или окно закрывается — игнорируем.
            // Dispatcher suspended or window closing — ignore.
        }
    }

    /// <summary>
    /// Тик таймера: обновляет UI из последнего снимка прогресса.
    /// Timer tick: updates UI from the latest progress snapshot.
    /// </summary>
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (IsPaused) return;

        OperationProgress? p;
        lock (_lock)
        {
            if (_snapshots.Count == 0) return;
            p = _snapshots[^1];
        }

        // Прогресс текущего файла / Current file progress
        FileProgress = p.BytesTotal > 0 ? Math.Min(100.0, 100.0 * p.BytesDone / p.BytesTotal) : 0;

        // Общий прогресс / Overall progress
        OverallProgress = p.Percent;

        // Текущий файл / Current file
        CurrentFile = Path.GetFileName(p.CurrentFile) ?? p.CurrentFile ?? "";

        // Файлы / Files
        FilesText = $"{p.FilesDone} {LocalizationService.Current.GetString("OpDlg.of")} {p.FilesTotal}";

        // Скорость / Speed
        var now = DateTime.UtcNow;
        var dt = (now - _lastSnapshotTime).TotalSeconds;
        if (dt > 0.5)
        {
            var bytesDelta = p.TotalBytesDone - _lastTotalBytesDone;
            var speed = bytesDelta / dt; // bytes/sec
            SpeedText = FormatSpeed(speed);

            // ETA / Оставшееся время
            if (p.TotalBytes > 0 && p.TotalBytesDone > 0 && p.Percent < 100)
            {
                var remaining = (p.TotalBytes - p.TotalBytesDone) / Math.Max(speed, 1);
                EtaText = FormatEta(remaining);
            }
            else
            {
                EtaText = "";
            }

            _lastTotalBytesDone = p.TotalBytesDone;
            _lastSnapshotTime = now;
        }
    }

    /// <summary>
    /// Форматирует скорость в человекочитаемый вид. / Formats speed to human-readable text.
    /// </summary>
    private static string FormatSpeed(double bytesPerSec)
    {
        var L = Services.LocalizationService.Current;
        if (bytesPerSec < 1024)
            return $"{bytesPerSec:F0} {L.GetString("OpDlg.Speed.Bs")}";
        if (bytesPerSec < 1024 * 1024)
            return $"{bytesPerSec / 1024:F1} {L.GetString("OpDlg.Speed.KBs")}";
        if (bytesPerSec < 1024L * 1024 * 1024)
            return $"{bytesPerSec / (1024 * 1024):F1} {L.GetString("OpDlg.Speed.MBs")}";
        return $"{bytesPerSec / (1024L * 1024 * 1024):F2} {L.GetString("OpDlg.Speed.GBs")}";
    }

    /// <summary>
    /// Форматирует время в человекочитаемый вид. / Formats time to human-readable text.
    /// </summary>
    private static string FormatEta(double seconds)
    {
        var L = Services.LocalizationService.Current;
        if (seconds < 1) return L.GetString("OpDlg.ETA.LessSec");
        if (seconds < 60) return string.Format(L.GetString("OpDlg.ETA.Sec"), (int)seconds);
        var min = (int)(seconds / 60);
        var sec = (int)(seconds % 60);
        return sec > 0
            ? string.Format(L.GetString("OpDlg.ETA.MinSec"), min, sec)
            : string.Format(L.GetString("OpDlg.ETA.Min"), min);
    }

    /// <summary>
    /// Освобождает ресурсы (таймер, отписки). / Disposes resources (timer, event subscriptions).
    /// </summary>
    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _operation.ProgressChanged -= OnProgressChanged;
        _operation.StateChanged -= OnStateChanged;
        _cts.Dispose();
        _pauseEvent.Dispose();
        GC.SuppressFinalize(this);
    }
}
