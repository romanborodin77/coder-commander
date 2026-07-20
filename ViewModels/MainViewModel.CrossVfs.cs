using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Operations;
using CoderCommander.Services;
using CoderCommander.Views;

namespace CoderCommander.ViewModels;

/// <summary>
/// Partial MainViewModel: кросс-VFS операции (Local ↔ SFTP).
/// Partial MainViewModel: cross-VFS operations (Local ↔ SFTP).
/// Обеспечивает копирование/перенос файлов между локальной панелью и подключённым SFTP-сервером.
/// Provides copy/move between the local panel and a connected SFTP server.
/// </summary>
public partial class MainViewModel
{
    /// <summary>
    /// Подключённый SFTP-filesystem для кросс-VFS операций (null если не подключено).
    /// Connected SFTP filesystem for cross-VFS operations (null if not connected).
    /// </summary>
    private SftpFileSystem? _activeSftpFs;

    /// <summary>
    /// Текущий SSH-профиль SFTP-подключения (для UI-отображения).
    /// Current SSH profile of the SFTP connection (for UI display).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    private Services.SshProfile? _activeSftpProfile;

    /// <summary>Флаг: активно ли SFTP-подключение для кросс-VFS. / Whether SFTP connection for cross-VFS is active.</summary>
    public bool IsSftpConnected => _activeSftpFs is not null;

    /// <summary>Отображаемое имя подключённого SFTP-сервера. / Display name of the connected SFTP server.</summary>
    [ObservableProperty] private string _sftpConnectionInfo = "";

