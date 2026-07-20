using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace CoderCommander.Services;

/// <summary>
/// Модель одной записи удалённой файловой системы для отображения в списке.
/// Represents a single remote file system entry for display in a list.
/// </summary>
/// <param name="Name">Имя файла или папки. File or directory name.</param>
/// <param name="FullPath">Полный удалённый путь. Full remote path.</param>
/// <param name="IsDirectory">True, если это директория. True if this is a directory.</param>
/// <param name="Size">Размер в байтах (0 для директорий). Size in bytes (0 for directories).</param>
/// <param name="LastWriteTime">Дата и время последней модификации. Last write date and time.</param>
/// <param name="IsParent">True, если это запись ".." для перехода на уровень выше. True if this is the ".." parent entry.</param>
public sealed record SftpEntryModel(
    string Name,
    string FullPath,
    bool IsDirectory,
    long Size,
    DateTime LastWriteTime,
    bool IsParent = false);

/// <summary>
/// Сервис для работы с удалённой файловой системой через SFTP поверх SSH.
/// Service for working with a remote file system via SFTP over SSH.
/// </summary>
public sealed class SftpService
{
    /// <summary>
    /// Получает список элементов удалённой директории. В начало списка добавляет ".." если это не корень.
    /// Lists entries in a remote directory; prepends ".." if not at the root.
    /// </summary>
    /// <param name="p">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="remotePath">Удалённый путь к директории. Remote directory path.</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>Список записей удалённой файловой системы. List of remote file system entries.</returns>
    public Task<List<SftpEntryModel>> ListDirectoryAsync(SshProfile p, string remotePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var result = new List<SftpEntryModel>();
            using var client = new SftpClient(SshService.BuildConnectionInfo(p));
            client.Connect();
            // Для корня не добавляем переход наверх.
            if (remotePath != "/")
            {
                var parent = remotePath.TrimEnd('/');
                var idx = parent.LastIndexOf('/');
                var parentPath = idx <= 0 ? "/" : parent[..idx];
                result.Add(new SftpEntryModel("..", parentPath, true, 0, DateTime.MinValue, true));
            }
            foreach (var e in client.ListDirectory(remotePath))
            {
                // Пропускаем служебные записи "." и "..".
                if (e.Name == "." || e.Name == "..") continue;
                result.Add(new SftpEntryModel(
                    e.Name,
                    NormalizePath(remotePath, e.Name),
                    e.IsDirectory,
                    e.IsDirectory ? 0 : e.Length,
                    e.LastWriteTime));
            }
            client.Disconnect();
            // Сначала папки, затем файлы, внутри — по имени.
            result.Sort((a, b) =>
            {
                if (a.IsParent) return -1;
                if (b.IsParent) return 1;
                if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
                return string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }, ct);
    }

    /// <summary>
    /// Скачивает удалённый файл в локальный путь с отчётом о прогрессе.
    /// Downloads a remote file to a local path with progress reporting.
    /// </summary>
    /// <param name="p">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="remotePath">Удалённый путь к файлу. Remote file path.</param>
    /// <param name="localPath">Локальный путь для сохранения. Local destination path.</param>
    /// <param name="progress">Провайдер прогресса (байты). Progress provider (bytes).</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>Task, завершающийся после завершения скачивания. Task that completes when the download finishes.</returns>
    public Task DownloadFileAsync(SshProfile p, string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var client = new SftpClient(SshService.BuildConnectionInfo(p));
            client.Connect();
            using var fs = File.Create(localPath);
            client.DownloadFile(remotePath, fs, uploaded =>
            {
                progress?.Report((long)(ulong)uploaded);
                ct.ThrowIfCancellationRequested();
            });
            client.Disconnect();
        }, ct);
    }

    /// <summary>
    /// Загружает локальный файл на удалённый сервер с отчётом о прогрессе.
    /// Uploads a local file to a remote server with progress reporting.
    /// </summary>
    /// <param name="p">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="localPath">Локальный путь к файлу. Local file path.</param>
    /// <param name="remotePath">Удалённый путь назначения. Remote destination path.</param>
    /// <param name="progress">Провайдер прогресса (байты). Progress provider (bytes).</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>Task, завершающийся после завершения загрузки. Task that completes when the upload finishes.</returns>
    public Task UploadFileAsync(SshProfile p, string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var client = new SftpClient(SshService.BuildConnectionInfo(p));
            client.Connect();
            using var fs = File.OpenRead(localPath);
            try
            {
                client.UploadFile(fs, remotePath, uploaded =>
                {
                    progress?.Report((long)(ulong)uploaded);
                    ct.ThrowIfCancellationRequested();
                });
            }
            catch (OperationCanceledException)
            {
                // Удаляем частично загруженный файл с сервера при отмене.
                // Delete the partially uploaded file from the server on cancellation.
                try { if (client.Exists(remotePath)) client.DeleteFile(remotePath); }
                catch { /* best-effort cleanup */ }
                throw;
            }
            client.Disconnect();
        }, ct);
    }

    /// <summary>
    /// Создаёт удалённую директорию.
    /// Creates a remote directory.
    /// </summary>
    /// <param name="p">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="remotePath">Путь к создаваемой директории. Path of the directory to create.</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>Task, завершающийся после создания директории. Task that completes after the directory is created.</returns>
    public Task MakeDirectoryAsync(SshProfile p, string remotePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var client = new SftpClient(SshService.BuildConnectionInfo(p));
            client.Connect();
            client.CreateDirectory(remotePath);
            client.Disconnect();
        }, ct);
    }

    /// <summary>
    /// Удаляет удалённый элемент рекурсивно (папки удаляются вместе с содержимым).
    /// Deletes a remote item recursively (directories are deleted with all contents).
    /// </summary>
    /// <param name="p">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="remotePath">Путь к удаляемому элементу. Path of the item to delete.</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>Task, завершающийся после удаления. Task that completes after deletion.</returns>
    public Task DeleteRemoteAsync(SshProfile p, string remotePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var client = new SftpClient(SshService.BuildConnectionInfo(p));
            client.Connect();
            DeleteRecursive(client, remotePath, ct);
            client.Disconnect();
        }, ct);
    }

    private static void DeleteRecursive(SftpClient client, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (client.Exists(path) && client.GetAttributes(path).IsDirectory)
        {
            foreach (var e in client.ListDirectory(path))
            {
                if (e.Name == "." || e.Name == "..") continue;
                DeleteRecursive(client, e.FullName, ct);
            }
            client.DeleteDirectory(path);
        }
        else
        {
            client.DeleteFile(path);
        }
    }

    /// <summary>
    /// Переименовывает/перемещает удалённый элемент.
    /// Renames or moves a remote item.
    /// </summary>
    /// <param name="p">Профиль SSH-подключения. SSH connection profile.</param>
    /// <param name="oldPath">Текущий удалённый путь. Current remote path.</param>
    /// <param name="newPath">Новый удалённый путь. New remote path.</param>
    /// <param name="ct">Токен отмены. Cancellation token.</param>
    /// <returns>Task, завершающийся после переименования. Task that completes after the rename.</returns>
    public Task RenameRemoteAsync(SshProfile p, string oldPath, string newPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var client = new SftpClient(SshService.BuildConnectionInfo(p));
            client.Connect();
            client.RenameFile(oldPath, newPath);
            client.Disconnect();
        }, ct);
    }

    // Нормализует удалённый путь: всегда через '/', без дублирующихся разделителей.
    private static string NormalizePath(string dir, string name)
    {
        var baseDir = dir.TrimEnd('/');
        return baseDir.Length == 0 ? "/" + name : baseDir + "/" + name;
    }
}
