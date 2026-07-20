using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoderCommander.Services;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace CoderCommander.FileSystem;

/// <summary>
/// Реализация <see cref="IFileSystem"/> для удалённой файловой системы через SFTP (SSH.NET).
/// IFileSystem implementation for a remote file system via SFTP (SSH.NET).
/// Каждый метод открывает/закрывает SftpClient — потокобезопасно, без compartir-соединений.
/// Each method opens/closes an SftpClient — thread-safe, no shared connections.
/// </summary>
public sealed class SftpFileSystem : IFileSystem
{
    private readonly Func<SftpClient> _clientFactory;

    /// <summary>Хост удалённого сервера. / Remote server host.</summary>
    public string Host { get; }

    /// <summary>Имя пользователя SSH. / SSH username.</summary>
    public string Username { get; }

    /// <summary>Корневой путь по умолчанию (для отображения). / Default root path (for display).</summary>
    public string RootPath { get; }

    /// <inheritdoc/>
    public string Name => $"SFTP ({Host})";

    /// <summary>
    /// Создаёт SFTP-filesystem по параметрам подключения.
    /// Creates an SFTP filesystem from connection parameters.
    /// </summary>
    /// <param name="host">Хост или IP. / Host or IP address.</param>
    /// <param name="username">Имя пользователя. / SSH username.</param>
    /// <param name="password">Пароль (может быть пустым для key-auth). / Password (may be empty for key auth).</param>
    /// <param name="keyFile">Путь к файлу приватного ключа (опционально). / Path to private key file (optional).</param>
    /// <param name="port">Порт SSH (по умолчанию 22). / SSH port (default 22).</param>
    /// <param name="rootPath">Корневой путь (по умолчанию "/"). / Root path (default "/").</param>
    public SftpFileSystem(string host, string username, string? password, string? keyFile, int port = 22, string rootPath = "/")
    {
        Host = host;
        Username = username;
        RootPath = rootPath;

        _clientFactory = () =>
        {
            var auth = new List<AuthenticationMethod>();
            if (!string.IsNullOrWhiteSpace(keyFile) && File.Exists(keyFile))
                auth.Add(new PrivateKeyAuthenticationMethod(username, new PrivateKeyFile(keyFile)));
            if (!string.IsNullOrEmpty(password))
                auth.Add(new PasswordAuthenticationMethod(username, password));
            auth.Add(new KeyboardInteractiveAuthenticationMethod(username));

            var connInfo = new ConnectionInfo(host, port, username, auth.ToArray())
            {
                Timeout = TimeSpan.FromSeconds(30),
                RetryAttempts = 1
            };
            return new SftpClient(connInfo);
        };
    }

