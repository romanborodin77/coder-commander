using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления для управления Docker-контейнерами и образами.
/// ViewModel for managing Docker containers and images.
/// </summary>
public partial class DockerViewModel : ObservableObject
{
    private readonly DockerService _docker;

    /// <summary>
    /// Создаёт экземпляр DockerViewModel с указанным сервисом Docker.
    /// Creates a DockerViewModel instance with the specified Docker service.
    /// </summary>
    /// <param name="docker">Сервис для выполнения Docker-команд.</param>
    public DockerViewModel(DockerService docker) => _docker = docker;

    /// <summary>Видимость панели Docker.</summary>
    [ObservableProperty] private bool _isVisible;
    /// <summary>Список Docker-контейнеров.</summary>
    [ObservableProperty] private ObservableCollection<DockerContainer> _containers = new();
    /// <summary>Список Docker-образов.</summary>
    [ObservableProperty] private ObservableCollection<DockerImage> _images = new();
    /// <summary>Выбранный контейнер.</summary>
    [ObservableProperty] private DockerContainer? _selectedContainer;
    /// <summary>Выбранный образ.</summary>
    [ObservableProperty] private DockerImage? _selectedImage;
    /// <summary>Текст логов выбранного контейнера.</summary>
    [ObservableProperty] private string _logs = "";
    /// <summary>Строка статуса для отображения в UI.</summary>
    [ObservableProperty] private string _status = "";
    /// <summary>Команда для выполнения внутри контейнера.</summary>
    [ObservableProperty] private string _execCommand = "";

    /// <summary>
    /// Устанавливает видимость панели и обновляет данные при показе.
    /// Sets panel visibility and refreshes data when shown.
    /// </summary>
    /// <param name="v">Новое состояние видимости.</param>
    public void SetVisible(bool v)
    {
        IsVisible = v;
        if (v) _ = RefreshAsync();
    }

    /// <summary>Закрыть панель Docker (скрыть вкладку).</summary>
    [RelayCommand] public void Close() => IsVisible = false;
    /// <summary>Открыть панель Docker.</summary>
    [RelayCommand] public void Open() => SetVisible(true);

    /// <summary>Обновить списки контейнеров и образов из Docker.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        Containers = new ObservableCollection<DockerContainer>(await _docker.ContainersAsync());
        Images = new ObservableCollection<DockerImage>(await _docker.ImagesAsync());
        Status = string.Format(LocalizationService.Current.GetString("Docker.StatusFormat"), Containers.Count, Images.Count);
    }

    /// <summary>Запустить выбранный контейнер.</summary>
    [RelayCommand]
    public async Task StartAsync()
    {
        if (SelectedContainer is null) return;
        var r = await _docker.StartAsync(SelectedContainer.Id);
        Status = r.Success ? "Запущен" : "Ошибка: " + r.StdErr;
        await RefreshAsync();
    }

    /// <summary>Остановить выбранный контейнер.</summary>
    [RelayCommand]
    public async Task StopAsync()
    {
        if (SelectedContainer is null) return;
        var r = await _docker.StopAsync(SelectedContainer.Id);
        Status = r.Success ? "Остановлен" : "Ошибка: " + r.StdErr;
        await RefreshAsync();
    }

    /// <summary>Удалить выбранный контейнер.</summary>
    [RelayCommand]
    public async Task RemoveAsync()
    {
        if (SelectedContainer is null) return;
        var r = await _docker.RemoveAsync(SelectedContainer.Id);
        Status = r.Success ? "Удалён" : "Ошибка: " + r.StdErr;
        await RefreshAsync();
    }

    /// <summary>Удалить выбранный образ.</summary>
    [RelayCommand]
    public async Task RemoveImageAsync()
    {
        if (SelectedImage is null) return;
        var r = await _docker.RemoveImageAsync(SelectedImage.Id);
        Status = r.Success ? "Образ удалён" : "Ошибка: " + r.StdErr;
        await RefreshAsync();
    }

    /// <summary>Показать логи выбранного контейнера.</summary>
    [RelayCommand]
    public async Task ShowLogsAsync()
    {
        if (SelectedContainer is null) return;
        Logs = await _docker.LogsAsync(SelectedContainer.Id);
    }

    /// <summary>Выполнить произвольную команду внутри выбранного контейнера.</summary>
    [RelayCommand]
    public async Task ExecInContainerAsync()
    {
        if (SelectedContainer is null || string.IsNullOrWhiteSpace(ExecCommand)) return;
        var r = await _docker.ExecAsync(SelectedContainer.Id, ExecCommand);
        Logs = (r.Success ? r.StdOut : r.StdErr) + "\r\n" + Logs;
    }
}
