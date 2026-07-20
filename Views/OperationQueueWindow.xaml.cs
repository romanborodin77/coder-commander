using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CoderCommander.Services;
using CoderCommander.ViewModels;

namespace CoderCommander.Views;

public partial class OperationQueueWindow : Window
{
    private System.Windows.Threading.DispatcherTimer? _refreshTimer;
    private OperationQueueViewModel? _viewModel;
    private bool _forceClose;

    public OperationQueueWindow() : this(new OperationQueueViewModel()) { }

    public OperationQueueWindow(OperationQueueViewModel vm)
    {
        DataContext = vm;
        _viewModel = vm;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;

        // Обработчик запроса закрытия окна (Cancel All).
        // Window close request handler (Cancel All).
        _viewModel.CloseRequested += () =>
        {
            try { Close(); } catch { }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _refreshTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _refreshTimer.Tick += (_, _) => _viewModel?.UpdateAll();
        _refreshTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _refreshTimer?.Stop();
        _viewModel?.Detach();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
        }
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OperationQueueWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_forceClose)
        {
            _viewModel?.CancelAllCommand.Execute(null);
            return;
        }

        Hide();
        ShowInTaskbar = false;
        e.Cancel = true;
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    public void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }
}
