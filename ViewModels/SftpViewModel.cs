using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления браузера удалённой файловой системы (SFTP) для работы с файлами через SSH.
/// ViewModel for the SFTP remote file system browser — browse, download, upload, and manage files via SSH.
/// </summary>
public partial class SftpViewModel : ObservableObject
{
    private readonly SshService _ssh;
    private readonly SftpService _sftp;
    private readonly Func<string>? _getLocalDir;
    private readonly Func<string, string, string?>? _prompt;

    /// <summary>Список элементов удалённой директории.</summary>
    [ObservableProperty] private ObservableCollection<SftpEntryModel> _items = new();
    /// <summary>Список доступных SSH-профилей.</summary>
    [ObservableProperty] private ObservableCollection<SshProfile> _profiles = new();
    /// <summary>Выбранный SSH-профиль для SFTP-подключения.</summary>
    [ObservableProperty] private SshProfile? _selectedProfile;
    /// <summary>Текущий удалённый путь.</summary>
    [ObservableProperty] private string _currentRemotePath = "/";
    /// <summary>Выбранный элемент в списке удалённой директории.</summary>
    [ObservableProperty] private SftpEntryModel? _selectedItem;
    /// <summary>Видимость панели SFTP.</summary>
    [ObservableProperty] private bool _isVisible;
    /// <summary>Флаг установленного SFTP-подключения.</summary>
    [ObservableProperty] private bool _isConnected;
    /// <summary>Строка статуса для отображения в UI.</summary>
    [ObservableProperty] private string _status = LocalizationService.Current.GetString("Sftp.SelectProfileAndConnect");
    /// <summary>Флаг выполнения операции (блокировка UI).</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// Создаёт экземпляр SftpViewModel с необходимыми сервисами и фабричными функциями.
    /// Creates an SftpViewModel instance with required services and factory functions.
    /// </summary>
    /// <param name="ssh">Сервис SSH для проверки доступности.</param>
    /// <param name="sftp">Сервис SFTP для операций с удалённой ФС.</param>
    /// <param name="getLocalDir">Функция, возвращающая текущую локальную директорию для скачивания.</param>
    /// <param name="prompt">Функция для отображения диалога ввода (заголовок, подпись, значение по умолчанию).</param>
    public SftpViewModel(SshService ssh, SftpService sftp, Func<string>? getLocalDir = null, Func<string, string, string?>? prompt = null)
    {
        _ssh = ssh;
        _sftp = sftp;
        _getLocalDir = getLocalDir;
        _prompt = prompt;
    }

    /// <summary>
    /// Устанавливает видимость панели и загружает профили при показе.
    /// Sets panel visibility and loads SSH profiles when shown.
    /// </summary>
    /// <param name="v">Новое состояние видимости.</param>
    public void SetVisible(bool v)
    {
        IsVisible = v;
        if (v) Profiles = new ObservableCollection<SshProfile>(_ssh.LoadProfiles());
    }

    /// <summary>Закрыть панель SFTP (скрыть вкладку).</summary>
    [RelayCommand] public void Close() => IsVisible = false;

