using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.Services;
using Microsoft.Win32;

namespace CoderCommander.ViewModels;

/// <summary>
/// Модель представления для управления SSH-профилями и публикации файлов на удалённых серверах.
/// ViewModel for managing SSH profiles and publishing files to remote servers.
/// </summary>
public partial class SshViewModel : ObservableObject
{
    private readonly SshService _ssh;

    /// <summary>
    /// Создаёт экземпляр SshViewModel с указанным сервисом SSH.
    /// Creates an SshViewModel instance with the specified SSH service.
    /// </summary>
    /// <param name="ssh">Сервис для выполнения SSH-команд.</param>
    public SshViewModel(SshService ssh) => _ssh = ssh;

    /// <summary>Видимость панели SSH.</summary>
    [ObservableProperty] private bool _isVisible;
    /// <summary>Список SSH-профилей.</summary>
    [ObservableProperty] private ObservableCollection<SshProfile> _profiles = new();
    /// <summary>Выбранный SSH-профиль.</summary>
    [ObservableProperty] private SshProfile? _selectedProfile;
    /// <summary>Имя профиля для редактирования.</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>Хост профиля.</summary>
    [ObservableProperty] private string _host = "";
    /// <summary>Имя пользователя для подключения.</summary>
    [ObservableProperty] private string _user = "";
    /// <summary>Порт SSH (по умолчанию 22).</summary>
    [ObservableProperty] private int _port = 22;
    /// <summary>Удалённый путь по умолчанию.</summary>
    [ObservableProperty] private string _remotePath = "";
    /// <summary>Строка статуса для отображения в UI.</summary>
    [ObservableProperty] private string _status = "";

    /// <summary>
    /// Устанавливает видимость панели и загружает профили при показе.
    /// Sets panel visibility and loads profiles when shown.
    /// </summary>
    /// <param name="v">Новое состояние видимости.</param>
    public void SetVisible(bool v)
    {
        IsVisible = v;
        if (v) Profiles = new ObservableCollection<SshProfile>(_ssh.LoadProfiles());
    }

    /// <summary>Закрыть панель SSH (скрыть вкладку).</summary>
    [RelayCommand] public void Close() => IsVisible = false;
    /// <summary>Открыть панель SSH.</summary>
    [RelayCommand] public void Open() => SetVisible(true);

    /// <summary>Создать новый профиль, очистив поля редактирования.</summary>
    [RelayCommand]
    public void NewProfile()
    {
        Name = ""; Host = ""; User = ""; Port = 22; RemotePath = "/";
        SelectedProfile = null;
    }

    /// <summary>Загрузить выбранный профиль в поля редактирования.</summary>
    [RelayCommand]
    public void EditProfile(SshProfile? p)
    {
        if (p is null) return;
        SelectedProfile = p; Name = p.Name; Host = p.Host; User = p.User; Port = p.Port; RemotePath = p.RemotePath;
    }

    /// <summary>Сохранить текущий профиль (добавить или обновить существующий).</summary>
    [RelayCommand]
    public void SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Host))
        { Status = LocalizationService.Current.GetString("Ssh.NameHostRequired"); return; }
        var list = _ssh.LoadProfiles();
        var prof = new SshProfile(Name, Host, User, Port, RemotePath, SelectedProfile?.IdentityFile);
        var idx = list.FindIndex(x => x.Name == Name);
        if (idx >= 0) list[idx] = prof; else list.Add(prof);
        _ssh.SaveProfiles(list);
        Profiles = new ObservableCollection<SshProfile>(list);
        Status = LocalizationService.Current.GetString("Ssh.ProfileSaved");
    }

    /// <summary>Удалить указанный SSH-профиль.</summary>
    [RelayCommand]
    public void DeleteProfile(SshProfile? p)
    {
        if (p is null) return;
        var list = _ssh.LoadProfiles();
        list.RemoveAll(x => x.Name == p.Name);
        _ssh.SaveProfiles(list);
        Profiles = new ObservableCollection<SshProfile>(list);
    }

    /// <summary>
    /// Проверяет доступность выбранного SSH-профиля, выполняя пробное подключение.
    /// Checks availability of the selected SSH profile by performing a test connection.
    /// </summary>
    [RelayCommand]
    public async Task CheckConnectionAsync()
    {
        var p = SelectedProfile;
        if (p is null) { Status = LocalizationService.Current.GetString("Ssh.NoProfileSelected"); return; }
        Status = LocalizationService.Current.GetString("Ssh.Checking");
        var ok = await _ssh.IsReachableAsync(p);
        Status = ok
            ? string.Format(LocalizationService.Current.GetString("Ssh.Reachable"), p.Name)
            : string.Format(LocalizationService.Current.GetString("Ssh.Unreachable"), p.Name);
    }

    /// <summary>
    /// Открывает диалог выбора файла приватного ключа для SSH-подключения.
    /// Opens a file dialog to select a private key file for SSH connection.
    /// </summary>
    [RelayCommand]
    public void BrowseIdentityFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationService.Current.GetString("Ssh.Tip.KeyFile"),
            Filter = "Key files (*.pem;*.ppk;*.key)|*.pem;*.ppk;*.key|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true && File.Exists(dlg.FileName))
        {
            if (SelectedProfile is not null)
            {
                var idx = Profiles.IndexOf(SelectedProfile);
                var updated = SelectedProfile with { IdentityFile = dlg.FileName };
                Profiles[idx] = updated;
                SelectedProfile = updated;
            }
        }
    }

    /// <summary>
    /// Публикует локальный файл или папку на удалённом сервере по указанному профилю.
    /// Publishes a local file/folder to the remote server using the specified profile.
    /// </summary>
    /// <param name="profile">SSH-профиль для подключения.</param>
    /// <param name="localPath">Локальный путь к файлу или папке.</param>
    public async Task PublishAsync(SshProfile profile, string localPath)
    {
        var r = await _ssh.PublishAsync(profile, localPath);
        Status = r.Success ? string.Format(LocalizationService.Current.GetString("Ssh.Published"), localPath) : string.Format(LocalizationService.Current.GetString("Status.Error"), r.StdErr);
    }

    /// <summary>
    /// Выполняет произвольную команду на удалённом сервере.
    /// Runs an arbitrary command on the remote server.
    /// </summary>
    /// <param name="profile">SSH-профиль для подключения.</param>
    /// <param name="command">Команда для выполнения.</param>
    public async Task RunRemoteAsync(SshProfile profile, string command)
    {
        var r = await _ssh.RunRemoteAsync(profile, command);
        Status = r.Success ? r.StdOut : string.Format(LocalizationService.Current.GetString("Status.Error"), r.StdErr);
    }
}
