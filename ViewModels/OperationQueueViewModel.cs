using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Models;
using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

public partial class OperationQueueViewModel : ObservableObject
{
    private readonly OperationQueueService _queue = OperationQueueService.Current;
    private long _lastBytesDone;
    private DateTime _lastSnapshotTime = DateTime.UtcNow;

    public ObservableCollection<QueuedOperationItem> AllOperations { get; } = new();

    [ObservableProperty] private string _windowTitle = "";
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private string _runningLabel = "";
    [ObservableProperty] private string _queuedLabel = "";
    [ObservableProperty] private string _completedLabel = "";
    [ObservableProperty] private string _speedText = "";
    [ObservableProperty] private bool _hasSpeed;
    [ObservableProperty] private string _etaText = "";
    [ObservableProperty] private bool _hasEta;
    [ObservableProperty] private string _pauseIcon = "\u23F8";
    [ObservableProperty] private Brush _runningBadgeColor = Brushes.DodgerBlue;
    [ObservableProperty] private string _footerHint = "";
    [ObservableProperty] private string _clearLabel = "";
    [ObservableProperty] private string _cancelAllLabel = "";

    /// <summary>Событие запроса закрытия окна (для Cancel All). / Window close request event (for Cancel All).</summary>
    public event Action? CloseRequested;

    public bool HasActiveOperations => ActiveCount > 0 || PendingCount > 0;

    public OperationQueueViewModel()
    {
        _queue.QueueChanged += OnQueueChanged;
        _queue.PauseStateChanged += OnPauseStateChanged;
        UpdateAll();
    }

    partial void OnActiveCountChanged(int value)
    {
        UpdateWindowTitle();
        OnPropertyChanged(nameof(HasActiveOperations));
    }

    partial void OnPendingCountChanged(int value)
    {
        UpdateWindowTitle();
        OnPropertyChanged(nameof(HasActiveOperations));
    }

    private void UpdateWindowTitle()
    {
        var L = LocalizationService.Current;
        var total = ActiveCount + PendingCount;
        if (total > 0)
            WindowTitle = string.Format(L.GetString("OpQueue.Title.Operations"), total);
        else if (CompletedCount > 0)
            WindowTitle = L.GetString("OpQueue.Title.Completed");
        else
            WindowTitle = L.GetString("OpQueue.Title");
    }

    private void UpdatePauseIcon()
    {
        IsPaused = _queue.IsPaused;
        PauseIcon = _queue.IsPaused ? "\u25B6" : "\u23F8";
        RunningBadgeColor = IsPaused
            ? Application.Current.FindResource("WarnBrush") as Brush ?? Brushes.Orange
            : Application.Current.FindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
    }

    [ObservableProperty] private bool _isPaused;

    [RelayCommand]
    private void TogglePause() => _queue.TogglePause();