    /// <summary>Подключиться к удалённому серверу через SFTP по выбранному профилю.</summary>
    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (SelectedProfile is null) { Status = LocalizationService.Current.GetString("Sftp.ProfileNotSelected"); return; }
        IsBusy = true; Status = LocalizationService.Current.GetString("Sftp.CheckingConnection");
        try
        {
            if (!await _ssh.IsReachableAsync(SelectedProfile))
            {
                Status = string.Format(LocalizationService.Current.GetString("Sftp.ConnectFailed"), SelectedProfile.Host);
                IsConnected = false;
                return;
            }
            IsConnected = true;
            await NavigateToAsync(CurrentRemotePath);
        }
        catch (System.Exception e) { Status = string.Format(LocalizationService.Current.GetString("Status.Error"), e.Message); IsConnected = false; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Переходит к указанной удалённой директории и загружает список её содержимого.
    /// Navigates to the specified remote directory and loads its contents listing.
    /// </summary>
    /// <param name="remotePath">Удалённый путь для перехода.</param>
    public async Task NavigateToAsync(string remotePath)
    {
        if (SelectedProfile is null) return;
        IsBusy = true; Status = string.Format(LocalizationService.Current.GetString("Sftp.LoadingPath"), remotePath);
        try
        {
            var list = await _sftp.ListDirectoryAsync(SelectedProfile, remotePath);
            Items = new ObservableCollection<SftpEntryModel>(list);
            CurrentRemotePath = remotePath;
            Status = string.Format(LocalizationService.Current.GetString("Sftp.ItemsIn"), list.Count, remotePath);
        }
        catch (System.Exception e) { Status = string.Format(LocalizationService.Current.GetString("Status.Error"), e.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>Перейти к родительской директории на удалённом сервере.</summary>
    [RelayCommand]
    public async Task GoUpAsync()
    {
        var path = CurrentRemotePath.TrimEnd('/');
        if (path.Length <= 1) return;
        var idx = path.LastIndexOf('/');
        var parent = idx <= 0 ? "/" : path[..idx];
        await NavigateToAsync(parent);
    }

    /// <summary>Обновить список текущей удалённой директории.</summary>
    [RelayCommand]
    public async Task RefreshAsync() => await NavigateToAsync(CurrentRemotePath);

    /// <summary>Открыть элемент: папка — переход, ".." — вверх, файл — скачивание.</summary>
    [RelayCommand]
    public async Task OpenItemAsync()
    {
        var item = SelectedItem;
        if (item is null) return;
        if (item.IsParent) { await GoUpAsync(); return; }
        if (item.IsDirectory) { await NavigateToAsync(item.FullPath); return; }
        await DownloadToAsync();
    }

    /// <summary>Скачать выбранный файл с удалённого сервера в локальную папку.</summary>
    [RelayCommand]
    public async Task DownloadToAsync()
    {
        var item = GetSelectionOrCurrent();
        if (item is null) { Status = LocalizationService.Current.GetString("Sftp.ItemNotSelected"); return; }
        if (item.IsDirectory) { Status = LocalizationService.Current.GetString("Sftp.DirDownloadNotSupported"); return; }
        var localDir = _getLocalDir?.Invoke() ?? "C:\\";
        var localPath = System.IO.Path.Combine(localDir, item.Name);
        IsBusy = true; Status = string.Format(LocalizationService.Current.GetString("Sftp.Downloading"), item.Name);
        try { await _sftp.DownloadFileAsync(SelectedProfile!, item.FullPath, localPath, new Progress<long>(b => Status = string.Format(LocalizationService.Current.GetString("Sftp.DownloadingBytes"), item.Name, b))); Status = string.Format(LocalizationService.Current.GetString("Sftp.Downloaded"), localPath); }
        catch (System.Exception e) { Status = string.Format(LocalizationService.Current.GetString("Status.Error"), e.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>Загрузить локальный файл на удалённый сервер.</summary>
    [RelayCommand]
    public async Task UploadFromAsync()
    {
        var localDir = _getLocalDir?.Invoke() ?? "C:\\";
        var name = _prompt?.Invoke(LocalizationService.Current.GetString("Sftp.UploadTitle"), LocalizationService.Current.GetString("Sftp.UploadLocalFile"));
        if (string.IsNullOrWhiteSpace(name)) { Status = LocalizationService.Current.GetString("Sftp.SpecifyLocalFile"); return; }
        var localPath = System.IO.Path.Combine(localDir, name);
        if (!System.IO.File.Exists(localPath)) { Status = string.Format(LocalizationService.Current.GetString("Sftp.FileNotFound"), localPath); return; }
        var remotePath = (CurrentRemotePath.TrimEnd('/') + "/" + name);
        IsBusy = true; Status = string.Format(LocalizationService.Current.GetString("Sftp.Uploading"), name);
        try { await _sftp.UploadFileAsync(SelectedProfile!, localPath, remotePath, new Progress<long>(b => Status = string.Format(LocalizationService.Current.GetString("Sftp.UploadingBytes"), name, b))); await RefreshAsync(); }
        catch (System.Exception e) { Status = string.Format(LocalizationService.Current.GetString("Status.Error"), e.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>Создать новую папку на удалённом сервере.</summary>
    [RelayCommand]
    public async Task MakeDirAsync()
    {
        if (SelectedProfile is null) return;
        var name = _prompt?.Invoke(LocalizationService.Current.GetString("Sftp.MkdirTitle"), LocalizationService.Current.GetString("Sftp.FolderName"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var remotePath = CurrentRemotePath.TrimEnd('/') + "/" + name;
        IsBusy = true; Status = string.Format(LocalizationService.Current.GetString("Sftp.Creating"), name);
        try { await _sftp.MakeDirectoryAsync(SelectedProfile, remotePath); await RefreshAsync(); }
        catch (System.Exception e) { Status = string.Format(LocalizationService.Current.GetString("Status.Error"), e.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>Удалить выбранный элемент на удалённом сервере (рекурсивно для папок).</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        var item = GetSelectionOrCurrent();
        if (item is null) { Status = LocalizationService.Current.GetString("Sftp.ItemNotSelected"); return; }
        if (StyledMessageBoxWindow.Show(string.Format(LocalizationService.Current.GetString("Sftp.ConfirmDelete"), item.Name), LocalizationService.Current.GetString("Sftp.DeleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsBusy = true; Status = string.Format(LocalizationService.Current.GetString("Sftp.Deleting"), item.Name);
        try { await _sftp.DeleteRemoteAsync(SelectedProfile!, item.FullPath); await RefreshAsync(); }
        catch (System.Exception e) { Status = string.Format(LocalizationService.Current.GetString("Status.Error"), e.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>Переименовать выбранный элемент на удалённом сервере.</summary>
    [RelayCommand]
    public async Task RenameAsync()
    {
        var item = GetSelectionOrCurrent();
        if (item is null) { Status = LocalizationService.Current.GetString("Sftp.ItemNotSelected"); return; }
        var name = _prompt?.Invoke(LocalizationService.Current.GetString("Dialog.RenameTitle"), LocalizationService.Current.GetString("Dialog.RenameName"));
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        var parent = item.FullPath[..item.FullPath.LastIndexOf('/')];
        var newPath = parent.Length == 0 ? "/" + name : parent + "/" + name;
        IsBusy = true; Status = string.Format(LocalizationService.Current.GetString("Sftp.Renaming"), item.Name);
        try { await _sftp.RenameRemoteAsync(SelectedProfile!, item.FullPath, newPath); await RefreshAsync(); }
        catch (System.Exception e) { Status = string.Format(LocalizationService.Current.GetString("Status.Error"), e.Message); }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Возвращает выбранный элемент, игнорируя служебную запись "..".
    /// Returns the selected item, ignoring the special ".." entry.
    /// </summary>
    /// <returns>Выбранный элемент или null, если выбран "..".</returns>
    public SftpEntryModel? GetSelectionOrCurrent()
    {
        if (SelectedItem is null || SelectedItem.IsParent) return null;
        return SelectedItem;
    }
}
