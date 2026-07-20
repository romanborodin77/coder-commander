using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace CoderCommander.Services;

/// <summary>
/// Сервис для управления SSH-подключениями: запуск удалённых команд, публикация через SCP,
/// управление профилями и проверка доступности.
/// Service for managing SSH connections: running remote commands, publishing via SCP,
/// managing profiles, and reachability checks.
/// </summary>
public sealed class SshService
{
    private readonly IProcessService _proc;

    /// <summary>
    /// Инициализирует новый экземпляр SshService с указанным сервисом процессов.
    /// Initializes a new instance of SshService with the specified process service.
    /// </summary>
    /// <param name="proc">Сервис для запуска внешних процессов. Service for launching external processes.</param>
    public SshService(IProcessService proc) => _proc = proc;

    /// <summary>
    /// Возвращает путь к JSON-файлу с сохранёнными SSH-профилями.
    /// Returns the path to the JSON file storing SSH profiles.
    /// </summary>
    public string ProfilesFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoderCommander", "ssh-profiles.json");

    /// <summary>
    /// Загружает список SSH-профилей из JSON-файла. При ошибке возвращает пустой список.
    /// Loads the list of SSH profiles from the JSON file. Returns an empty list on error.
    /// </summary>
    /// <returns>Список профилей SSH. List of SSH profiles.</returns>
    public List<SshProfile> LoadProfiles()
    {
        try
        {
            if (!File.Exists(ProfilesFile)) return new List<SshProfile>();
            return JsonSerializer.Deserialize<List<SshProfile>>(File.ReadAllText(ProfilesFile)) ?? new List<SshProfile>();
        }
        catch { return new List<SshProfile>(); }
    }

    /// <summary>
    /// Сохраняет список SSH-профилей в JSON-файл с форматированием.
    /// Saves the list of SSH profiles to the JSON file with indented formatting.
    /// </summary>
    /// <param name="profiles">Список профилей для сохранения. List of profiles to save.</param>
    public void SaveProfiles(List<SshProfile> profiles)
    {
        var dir = Path.GetDirectoryName(ProfilesFile);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllText(ProfilesFile, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Публикует локальный файл/директорию на удалённый сервер через SCP (рекурсивно).
    /// Publishes a local file/directory to a remote server via SCP (recursively).
    /// </summary>
    /// <param name="profile">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="localPath">Локальный путь к файлу или директории. Local file or directory path.</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>Результат выполнения процесса SCP. Result of the SCP process execution.</returns>
    public async Task<ProcessResult> PublishAsync(SshProfile profile, string localPath, CancellationToken ct = default)
    {
        var remote = profile.RemotePath.TrimEnd('/') + "/" + Path.GetFileName(localPath);
        var args = new List<string> { "-r", "-P", profile.Port.ToString(), "-o", "StrictHostKeyChecking=accept-new" };
        if (!string.IsNullOrWhiteSpace(profile.IdentityFile)) { args.Add("-i"); args.Add(profile.IdentityFile); }
        args.Add(localPath);
        args.Add($"{profile.User}@{profile.Host}:{remote}");
        return await _proc.RunAsync("scp", args, ct: ct);
    }

    /// <summary>
    /// Выполняет удалённую команду на сервере через SSH с использованием нативного клиента.
    /// Executes a remote command on the server via SSH using the native client.
    /// </summary>
    /// <param name="profile">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="command">Команда для выполнения на удалённом сервере. Command to execute on the remote server.</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>Результат выполнения команды. Result of the command execution.</returns>
    public async Task<ProcessResult> RunRemoteAsync(SshProfile profile, string command, CancellationToken ct = default)
    {
        var args = new List<string> { "-p", profile.Port.ToString(), "-o", "StrictHostKeyChecking=accept-new", "-o", "BatchMode=yes" };
        if (!string.IsNullOrWhiteSpace(profile.IdentityFile)) { args.Add("-i"); args.Add(profile.IdentityFile); }
        args.Add($"{profile.User}@{profile.Host}");
        args.Add(command);
        return await _proc.RunAsync("ssh", args, ct: ct);
    }

    /// <summary>
    /// Строит ConnectionInfo для SSH/SFTP с аутентификацией по приватному ключу, паролю или keyboard-interactive.
    /// Builds ConnectionInfo for SSH/SFTP with authentication via private key, password, or keyboard-interactive.
    /// </summary>
    /// <param name="p">Профиль SSH-подключения. SSH connection profile.</param>
    /// <returns>Настроенный объект ConnectionInfo для Renci.SshNet. Configured ConnectionInfo for Renci.SshNet.</returns>
    internal static ConnectionInfo BuildConnectionInfo(SshProfile p)
    {
        var auth = new List<AuthenticationMethod>();
        // Аутентификация по приватному ключу, если указан путь к файлу.
        if (!string.IsNullOrWhiteSpace(p.IdentityFile) && File.Exists(p.IdentityFile))
        {
            var keyFile = new PrivateKeyFile(p.IdentityFile);
            auth.Add(new PrivateKeyAuthenticationMethod(p.User, keyFile));
        }
        // Аутентификация по паролю (интерактивная/клавиатурная) — используется при отсутствии ключа.
        auth.Add(new PasswordAuthenticationMethod(p.User, ""));
        auth.Add(new KeyboardInteractiveAuthenticationMethod(p.User));

        return new ConnectionInfo(
            p.Host,
            p.Port,
            p.User,
            auth.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(30),
            RetryAttempts = 1
        };
    }

    /// <summary>
    /// Пробный коннект к серверу для проверки доступности профиля.
    /// Tests connectivity to the server to verify the profile is reachable.
    /// </summary>
    /// <param name="p">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>true, если удалось подключиться; false в противном случае. true if connection succeeded; false otherwise.</returns>
    public Task<bool> IsReachableAsync(SshProfile p, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var client = new SshClient(BuildConnectionInfo(p));
                client.Connect();
                var ok = client.IsConnected;
                client.Disconnect();
                return ok;
            }
            catch (OperationCanceledException) { return false; }
            catch { return false; }
        }, ct);
    }
}

/// <summary>
/// Профиль SSH-подключения: хост, порт, пользователь, путь и опциональный ключ.
/// SSH connection profile: host, port, user, remote path, and optional identity file.
/// </summary>
/// <param name="Name">Название профиля. Profile name.</param>
/// <param name="Host">Хост или IP-адрес сервера. Server hostname or IP address.</param>
/// <param name="User">Имя пользователя для подключения. SSH username.</param>
/// <param name="Port">Порт SSH (обычно 22). SSH port (typically 22).</param>
/// <param name="RemotePath">Удалённый путь по умолчанию. Default remote path.</param>
/// <param name="IdentityFile">Путь к файлу приватного ключа (опционально). Path to the private key file (optional).</param>
public sealed record SshProfile(string Name, string Host, string User, int Port, string RemotePath, string? IdentityFile = null)
{
    /// <summary>
    /// Возвращает строку вида user@host:path для использования в SCP.
    /// Returns a user@host:path string for use with SCP.
    /// </summary>
    public string ScpTarget => $"{User}@{Host}:{RemotePath}";
}
