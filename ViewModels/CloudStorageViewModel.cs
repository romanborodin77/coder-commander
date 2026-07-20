using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Views;
using Google.Apis.Auth.OAuth2;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления панели облачных хранилищ: профили, подключение, браузер файлов.
/// ViewModel for the cloud storage panel: profiles, connection, file browser.
/// </summary>
public partial class CloudStorageViewModel : ObservableObject
{
    private readonly CloudStorageService _cloudService;
    private readonly Func<string>? _getLocalDir;
    private readonly Func<string, string, string?>? _prompt;
    private CancellationTokenSource? _cts;

    /// <summary>Список профилей облачных хранилищ. / Cloud storage profile list.</summary>
    [ObservableProperty] private ObservableCollection<CloudProfile> _profiles = new();

    /// <summary>Выбранный профиль. / Selected profile.</summary>
    [ObservableProperty] private CloudProfile? _selectedProfile;

    /// <summary>Элементы текущего каталога. / Current directory items.</summary>
    [ObservableProperty] private ObservableCollection<FileEntry> _items = new();

    /// <summary>Выбранный элемент. / Selected item.</summary>
    [ObservableProperty] private FileEntry? _selectedItem;

    /// <summary>Текущий путь. / Current path.</summary>
    [ObservableProperty] private string _currentPath = "/";

    /// <summary>Подключено ли хранилище. / Whether the storage is connected.</summary>
    [ObservableProperty] private bool _isConnected;

    /// <summary>Видимость панели. / Panel visibility.</summary>
    [ObservableProperty] private bool _isVisible;

    /// <summary>Строка статуса. / Status string.</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>Флаг выполнения операции. / Busy flag.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Активное облачное подключение. / Active cloud connection.</summary>
    private CloudFileSystem? _activeFs;

    /// <summary>Google Drive Client ID (из текущего профиля). / Google Drive Client ID (from current profile).</summary>
    [ObservableProperty] private string _gDriveClientId = "";

    /// <summary>Google Drive Client Secret (из текущего профиля). / Google Drive Client Secret (from current profile).</summary>
    [ObservableProperty] private string _gDriveClientSecret = "";

    /// <summary>Статус авторизации Google Drive. / Google Drive authorization status.</summary>
    [ObservableProperty] private string _gDriveStatus = "";

    /// <summary>Видимость панели конфигурации Google Drive. / Google Drive config panel visibility.</summary>
    [ObservableProperty] private bool _isGDriveConfigVisible;

    /// <summary>Выполняется ли авторизация Google Drive. / Whether Google Drive authorization is in progress.</summary>
    [ObservableProperty] private bool _isGDriveAuthorizing;

    /// <summary>
    /// Создаёт экземпляр CloudStorageViewModel.
    /// Creates a CloudStorageViewModel instance.
    /// </summary>
    public CloudStorageViewModel(
        CloudStorageService cloudService,
        Func<string>? getLocalDir = null,
        Func<string, string, string?>? prompt = null)
    {
        _cloudService = cloudService;
        _getLocalDir = getLocalDir;
        _prompt = prompt;
    }

    /// <summary>
    /// Устанавливает видимость панели и загружает профили.
    /// Sets panel visibility and loads profiles.
    /// </summary>
    public void SetVisible(bool v)
    {
        IsVisible = v;
        if (v) LoadProfiles();
    }

    /// <summary>
    /// Обновляет свойства Google Drive при смене выбранного профиля.
    /// Updates Google Drive properties when the selected profile changes.
    /// </summary>
    partial void OnSelectedProfileChanged(CloudProfile? value)
    {
        if (value?.Provider == CloudProvider.GoogleDrive)
        {
            value.Credentials.TryGetValue("ClientId", out var cid);
            value.Credentials.TryGetValue("ClientSecret", out var cs);
            GDriveClientId = cid ?? "";
            GDriveClientSecret = cs ?? "";
            var hasToken = value.Credentials.ContainsKey("RefreshToken")
                           && !string.IsNullOrEmpty(value.Credentials["RefreshToken"]);
            GDriveStatus = hasToken ? LocalizationService.Current.GetString("Cloud.GDrive.AuthorizedStatus") : LocalizationService.Current.GetString("Cloud.GDrive.NotAuthorizedStatus");
            IsGDriveConfigVisible = true;
        }
        else
        {
            IsGDriveConfigVisible = false;
            GDriveStatus = "";
        }
    }