    /// <summary>
    /// Создаёт SFTP-filesystem из существующего SSH-профиля.
    /// Creates an SFTP filesystem from an existing SSH profile.
    /// </summary>
    /// <param name="profile">Профиль SSH-подключения. / SSH connection profile.</param>
    /// <param name="rootPath">Корневой путь (если null — берётся из профиля). / Root path (if null, taken from profile).</param>
    public SftpFileSystem(SshProfile profile, string? rootPath = null)
        : this(profile.Host, profile.User, null, profile.IdentityFile, profile.Port, rootPath ?? profile.RemotePath)
    {
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FileEntry>> EnumerateAsync(string path, bool includeHidden = false, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var client = CreateClient();
            client.Connect();
            try
            {
                var result = new List<FileEntry>();
                foreach (var entry in client.ListDirectory(path))
                {
                    ct.ThrowIfCancellationRequested();
                    // Пропускаем служебные записи "." и ".." / Skip "." and ".." entries.
                    if (entry.Name == "." || entry.Name == "..") continue;

                    // Фильтрация скрытых файлов: на Linux начинаются с "." / Hidden filter: on Linux starts with ".".
                    if (!includeHidden && entry.Name.StartsWith('.')) continue;

                    var fullPath = NormalizePath(path, entry.Name);
                    var attrs = entry.Attributes;

                    result.Add(new FileEntry(
                        fullPath: fullPath,
                        isDirectory: entry.IsDirectory,
                        exists: true,
                        size: entry.IsDirectory ? 0 : (long)entry.Length,
                        attributes: entry.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                        createdTimeUtc: default,
                        lastWriteTimeUtc: attrs.LastWriteTimeUtc,
                        lastAccessTimeUtc: attrs.LastAccessTimeUtc));
                }

                // Сначала папки, затем файлы, внутри — по имени / Directories first, then files, sorted by name.
                result.Sort((a, b) =>
                {
                    if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
                    return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
                return (IReadOnlyList<FileEntry>)result;
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <inheritdoc/>
    public Task<FileEntry?> GetFileInfoAsync(string path, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                if (!client.Exists(path)) return (FileEntry?)null;

                var attrs = client.GetAttributes(path);
                var name = Path.GetFileName(path.TrimEnd('/'));
                if (string.IsNullOrEmpty(name)) name = path; // корень / root

                return new FileEntry(
                    fullPath: path,
                    isDirectory: attrs.IsDirectory,
                    exists: true,
                    size: attrs.IsDirectory ? 0 : (long)attrs.Size,
                    attributes: attrs.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
                    createdTimeUtc: default,
                    lastWriteTimeUtc: attrs.LastWriteTimeUtc,
                    lastAccessTimeUtc: attrs.LastAccessTimeUtc);
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                return client.Exists(path);
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Внутреннее SFTP→SFTP копирование: создаём временный файл, скачиваем,
    /// загружаем на новое место, удаляем временный. Для больших файлов
    /// лучше использовать <see cref="CrossVfsCopyOperation"/> с прогрессом.
    /// Internal SFTP→SFTP copy: creates a temp file, downloads, uploads to
    /// the new path, deletes the temp file. For large files, prefer
    /// <see cref="CrossVfsCopyOperation"/> for progress support.
    /// </remarks>
    public Task CopyAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                if (!client.Exists(source))
                    throw new FileNotFoundException($"Source not found: {source}");

                var srcAttrs = client.GetAttributes(source);
                if (srcAttrs.IsDirectory)
                {
                    // Рекурсивное копирование каталога / Recursive directory copy.
                    client.CreateDirectory(destination);
                    foreach (var entry in client.ListDirectory(source))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (entry.Name == "." || entry.Name == "..") continue;
                        var srcChild = NormalizePath(source, entry.Name);
                        var dstChild = NormalizePath(destination, entry.Name);
                        CopySftpEntry(client, srcChild, dstChild, overwrite, ct);
                    }
                }
                else
                {
                    CopySftpEntry(client, source, destination, overwrite, ct);
                }
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <summary>
    /// Копирует один файл внутри SFTP через временный локальный файл.
    /// Copies a single file within SFTP via a temporary local file.
    /// </summary>
    private static void CopySftpEntry(SftpClient client, string source, string destination, bool overwrite, CancellationToken ct)
    {
        if (client.Exists(destination) && !overwrite)
            throw new IOException($"Destination already exists: {destination}");

        var tmpPath = Path.Combine(Path.GetTempPath(), $"sftp_copy_{Guid.NewGuid():N}");
        try
        {
            // Скачиваем во временный файл / Download to temp file.
            using (var tmpFs = File.Create(tmpPath))
            {
                client.DownloadFile(source, tmpFs, _ => ct.ThrowIfCancellationRequested());
            }

            // Загружаем на место назначения / Upload to destination.
            using (var tmpFs = File.OpenRead(tmpPath))
            {
                client.UploadFile(tmpFs, destination, uploaded => ct.ThrowIfCancellationRequested());
            }
        }
        finally
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* best effort */ }
        }
    }

    /// <inheritdoc/>
    public Task MoveAsync(string source, string destination, bool overwrite = false, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                if (!client.Exists(source))
                    throw new FileNotFoundException($"Source not found: {source}");

                if (client.Exists(destination) && !overwrite)
                    throw new IOException($"Destination already exists: {destination}");

                if (client.Exists(destination))
                {
                    // Удаляем назначение перед переименованием / Remove destination before rename.
                    var dstAttrs = client.GetAttributes(destination);
                    if (dstAttrs.IsDirectory) client.DeleteDirectory(destination);
                    else client.DeleteFile(destination);
                }

                // SFTP не различает Rename и Move (всё в пределах одного сервера) / SFTP doesn't distinguish Rename and Move.
                client.RenameFile(source, destination);
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string path, bool recursive = false, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                if (!client.Exists(path)) return;

                var attrs = client.GetAttributes(path);
                if (attrs.IsDirectory)
                {
                    if (recursive) DeleteRecursive(client, path, ct);
                    else client.DeleteDirectory(path);
                }
                else
                {
                    client.DeleteFile(path);
                }
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <summary>
    /// Рекурсивно удаляет каталог и его содержимое.
    /// Recursively deletes a directory and its contents.
    /// </summary>
    private static void DeleteRecursive(SftpClient client, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var entry in client.ListDirectory(path))
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Name == "." || entry.Name == "..") continue;
            if (entry.IsDirectory) DeleteRecursive(client, entry.FullName, ct);
            else client.DeleteFile(entry.FullName);
        }
        client.DeleteDirectory(path);
    }

    /// <inheritdoc/>
    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                // SSH.NET CreateDirectory выбрасывает исключение, если каталог уже существует.
                // SSH.NET CreateDirectory throws if the directory already exists.
                if (!client.Exists(path))
                    client.CreateDirectory(path);
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">SFTP не поддерживает установку атрибутов через эту абстракцию.</exception>
    public Task SetAttributesAsync(string path, FileAttributes attributes, CancellationToken ct = default)
    {
        return Task.FromException(new NotSupportedException("SFTP: SetAttributes is not supported through this abstraction. Use SftpClient.SetAttributes directly."));
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">SFTP не поддерживает жёсткие ссылки.</exception>
    public Task CreateHardlinkAsync(string source, string linkPath, CancellationToken ct = default)
    {
        return Task.FromException(new NotSupportedException("SFTP: Hard links are not supported."));
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">SFTP не поддерживает символические ссылки через эту абстракцию.</exception>
    public Task CreateSymbolicLinkAsync(string target, string linkPath, bool isDirectory = false, CancellationToken ct = default)
    {
        return Task.FromException(new NotSupportedException("SFTP: Symbolic links are not supported through this abstraction."));
    }

    #region Internal stream helpers (for CrossVfsCopyOperation)

    /// <summary>
    /// Загружает локальный файл на SFTP-сервер с отчётом прогресса (байты).
    /// Uploads a local file to the SFTP server with byte-level progress reporting.
    /// Используется <see cref="Operations.CrossVfsCopyOperation"/> для кросс-VFS передачи.
    /// Used by CrossVfsCopyOperation for cross-VFS transfers.
    /// </summary>
    internal Task UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                using var fs = File.OpenRead(localPath);
                client.UploadFile(fs, remotePath, uploaded =>
                {
                    progress?.Report((long)(ulong)uploaded);
                    ct.ThrowIfCancellationRequested();
                });
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <summary>
    /// Скачивает удалённый файл с SFTP-сервера локально с отчётом прогресса (байты).
    /// Downloads a remote file from the SFTP server locally with byte-level progress reporting.
    /// Используется <see cref="Operations.CrossVfsCopyOperation"/> для кросс-VFS передачи.
    /// Used by CrossVfsCopyOperation for cross-VFS transfers.
    /// </summary>
    internal Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                var dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using var fs = File.Create(localPath);
                client.DownloadFile(remotePath, fs, downloaded =>
                {
                    progress?.Report((long)(ulong)downloaded);
                    ct.ThrowIfCancellationRequested();
                });
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <summary>
    /// Рекурсивно загружает локальную папку на SFTP-сервер.
    /// Recursively uploads a local directory to the SFTP server.
    /// </summary>
    internal Task UploadDirectoryAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                UploadDirRecursive(client, localPath, remotePath, progress, ct);
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    /// <summary>
    /// Рекурсивно скачивает удалённую папку с SFTP-сервера локально.
    /// Recursively downloads a remote directory from the SFTP server locally.
    /// </summary>
    internal Task DownloadDirectoryAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = CreateClient();
            client.Connect();
            try
            {
                DownloadDirRecursive(client, remotePath, localPath, progress, ct);
            }
            finally
            {
                client.Disconnect();
            }
        }, ct);
    }

