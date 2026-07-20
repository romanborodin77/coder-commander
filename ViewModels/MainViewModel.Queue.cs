using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Operations;
using CoderCommander.Services;
using OverwritePolicy = CoderCommander.Operations.OverwritePolicy;

namespace CoderCommander.ViewModels;

/// <summary>
/// Partial MainViewModel: enqueue copy/move operations into OperationQueue.
/// Частичный MainViewModel: добавление операций копирования/перемещения в очередь.
/// </summary>
public partial class MainViewModel
{
    private Views.OperationQueueWindow? _queueWindow;

    /// <summary>
    /// Показывает или активирует окно очереди операций.
    /// Shows or activates the operation queue window.
    /// </summary>
    public void ShowQueueWindow()
    {
        if (_queueWindow == null)
        {
            _queueWindow = new Views.OperationQueueWindow(new OperationQueueViewModel())
            {
                Owner = Application.Current.MainWindow
            };
            _queueWindow.Closed += (_, _) => _queueWindow = null;
            _queueWindow.Show();
        }
        else
        {
            _queueWindow.ShowFromTray();
        }
    }

    /// <summary>
    /// Добавляет операцию копирования/перемещения в очередь.
    /// Enqueues a copy/move operation into the operation queue.
    /// </summary>
    public void EnqueueCopyMove(List<FileSystemItem> items, string targetDir, bool isMove,
        OverwritePolicy policy, bool copyAttributes, bool copyTimestamps, bool copyNtfsPermissions)
    {
        if (items.Count == 0) return;

        var settings = SettingsService.Load();
        var lfs = new LocalFileSystem();
        var sources = items.Select(i => i.FullPath).ToList();
        var options = new TransferOptions
        {
            BufferSize = settings.CopyBufferSizeKB * 1024,
            CopyAttributes = copyAttributes,
            CopyTimestamps = copyTimestamps,
            ReserveDiskSpace = settings.ReserveDiskSpace,
            CopyNtfsPermissions = copyNtfsPermissions
        };

        TransferOperation transferOp = null!;
        Func<string, string, OverwritePolicy>? conflictCb = null;
        if (policy == OverwritePolicy.Ask)
        {
            conflictCb = (src, dst) =>
            {
                try
                {
                    OverwritePolicy result = OverwritePolicy.Skip;
                    bool applyToAll = false;
                    var ev = new System.Threading.ManualResetEventSlim(false);
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var dlg = new Views.OverwriteDialog(Path.GetFileName(src), src, dst)
                            { Owner = Application.Current.MainWindow };
                            if (dlg.ShowDialog() == true)
                            {
                                result = dlg.Result;
                                applyToAll = dlg.ApplyToAll;
                            }
                        }
                        finally { ev.Set(); }
                    }));
                    ev.Wait();
                    if (applyToAll) transferOp.SetCachedAskPolicy(result);
                    return result;
                }
                catch { return OverwritePolicy.Skip; }
            };
        }

        if (isMove)
            transferOp = new MoveOperation(lfs, sources, targetDir, policy, conflictCb, null, options);
        else
            transferOp = new CopyOperation(lfs, sources, targetDir, policy, conflictCb, null, options);

        var srcDir = items.Count > 0 ? Path.GetDirectoryName(items[0].FullPath) ?? "" : "";
        var count = items.Count;
        var opTitle = isMove ? L10n("OpDlg.Title.Move") : L10n("OpDlg.Title.Copy");
        var opType = isMove ? "Move" : "Copy";
        var desc = count == 1
            ? $"{opTitle}: {Path.GetFileName(items[0].FullPath)}"
            : $"{opTitle}: {count} items";

        var wrappedOp = new DelegateOperation(transferOp, async ct =>
        {
            try
            {
                await transferOp.ExecuteAsync(ct);
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await ActivePanel.RefreshAsync();
                    await (ActivePanel == LeftPanel ? RightPanel : LeftPanel).RefreshAsync();
                    await SyncActiveVirtualPanelAsync();
                });
            }
        });

        _queue.Enqueue(wrappedOp, desc, srcDir, targetDir, opType);
        ShowQueueWindow();
    }
}