    /// <summary>
    /// Подключиться к SFTP-серверу через профиль для кросс-VFS операций.
    /// Connect to an SFTP server via profile for cross-VFS operations.
    /// </summary>
    /// <param name="profile">SSH-профиль для подключения. / SSH profile to connect with.</param>
    /// <returns>True если подключение успешно. / True if connection succeeded.</returns>
    public async Task<bool> ConnectSftpAsync(SshProfile profile)
    {
        try
        {
            StatusText = string.Format(L10n("CrossVfs.Connecting"), profile.Host);

            // Создаём SftpFileSystem из профиля / Create SftpFileSystem from profile.
            var fs = new SftpFileSystem(profile);

            // Пробное подключение / Test connection.
            var reachable = await _ssh.IsReachableAsync(profile);
            if (!reachable)
            {
                StatusText = string.Format(L10n("CrossVfs.ConnectFailed"), profile.Host);
                return false;
            }

            _activeSftpFs = fs;
            _activeSftpProfile = profile;
            SftpConnectionInfo = $"{profile.User}@{profile.Host}:{profile.RemotePath}";
            OnPropertyChanged(nameof(IsSftpConnected));
            StatusText = string.Format(L10n("CrossVfs.Connected"), profile.Host);
            return true;
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n("CrossVfs.Error"), ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Отключить SFTP-подключение для кросс-VFS операций.
    /// Disconnect the SFTP connection for cross-VFS operations.
    /// </summary>
    [RelayCommand]
    public void DisconnectSftp()
    {
        _activeSftpFs = null;
        _activeSftpProfile = null;
        SftpConnectionInfo = "";
        OnPropertyChanged(nameof(IsSftpConnected));
        StatusText = L10n("CrossVfs.Disconnected");
    }

    /// <summary>
    /// Копировать выделенные элементы из активной (локальной) панели на подключённый SFTP-сервер.
    /// Copy selected items from the active (local) panel to the connected SFTP server.
    /// Аналог F5, но для кросс-VFS: Local → SFTP.
    /// </summary>
    [RelayCommand]
    public async Task CrossVfsCopyToSftpAsync()
    {
        if (IsBusy || _activeSftpFs is null) return;
        var items = ActivePanel.GetSelectionOrCurrent().ToList();
        if (items.Count == 0) return;

        var remoteDir = _activeSftpFs.RootPath;
        var confirmOverwrite = SettingsService.Load().ConfirmOverwrite;

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "";

        try
        {
            var sources = items.Where(i => !i.IsParent).Select(i => i.FullPath).ToList();
            var op = new CrossVfsCopyOperation(
                sourceFs: LocalFileSystem.Instance,
                destFs: _activeSftpFs,
                sources: sources,
                destDir: remoteDir,
                isMove: false,
                policy: Operations.OverwritePolicy.Overwrite,
                progress: new Progress<OperationProgress>(p =>
                {
                    ProgressValue = p.Percent;
                    ProgressText = string.Format(L10n("CrossVfs.Uploading"), p.CurrentFile ?? "");
                }));

            using var cts = new CancellationTokenSource();
            await op.ExecuteAsync(cts.Token);

            if (op.Failed > 0)
                StatusText = string.Format(L10n("CrossVfs.CopyDoneErrors"), op.Copied, op.Failed);
            else
                StatusText = string.Format(L10n("CrossVfs.CopyDone"), op.Copied);
        }
        catch (OperationCanceledException)
        {
            StatusText = L10n("CrossVfs.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n("CrossVfs.Error"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = "";
        }
    }

    /// <summary>
    /// Скачать файлы из текущей SFTP-директории в активную локальную панель.
    /// Download files from the current SFTP directory into the active local panel.
    /// </summary>
    [RelayCommand]
    public async Task CrossVfsDownloadFromSftpAsync()
    {
        if (IsBusy || _activeSftpFs is null) return;

        // Получаем текущий удалённый путь из SftpViewModel / Get current remote path from SftpViewModel.
        var remotePath = Sftp.CurrentRemotePath;
        if (string.IsNullOrEmpty(remotePath)) return;

        // Выбираем файлы из SFTP-браузера для скачивания / Select files from SFTP browser for download.
        var sftpItems = Sftp.Items
            .Where(i => !i.IsParent)
            .ToList();
        if (sftpItems.Count == 0) return;

        var localDir = ActivePanel.CurrentPath;
        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "";

        try
        {
            var sources = sftpItems.Select(i => i.FullPath).ToList();
            var op = new CrossVfsCopyOperation(
                sourceFs: _activeSftpFs,
                destFs: LocalFileSystem.Instance,
                sources: sources,
                destDir: localDir,
                isMove: false,
                policy: Operations.OverwritePolicy.Overwrite,
                progress: new Progress<OperationProgress>(p =>
                {
                    ProgressValue = p.Percent;
                    ProgressText = string.Format(L10n("CrossVfs.Downloading"), p.CurrentFile ?? "");
                }));

            using var cts = new CancellationTokenSource();
            await op.ExecuteAsync(cts.Token);

            // Обновляем локальную панель / Refresh local panel.
            await ActivePanel.RefreshAsync();

            if (op.Failed > 0)
                StatusText = string.Format(L10n("CrossVfs.CopyDoneErrors"), op.Copied, op.Failed);
            else
                StatusText = string.Format(L10n("CrossVfs.CopyDone"), op.Copied);
        }
        catch (OperationCanceledException)
        {
            StatusText = L10n("CrossVfs.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n("CrossVfs.Error"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = "";
        }
    }

    /// <summary>
    /// Переместить выделенные элементы из активной (локальной) панели на подключённый SFTP-сервер (с удалением источника).
    /// Move selected items from the active (local) panel to the connected SFTP server (delete source).
    /// Аналог F6, но для кросс-VFS: Local → SFTP + delete.
    /// </summary>
    [RelayCommand]
    public async Task CrossVfsMoveToSftpAsync()
    {
        if (IsBusy || _activeSftpFs is null) return;
        var items = ActivePanel.GetSelectionOrCurrent().ToList();
        if (items.Count == 0) return;

        // Подтверждение переноса / Confirm move.
        if (SettingsService.Load().ConfirmDelete &&
            StyledMessageBoxWindow.Show(
                string.Format(L10n("CrossVfs.ConfirmMove"), items.Count),
                L10n("CrossVfs.MoveTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var remoteDir = _activeSftpFs.RootPath;

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "";

        try
        {
            var sources = items.Where(i => !i.IsParent).Select(i => i.FullPath).ToList();
            var op = new CrossVfsCopyOperation(
                sourceFs: LocalFileSystem.Instance,
                destFs: _activeSftpFs,
                sources: sources,
                destDir: remoteDir,
                isMove: true,
                policy: Operations.OverwritePolicy.Overwrite,
                progress: new Progress<OperationProgress>(p =>
                {
                    ProgressValue = p.Percent;
                    ProgressText = string.Format(L10n("CrossVfs.Moving"), p.CurrentFile ?? "");
                }));

            using var cts = new CancellationTokenSource();
            await op.ExecuteAsync(cts.Token);

            await ActivePanel.RefreshAsync();

            if (op.Failed > 0)
                StatusText = string.Format(L10n("CrossVfs.MoveDoneErrors"), op.Copied, op.Failed);
            else
                StatusText = string.Format(L10n("CrossVfs.MoveDone"), op.Copied);
        }
        catch (OperationCanceledException)
        {
            StatusText = L10n("CrossVfs.Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = string.Format(L10n("CrossVfs.Error"), ex.Message);
        }
        finally
        {
            IsBusy = false;
            ProgressValue = 0;
            ProgressText = "";
        }
    }
}
