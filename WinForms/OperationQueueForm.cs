using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Operation queue manager: lists running/completed operations with cancel/clear.
/// </summary>
public sealed partial class OperationQueueForm : ThemedForm
{
    private readonly OperationManager _manager;

    /// <param name="manager">The <see cref="OperationManager"/> whose queue is displayed.</param>
    public OperationQueueForm(OperationManager manager)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _manager = manager;

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colType.Text = L.GetString("OpQueue.Col.Type");
        _colSource.Text = L.GetString("OpQueue.Col.Source");
        _colStatus.Text = L.GetString("OpQueue.Col.Status");

        _cancelAllBtn.Click += (_, _) => _manager.CancelAll();
        _clearBtn.Click += (_, _) => { _manager.RemoveCompleted(); RefreshList(); };
        _closeBtn.Click += (_, _) => Close();

        // Pause/Resume/Skip per row (audit finding G051/G052) - OpQueue.Pause/OpQueue.Resume
        // existed as localization strings with nothing in the code ever reading them; this form
        // had no per-row interaction at all before this (Cancel All / Clear only acted on the
        // whole queue). Built fresh on every right-click and self-disposes via AutoDisposeOnClose,
        // the same pattern FilePanelUserControl.BuildContextMenu uses - a persistent, rebuilt-in-
        // place menu has no safe way to update while still open.
        _listView.MouseDown += OnListViewMouseDown;

        _manager.OperationChanged += OnOperationChanged;
        FormClosing += (_, _) => _manager.OperationChanged -= OnOperationChanged;

        Load += (_, _) => RefreshList();
    }

    private void OnListViewMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var info = _listView.HitTest(e.Location);
        if (info.Item?.Tag is not QueuedOperation queued) return;
        info.Item.Selected = true;
        info.Item.Focused = true;

        var op = queued.Operation;
        var L = LocalizationService.Current;
#pragma warning disable CA2000 // disposed via AutoDisposeOnClose below, same as FilePanelUserControl's own context menu
        var menu = new ContextMenuStrip
        {
            BackColor = ThemeService.Current.HeaderBackground,
            ForeColor = ThemeService.Current.Foreground,
            Font = ThemeService.Current.GridFont,
            Renderer = new ThemeRenderer()
        };
#pragma warning restore CA2000

        if (queued.RequiresManualStart && op.State == OperationState.NotStarted)
        {
            var startItem = new ToolStripMenuItem(L.GetString("OpQueue.Start"));
            startItem.Click += (_, _) => _ = _manager.StartQueuedAsync(queued.Id);
            menu.Items.Add(startItem);
            menu.Items.Add(new ToolStripSeparator());
        }

        if (op.SupportsPauseAndSkip && op.State is OperationState.Running or OperationState.Paused)
        {
            var pauseItem = new ToolStripMenuItem(op.State == OperationState.Paused
                ? L.GetString("OpQueue.Resume")
                : L.GetString("OpQueue.Pause"));
            pauseItem.Click += (_, _) =>
            {
                if (op.State == OperationState.Paused) op.Resume();
                else op.Pause();
            };
            menu.Items.Add(pauseItem);

            var skipItem = new ToolStripMenuItem(L.GetString("OpDlg.Skip"));
            skipItem.Click += (_, _) => op.RequestSkip();
            menu.Items.Add(skipItem);

            menu.Items.Add(new ToolStripSeparator());
        }

        if (op.State is OperationState.Running or OperationState.Paused or OperationState.NotStarted)
        {
            var cancelItem = new ToolStripMenuItem(L.GetString("OpDlg.Cancel"));
            // Routed through the manager (not a bare op.Cancel()) so a held-but-not-yet-started
            // entry (queued.RequiresManualStart) actually transitions to Canceled and is removed -
            // FileOperation.Cancel() alone only sets a flag ExecuteAsync would have checked, which
            // for an operation that was never started never runs. Also correctly dequeues a plain
            // not-yet-started RunAsync entry, same as before.
            cancelItem.Click += (_, _) => _manager.Cancel(queued.Id);
            menu.Items.Add(cancelItem);
        }

        if (menu.Items.Count == 0)
        {
            menu.Dispose();
            return;
        }

        UiHelpers.AutoDisposeOnClose(menu, this);
        menu.Show(_listView, e.Location);
    }

    private void OnOperationChanged(object? sender, OperationManagerEventArgs e)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(RefreshList);
    }

    private void RefreshList()
    {
        var L = LocalizationService.Current;
        _listView.BeginUpdate();
        _listView.Items.Clear();

        var ops = _manager.Operations;
        if (ops.Count == 0)
        {
            _statusLabel.Text = L.GetString("OpQueue.Empty");
        }
        else
        {
            _statusLabel.Text = L.GetString("OpQueue.Count", ops.Count);
        }

        foreach (var op in ops)
        {
            var stateText = op.Operation.State switch
            {
                OperationState.Running => L.GetString("OpQueue.Status.Running"),
                OperationState.Paused => L.GetString("OpQueue.Status.Paused"),
                OperationState.Completed => L.GetString("OpQueue.Status.Completed"),
                OperationState.Canceled => L.GetString("OpQueue.Status.Canceled"),
                OperationState.Failed => L.GetString("OpQueue.Status.Failed"),
                OperationState.NotStarted when op.RequiresManualStart => L.GetString("OpQueue.Status.Held"),
                _ => L.GetString("OpQueue.Status.Queued")
            };

            var typeText = op.Operation.Type switch
            {
                OperationType.Copy => L.GetString("OpQueue.Type.Copy"),
                OperationType.Move => L.GetString("OpQueue.Type.Move"),
                OperationType.Delete => L.GetString("OpQueue.Type.Delete"),
                OperationType.Pack => L.GetString("OpQueue.Type.Pack"),
                OperationType.Unpack => L.GetString("OpQueue.Type.Unpack"),
                _ => op.Operation.Type.ToString()
            };

            var lvi = new ListViewItem(typeText);
            lvi.SubItems.Add(op.DisplayName);
            lvi.SubItems.Add(stateText);
            lvi.Tag = op;
            _listView.Items.Add(lvi);
        }

        _listView.EndUpdate();
    }

}