    [RelayCommand]
    private void CancelAll()
    {
        _queue.CancelAll();
        // Запрашиваем закрытие окна через 2 секунды (время на завершение отмены).
        // Request window close after 2 seconds (time for cancel completion).
        _ = Task.Delay(2000).ContinueWith(_ =>
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => CloseRequested?.Invoke()));
            }
            catch { /* Приложение закрывается */ }
        });
    }

    [RelayCommand]
    private void ClearCompleted() => _queue.RemoveCompleted();

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        try { Application.Current?.Dispatcher.BeginInvoke(new Action(UpdateAll)); }
        catch { }
    }

    private void OnPauseStateChanged(object? sender, bool isPaused)
    {
        try { Application.Current?.Dispatcher.BeginInvoke(new Action(UpdatePauseIcon)); }
        catch { }
    }

    public void UpdateAll()
    {
        try
        {
            var L = LocalizationService.Current;
            RunningLabel = L.GetString("OpQueue.Stats.Active");
            QueuedLabel = L.GetString("OpQueue.Stats.Pending");
            CompletedLabel = L.GetString("OpQueue.Stats.Completed");
            ClearLabel = L.GetString("OpQueue.ClearCompleted");
            CancelAllLabel = L.GetString("OpQueue.CancelAll");
            FooterHint = L.GetString("OpQueue.Footer.Hint");

            ActiveCount = _queue.ActiveCount;
            PendingCount = _queue.PendingCount;
            CompletedCount = _queue.CompletedCount;

            UpdateOperationsList();
            UpdateSpeed();
            UpdateWindowTitle();
            UpdatePauseIcon();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateAll error: {ex.Message}");
        }
    }

    private void UpdateSpeed()
    {
        HasSpeed = false;
        HasEta = false;
        SpeedText = "";
        EtaText = "";

        if (_queue.Active.Count > 0)
        {
            var active = _queue.Active[0];
            if (active.Progress != null)
            {
                var p = active.Progress;
                var now = DateTime.UtcNow;
                var dt = (now - _lastSnapshotTime).TotalSeconds;

                if (dt > 0.3 && p.TotalBytes > 0)
                {
                    var bytesDelta = p.TotalBytesDone - _lastBytesDone;
                    if (bytesDelta > 0 && dt > 0)
                    {
                        var speed = bytesDelta / dt;
                        SpeedText = FormatSpeed(speed);
                        HasSpeed = true;

                        if (speed > 0 && p.Percent < 100 && p.TotalBytes > p.TotalBytesDone)
                        {
                            var remaining = (p.TotalBytes - p.TotalBytesDone) / speed;
                            EtaText = FormatEta(remaining);
                            HasEta = true;
                        }

                        _lastBytesDone = p.TotalBytesDone;
                        _lastSnapshotTime = now;
                    }
                }
            }
        }
    }

    private void UpdateOperationsList()
    {
        foreach (var item in AllOperations)
            item.Dispose();
        AllOperations.Clear();

        foreach (var op in _queue.Active)
            AllOperations.Add(new QueuedOperationItem(op));

        foreach (var op in _queue.Pending)
            AllOperations.Add(new QueuedOperationItem(op));

        foreach (var op in _queue.Completed.Take(20))
            AllOperations.Add(new QueuedOperationItem(op));
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 0) return "";
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024:F1} KB/s";
        if (bytesPerSec < 1024L * 1024 * 1024) return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
        return $"{bytesPerSec / (1024L * 1024 * 1024):F2} GB/s";
    }

    private static string FormatEta(double seconds)
    {
        if (seconds <= 0) return "";
        if (seconds < 1) return "< 1 sec";
        if (seconds < 60) return $"~{seconds:F0} sec";
        var min = (int)(seconds / 60);
        var sec = (int)(seconds % 60);
        return sec > 0 ? $"~{min}m {sec}s" : $"~{min} min";
    }

    public void Detach()
    {
        _queue.QueueChanged -= OnQueueChanged;
        _queue.PauseStateChanged -= OnPauseStateChanged;
        foreach (var item in AllOperations)
            item.Dispose();
        AllOperations.Clear();
    }
}

public class QueuedOperationItem : ObservableObject, IDisposable
{
    private readonly QueuedOperation _op;
    private bool _disposed;

    public QueuedOperationItem(QueuedOperation op)
    {
        _op = op;
        _op.PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QueuedOperation.Progress) or nameof(QueuedOperation.Status))
        {
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _op.PropertyChanged -= OnPropertyChanged;
    }

    public string TypeText => _op.OperationType == "Move" ? "MOVE" : "COPY";

    public Brush TypeBrush => _op.OperationType == "Move"
        ? (Application.Current.FindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue)
        : (Application.Current.FindResource("AccentDimBrush") as Brush ?? Brushes.LightSlateGray);

    public string SourcePath => _op.SourcePath;
    public string DestPath => _op.DestPath;
    public string SourceShort => TruncatePath(_op.SourcePath, 25);
    public string DestShort => TruncatePath(_op.DestPath, 25);

    public string StatusText => _op.Status switch
    {
        QueuedOperationStatus.Running => "Run",
        QueuedOperationStatus.Completed => "Done",
        QueuedOperationStatus.Failed => "Fail",
        QueuedOperationStatus.Cancelled => "Cancel",
        _ => "Queue"
    };

    public Brush StatusColor => _op.Status switch
    {
        QueuedOperationStatus.Running => (Application.Current.FindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue),
        QueuedOperationStatus.Completed => (Application.Current.FindResource("OkBrush") as Brush ?? Brushes.LimeGreen),
        QueuedOperationStatus.Failed => (Application.Current.FindResource("ErrBrush") as Brush ?? Brushes.OrangeRed),
        QueuedOperationStatus.Cancelled => (Application.Current.FindResource("WarnBrush") as Brush ?? Brushes.Orange),
        _ => (Application.Current.FindResource("FgDimBrush") as Brush ?? Brushes.Gray)
    };

    public double ProgressValue
    {
        get
        {
            if (_op.Progress != null && _op.Progress.Percent > 0)
                return _op.Progress.Percent;
            return _op.Status == QueuedOperationStatus.Completed ? 100 : 0;
        }
    }

    private static string TruncatePath(string path, int maxLen)
    {
        if (string.IsNullOrEmpty(path)) return "";
        if (path.Length <= maxLen) return path;
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path;
        if (name.Length >= maxLen - 3)
            return "..." + name.Substring(name.Length - maxLen + 3);
        return "..." + path.Substring(path.Length - maxLen + 3);
    }
}