    /// <summary>Загружает профили из настроек. / Loads profiles from settings.</summary>
    public void LoadProfiles()
    {
        var list = _cloudService.GetProfiles();
        Profiles = new ObservableCollection<CloudProfile>(list);
    }

    /// <summary>Закрыть панель. / Close panel.</summary>
    [RelayCommand] public void Close() => IsVisible = false;

    /// <summary>
    /// Запускает OAuth2-авторизацию Google Drive: открывает браузер, получает код, обменивает на Refresh Token.
    /// Launches Google Drive OAuth2 authorization: opens browser, gets code, exchanges for Refresh Token.
    /// </summary>
    [RelayCommand]
    public async Task AuthorizeGoogleDriveAsync()
    {
        if (SelectedProfile is null || SelectedProfile.Provider != CloudProvider.GoogleDrive)
        {
            Status = LocalizationService.Current.GetString("Cloud.GDrive.SelectProfileFirst");
            return;
        }

        // Сохраняем введённые ClientId/ClientSecret в профиль.
        // Save entered ClientId/ClientSecret to profile.
        SelectedProfile.Credentials["ClientId"] = GDriveClientId;
        SelectedProfile.Credentials["ClientSecret"] = GDriveClientSecret;
        _cloudService.UpdateProfile(SelectedProfile);

        if (string.IsNullOrWhiteSpace(GDriveClientId) || string.IsNullOrWhiteSpace(GDriveClientSecret))
        {
            GDriveStatus = LocalizationService.Current.GetString("Cloud.GDrive.NeedCredentials");
            return;
        }

        IsGDriveAuthorizing = true;
        GDriveStatus = LocalizationService.Current.GetString("Cloud.GDrive.OpenBrowser");
        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var code = await GoogleOAuthService.GetAuthorizationCodeAsync(GDriveClientId, ct: _cts.Token);
            GDriveStatus = LocalizationService.Current.GetString("Cloud.GDrive.ExchangeCode");

            var refreshToken = await GoogleOAuthService.ExchangeCodeForRefreshTokenAsync(
                code, GDriveClientId, GDriveClientSecret, _cts.Token);

            SelectedProfile.Credentials["RefreshToken"] = refreshToken;
            _cloudService.UpdateProfile(SelectedProfile);

            GDriveStatus = LocalizationService.Current.GetString("Cloud.GDrive.Authorized");
            Status = LocalizationService.Current.GetString("Cloud.GDrive.AuthSuccess");
            LoadProfiles();
            // Восстанавливаем SelectedProfile после перезагрузки.
            // Restore SelectedProfile after reload.
            SelectedProfile = Profiles.FirstOrDefault(p => p.Id == SelectedProfile.Id);
        }
        catch (OperationCanceledException)
        {
            GDriveStatus = LocalizationService.Current.GetString("Cloud.GDrive.AuthCancelled");
            Status = LocalizationService.Current.GetString("Cloud.AuthCancelledStatus");
        }
        catch (Exception ex)
        {
            GDriveStatus = string.Format(LocalizationService.Current.GetString("Cloud.GDrive.AuthError"), ex.Message);
            Status = string.Format(LocalizationService.Current.GetString("Cloud.Error"), ex.Message);
            LogService.Error($"Google OAuth failed: {ex.Message}", nameof(CloudStorageViewModel), ex);
        }
        finally
        {
            IsGDriveAuthorizing = false;
        }
    }

    /// <summary>
    /// Сохраняет Client ID и Client Secret в текущий профиль.
    /// Saves Client ID and Client Secret to the current profile.
    /// </summary>
    [RelayCommand]
    public void SaveGDriveCredentials()
    {
        if (SelectedProfile is null || SelectedProfile.Provider != CloudProvider.GoogleDrive) return;
        SelectedProfile.Credentials["ClientId"] = GDriveClientId;
        SelectedProfile.Credentials["ClientSecret"] = GDriveClientSecret;
        _cloudService.UpdateProfile(SelectedProfile);
        Status = LocalizationService.Current.GetString("Cloud.GDrive.CredentialsSaved");
    }

    /// <summary>Добавить новый профиль. / Add new profile.</summary>
    [RelayCommand]
    public void AddProfile()
    {
        var name = _prompt?.Invoke("Новый профиль", "Имя профиля:");
        if (string.IsNullOrWhiteSpace(name)) return;

        // Запрашиваем провайдер / Ask for provider.
        var providerStr = _prompt?.Invoke("Тип хранилища", "Провайдер (S3 / AzureBlob / YandexDisk / GoogleDrive / NextCloud):");
        var provider = providerStr?.Trim().ToLowerInvariant() switch
        {
            "s3" or "aws" => CloudProvider.S3,
            "azureblob" or "azure" => CloudProvider.AzureBlob,
            "yandexdisk" or "yandex" or "yd" => CloudProvider.YandexDisk,
            "googledrive" or "gdrive" => CloudProvider.GoogleDrive,
            "nextcloud" or "nc" or "webdav" => CloudProvider.NextCloud,
            _ => CloudProvider.S3
        };

        var profile = new CloudProfile { Name = name, Provider = provider };

        // Запрашиваем учётные данные в зависимости от провайдера.
        // Ask for credentials depending on provider.
        switch (provider)
        {
            case CloudProvider.YandexDisk:
                var token = _prompt?.Invoke("Yandex Disk", "OAuth токен:");
                if (!string.IsNullOrWhiteSpace(token))
                    profile.Credentials["OAuthToken"] = token.Trim();
                var root = _prompt?.Invoke("Yandex Disk", "Корневая папка (по умолчанию /):");
                if (!string.IsNullOrWhiteSpace(root))
                    profile.RootPath = root.Trim();
                break;

            case CloudProvider.S3:
                var ak = _prompt?.Invoke("S3", "Access Key:");
                if (!string.IsNullOrWhiteSpace(ak)) profile.Credentials["AccessKey"] = ak.Trim();
                var sk = _prompt?.Invoke("S3", "Secret Key:");
                if (!string.IsNullOrWhiteSpace(sk)) profile.Credentials["SecretKey"] = sk.Trim();
                var region = _prompt?.Invoke("S3", "Region (us-east-1):");
                profile.Region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region.Trim();
                var bucket = _prompt?.Invoke("S3", "Bucket:");
                if (!string.IsNullOrWhiteSpace(bucket)) profile.BucketOrContainer = bucket.Trim();
                break;

            case CloudProvider.AzureBlob:
                var cs = _prompt?.Invoke("Azure Blob", "Connection String:");
                if (!string.IsNullOrWhiteSpace(cs)) profile.Credentials["ConnectionString"] = cs.Trim();
                var azContainer = _prompt?.Invoke("Azure Blob", "Container:");
                if (!string.IsNullOrWhiteSpace(azContainer)) profile.BucketOrContainer = azContainer.Trim();
                break;

            case CloudProvider.GoogleDrive:
                var cid = _prompt?.Invoke("Google Drive", "Client ID:");
                if (!string.IsNullOrWhiteSpace(cid)) profile.Credentials["ClientId"] = cid.Trim();
                var csecret = _prompt?.Invoke("Google Drive", "Client Secret:");
                if (!string.IsNullOrWhiteSpace(csecret)) profile.Credentials["ClientSecret"] = csecret.Trim();
                var rt = _prompt?.Invoke("Google Drive", "Refresh Token:");
                if (!string.IsNullOrWhiteSpace(rt)) profile.Credentials["RefreshToken"] = rt.Trim();
                break;

            case CloudProvider.NextCloud:
                var ncServer = _prompt?.Invoke("NextCloud", "Server URL (https://cloud.example.com):");
                if (!string.IsNullOrWhiteSpace(ncServer)) profile.Endpoint = ncServer.Trim().TrimEnd('/');
                var ncUser = _prompt?.Invoke("NextCloud", "Username:");
                if (!string.IsNullOrWhiteSpace(ncUser)) profile.Credentials["Username"] = ncUser.Trim();
                var ncPass = _prompt?.Invoke("NextCloud", "Password (App Password recommended):");
                if (!string.IsNullOrWhiteSpace(ncPass)) profile.Credentials["Password"] = ncPass.Trim();
                var ncRoot = _prompt?.Invoke("NextCloud", "Root path (default /):");
                if (!string.IsNullOrWhiteSpace(ncRoot)) profile.RootPath = ncRoot.Trim();
                break;
        }

        _cloudService.AddProfile(profile);
        LoadProfiles();
    }

    /// <summary>Удалить выбранный профиль. / Delete selected profile.</summary>
    [RelayCommand]
    public void DeleteProfile()
    {
        if (SelectedProfile is null) return;
        var result = StyledMessageBoxWindow.Show(
            string.Format(LocalizationService.Current.GetString("Cloud.DeleteProfileConfirm"), SelectedProfile.Name),
            LocalizationService.Current.GetString("Cloud.DeleteProfileTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _cloudService.DeleteProfile(SelectedProfile.Id);
        LoadProfiles();
    }

    /// <summary>Подключиться к облачному хранилищу по выбранному профилю. / Connect to cloud storage by selected profile.</summary>
    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (SelectedProfile is null)
        {
            Status = LocalizationService.Current.GetString("Cloud.ProfileNotSelected");
            return;
        }
        IsBusy = true;
        Status = LocalizationService.Current.GetString("Cloud.ConnectingStatus");
        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            // Отключаем предыдущее / Disconnect previous.
            if (_activeFs is not null)
            {
                await _activeFs.DisconnectAsync();
                (_activeFs as IDisposable)?.Dispose();
                _activeFs = null;
            }

            _activeFs = await _cloudService.ConnectAsync(SelectedProfile, _cts.Token);
            IsConnected = true;
            CurrentPath = "/";
            await NavigateToAsync("/");
            Status = string.Format(LocalizationService.Current.GetString("Cloud.ConnectedTo"), SelectedProfile.Name);
        }
        catch (OperationCanceledException)
        {
            Status = LocalizationService.Current.GetString("Cloud.Cancelled");
        }
        catch (Exception e)
        {
            Status = string.Format(LocalizationService.Current.GetString("Cloud.Error"), e.Message);
            IsConnected = false;
            LogService.Error($"Cloud connect failed: {e.Message}", nameof(CloudStorageViewModel), e);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Отключиться от облачного хранилища. / Disconnect from cloud storage.</summary>
    [RelayCommand]
    public async Task DisconnectAsync()
    {
        if (_activeFs is not null)
        {
            await _activeFs.DisconnectAsync();
            (_activeFs as IDisposable)?.Dispose();
            _activeFs = null;
        }
        IsConnected = false;
        Items.Clear();
        Status = LocalizationService.Current.GetString("Cloud.DisconnectedStatus");
    }

    /// <summary>Переходит к указанному пути и загружает содержимое. / Navigates to a path and loads contents.</summary>
    public async Task NavigateToAsync(string path)
    {
        if (_activeFs is null || !_activeFs.IsConnected) return;
        IsBusy = true;
        Status = string.Format(LocalizationService.Current.GetString("Cloud.LoadingFiles"), path);
        try
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var entries = await _activeFs.EnumerateAsync(path, ct: _cts.Token);
            Items = new ObservableCollection<FileEntry>(entries);
            CurrentPath = path;
            Status = string.Format(LocalizationService.Current.GetString("Status.ItemsIn"), entries.Count, path);
        }
        catch (OperationCanceledException)
        {
            Status = LocalizationService.Current.GetString("Cloud.Cancelled");
        }
        catch (Exception e)
        {
            Status = string.Format(LocalizationService.Current.GetString("Cloud.Error"), e.Message);
            LogService.Error($"Cloud enumerate failed: {e.Message}", nameof(CloudStorageViewModel), e);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Перейти к родительской директории. / Go to parent directory.</summary>
    [RelayCommand]
    public async Task GoUpAsync()
    {
        var path = CurrentPath.TrimEnd('/');
        if (path.Length <= 1) return;
        var idx = path.LastIndexOf('/');
        var parent = idx <= 0 ? "/" : path[..idx];
        await NavigateToAsync(parent);
    }

    /// <summary>Обновить список текущей директории. / Refresh current directory listing.</summary>
    [RelayCommand]
    public async Task RefreshAsync() => await NavigateToAsync(CurrentPath);

    /// <summary>Открыть элемент: папка — переход, файл — скачать. / Open item: directory — navigate, file — download.</summary>
    [RelayCommand]
    public async Task OpenItemAsync()
    {
        var item = SelectedItem;
        if (item is null) return;
        if (item.IsDirectory)
        {
            await NavigateToAsync(item.FullPath);
            return;
        }
        await DownloadToAsync();
    }

    /// <summary>Скачать выбранный файл. / Download selected file.</summary>
    [RelayCommand]
    public async Task DownloadToAsync()
    {
        var item = SelectedItem;
        if (item is null || item.IsDirectory) { Status = LocalizationService.Current.GetString("Cloud.ItemNotSelectedOrDir"); return; }
        if (_activeFs is null) { Status = LocalizationService.Current.GetString("Cloud.NoConnectionStatus"); return; }

        var localDir = _getLocalDir?.Invoke() ?? "C:\\";
        var localPath = System.IO.Path.Combine(localDir, item.Name);
        IsBusy = true;
        Status = string.Format(LocalizationService.Current.GetString("Cloud.DownloadingFile"), item.Name);
        try
        {
            if (_activeFs is S3FileSystem s3)
                await s3.DownloadFileAsync(item.FullPath, localPath, null);
            else if (_activeFs is AzureBlobFileSystem az)
                await az.DownloadFileAsync(item.FullPath, localPath, null);
            else if (_activeFs is YandexDiskFileSystem yd)
                await yd.DownloadFileAsync(item.FullPath, localPath, null);
            else if (_activeFs is NextCloudFileSystem nc)
                await nc.DownloadFileAsync(item.FullPath, localPath, null);
            else if (_activeFs is GDriveFileSystem gdrive)
                await gdrive.DownloadFileAsync(item.FullPath, localPath, null);
            else
                throw new NotSupportedException($"Download not supported for {_activeFs.Name}");

            Status = string.Format(LocalizationService.Current.GetString("Cloud.Downloaded"), localPath);
        }
        catch (Exception e)
        {
            Status = string.Format(LocalizationService.Current.GetString("Cloud.Error"), e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Загрузить локальный файл. / Upload local file.</summary>
    [RelayCommand]
    public async Task UploadFromAsync()
    {
        if (_activeFs is null) { Status = LocalizationService.Current.GetString("Cloud.NoConnectionStatus"); return; }
        var localDir = _getLocalDir?.Invoke() ?? "C:\\";
        var name = _prompt?.Invoke(LocalizationService.Current.GetString("Cloud.UploadTitle"), LocalizationService.Current.GetString("Cloud.UploadLocalFile"));
        if (string.IsNullOrWhiteSpace(name)) { Status = LocalizationService.Current.GetString("Cloud.SpecifyFileName"); return; }
        var localPath = System.IO.Path.Combine(localDir, name);
        if (!System.IO.File.Exists(localPath)) { Status = string.Format(LocalizationService.Current.GetString("Cloud.FileNotFound"), localPath); return; }
        var remotePath = CurrentPath.TrimEnd('/') + "/" + name;
        IsBusy = true;
        Status = string.Format(LocalizationService.Current.GetString("Cloud.UploadingFile"), name);
        try
        {
            if (_activeFs is S3FileSystem s3)
                await s3.UploadFileAsync(localPath, remotePath, null);
            else if (_activeFs is AzureBlobFileSystem az)
                await az.UploadFileAsync(localPath, remotePath, null);
            else if (_activeFs is YandexDiskFileSystem yd)
                await yd.UploadFileAsync(localPath, remotePath, null);
            else if (_activeFs is NextCloudFileSystem nc)
                await nc.UploadFileAsync(localPath, remotePath, null);
            else if (_activeFs is GDriveFileSystem gdrive)
                await gdrive.UploadFileAsync(localPath, remotePath, null);
            else
                throw new NotSupportedException($"Upload not supported for {_activeFs.Name}");

            await RefreshAsync();
            Status = string.Format(LocalizationService.Current.GetString("Cloud.Uploaded"), name);
        }
        catch (Exception e)
        {
            Status = string.Format(LocalizationService.Current.GetString("Cloud.Error"), e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Создать папку в облачном хранилище. / Create folder in cloud storage.</summary>
    [RelayCommand]
    public async Task MakeDirAsync()
    {
        if (_activeFs is null) return;
        var name = _prompt?.Invoke(LocalizationService.Current.GetString("Cloud.CreateFolderTitle"), LocalizationService.Current.GetString("Cloud.FolderName"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var remotePath = CurrentPath.TrimEnd('/') + "/" + name;
        IsBusy = true;
        Status = string.Format(LocalizationService.Current.GetString("Cloud.Creating"), name);
        try
        {
            await _activeFs.CreateDirectoryAsync(remotePath);
            await RefreshAsync();
        }
        catch (Exception e)
        {
            Status = string.Format(LocalizationService.Current.GetString("Status.Error"), e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Удалить выбранный элемент. / Delete selected item.</summary>
    [RelayCommand]
    public async Task DeleteAsync()
    {
        var item = SelectedItem;
        if (item is null) { Status = LocalizationService.Current.GetString("Cloud.ItemNotSelected"); return; }
        if (_activeFs is null) { Status = LocalizationService.Current.GetString("Cloud.NoConnectionStatus"); return; }

        var result = StyledMessageBoxWindow.Show(
            string.Format(LocalizationService.Current.GetString("Cloud.ConfirmDelete"), item.Name),
            LocalizationService.Current.GetString("Cloud.ConfirmDeleteTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        Status = string.Format(LocalizationService.Current.GetString("Cloud.DeletingFile"), item.Name);
        try
        {
            await _activeFs.DeleteAsync(item.FullPath, item.IsDirectory);
            await RefreshAsync();
        }
        catch (Exception e)
        {
            Status = string.Format(LocalizationService.Current.GetString("Cloud.Error"), e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Переименовать выбранный элемент. / Rename selected item.</summary>
    [RelayCommand]
    public async Task RenameAsync()
    {
        var item = SelectedItem;
        if (item is null) { Status = LocalizationService.Current.GetString("Cloud.ItemNotSelected"); return; }
        if (_activeFs is null) { Status = LocalizationService.Current.GetString("Cloud.NoConnectionStatus"); return; }

        var name = _prompt?.Invoke(LocalizationService.Current.GetString("Cloud.RenameTitle"), LocalizationService.Current.GetString("Cloud.RenameName"));
        if (string.IsNullOrWhiteSpace(name) || name == item.Name) return;
        var parent = GetParentPath(item.FullPath);
        var newPath = parent.TrimEnd('/') + "/" + name;

        IsBusy = true;
        Status = string.Format(LocalizationService.Current.GetString("Cloud.RenamingFile"), item.Name);
        try
        {
            await _activeFs.MoveAsync(item.FullPath, newPath);
            await RefreshAsync();
        }
        catch (Exception e)
        {
            Status = string.Format(LocalizationService.Current.GetString("Cloud.Error"), e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Возвращает активную облачную файловую систему (для Cross-VFS операций).
    /// Returns the active cloud file system (for Cross-VFS operations).
    /// </summary>
    public CloudFileSystem? GetActiveFileSystem() => _activeFs;

    private static string GetParentPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        var idx = trimmed.LastIndexOf('/');
        return idx <= 0 ? "/" : trimmed[..idx];
    }
}