    private void UploadDirRecursive(SftpClient client, string localPath, string remotePath, IProgress<long>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!client.Exists(remotePath))
            client.CreateDirectory(remotePath);

        foreach (var file in Directory.EnumerateFiles(localPath))
        {
            ct.ThrowIfCancellationRequested();
            var remoteFile = NormalizePath(remotePath, Path.GetFileName(file));
            try
            {
                using var fs = File.OpenRead(file);
                client.UploadFile(fs, remoteFile, uploaded =>
                {
                    progress?.Report((long)(ulong)uploaded);
                    ct.ThrowIfCancellationRequested();
                });
            }
            catch (Exception ex)
            {
                LogService.Error($"Upload failed: {file} → {remoteFile}: {ex.Message}", nameof(SftpFileSystem), ex);
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(localPath))
        {
            ct.ThrowIfCancellationRequested();
            var remoteDir = NormalizePath(remotePath, Path.GetFileName(dir));
            try
            {
                UploadDirRecursive(client, dir, remoteDir, progress, ct);
            }
            catch (Exception ex)
            {
                LogService.Error($"Upload directory failed: {dir} → {remoteDir}: {ex.Message}", nameof(SftpFileSystem), ex);
            }
        }
    }

    private void DownloadDirRecursive(SftpClient client, string remotePath, string localPath, IProgress<long>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(localPath);

        foreach (var entry in client.ListDirectory(remotePath))
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Name == "." || entry.Name == "..") continue;
            var localChild = Path.Combine(localPath, entry.Name);
            if (entry.IsDirectory)
            {
                try
                {
                    DownloadDirRecursive(client, entry.FullName, localChild, progress, ct);
                }
                catch (Exception ex)
                {
                    LogService.Error($"Download directory failed: {entry.FullName} → {localChild}: {ex.Message}", nameof(SftpFileSystem), ex);
                }
            }
            else
            {
                try
                {
                    using var fs = File.Create(localChild);
                    client.DownloadFile(entry.FullName, fs, downloaded =>
                    {
                        progress?.Report((long)(ulong)downloaded);
                        ct.ThrowIfCancellationRequested();
                    });
                }
                catch (Exception ex)
                {
                    LogService.Error($"Download failed: {entry.FullName} → {localChild}: {ex.Message}", nameof(SftpFileSystem), ex);
                }
            }
        }
    }

    #endregion

    /// <summary>
    /// Создаёт новый экземпляр SftpClient через фабрику.
    /// Creates a new SftpClient instance via the factory.
    /// </summary>
    private SftpClient CreateClient() => _clientFactory();

    /// <summary>
    /// Нормализует удалённый путь: всегда через '/', без дублирующихся разделителей.
    /// Normalizes a remote path: always uses '/', no duplicate separators.
    /// </summary>
    private static string NormalizePath(string dir, string name)
    {
        var baseDir = dir.TrimEnd('/');
        return baseDir.Length == 0 ? "/" + name : baseDir + "/" + name;
    }
}
