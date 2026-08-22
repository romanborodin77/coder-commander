using CoderCommander.Operations;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Operation queue manager: lists running/completed operations with cancel/clear.
/// </summary>
public sealed class OperationQueueForm : ThemedForm
{
    private readonly OperationManager _manager;
    private readonly ListView _listView;
    private readonly Button _cancelAllBtn;
    private readonly Button _clearBtn;
    private readonly Button _closeBtn;
    private readonly Label _statusLabel;

    /// <param name="manager">The <see cref="OperationManager"/> whose queue is displayed.</param>
    public OperationQueueForm(OperationManager manager)
    {
        _manager = manager;
        var L = LocalizationService.Current;

        Text = L.GetString("OpQueue.Title");
        ClientSize = new Size(680, 440);
        MaximizeBox = false;
        MinimizeBox = false;

        var p = ThemeService.Current;

        _listView = UiHelpers.CreateListView(
            (L.GetString("OpQueue.Col.Type"), 80),
            (L.GetString("OpQueue.Col.Source"), 350),
            (L.GetString("OpQueue.Col.Status"), 80));
        _listView.Dock = DockStyle.Fill;

        // Button panel
        var btnPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };

        _cancelAllBtn = ThemedForm.CreateThemedButton(L.GetString("OpQueue.CancelAll"));
        _cancelAllBtn.Margin = new Padding(0, 0, 8, 0);
        _cancelAllBtn.Click += (_, _) => _manager.CancelAll();

        _clearBtn = ThemedForm.CreateThemedButton(L.GetString("OpQueue.Clear"));
        _clearBtn.Margin = new Padding(0);
        _clearBtn.Click += (_, _) => { _manager.RemoveCompleted(); RefreshList(); };

        // Same-side Dock stacks from the last-added control outward (outermost = leftmost for
        // Dock.Left), which had rendered these as "Clear CancelAll" instead of "CancelAll
        // Clear" - a FlowLayoutPanel makes the visual order match the add order, and its
        // Margin actually renders (Dock.Left ignores it entirely).
        var leftGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        leftGroup.Controls.Add(_cancelAllBtn);
        leftGroup.Controls.Add(_clearBtn);

        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("OpQueue.Close"), accent: true);
        _closeBtn.Dock = DockStyle.Right;
        _closeBtn.Click += (_, _) => Close();

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = p.DimForeground,
            Font = p.GridFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            Tag = ThemeRole.Muted
        };

        // Fill added first (docks last, gets the remainder) - added last, as it originally was,
        // it would have claimed the whole panel before the Left/Right groups got a chance to
        // carve out their own space.
        btnPanel.Controls.Add(_statusLabel);
        btnPanel.Controls.Add(_closeBtn);
        btnPanel.Controls.Add(leftGroup);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(_listView);
        Controls.Add(btnPanel);

        CancelButton = _closeBtn;

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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _listView?.Dispose();
            _cancelAllBtn?.Dispose();
            _clearBtn?.Dispose();
            _closeBtn?.Dispose();
            _statusLabel?.Dispose();
        }
        base.Dispose(disposing);
    }
}
