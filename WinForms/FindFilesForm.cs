using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Services.Search;

namespace CoderCommander.WinForms;

/// <summary>
/// Search for files by name and by content, over whatever filesystem the panel is showing.
///
/// <para><b>Results appear while the search runs.</b> A search over a whole drive takes minutes, and
/// a dialog that shows nothing until it finishes is one the user cancels before it ever helps. The
/// engine reports each hit as it finds it.</para>
///
/// <para><b>Hits are batched before they reach the grid.</b> The engine calls back from a background
/// thread, and marshalling ten thousand of those individually would spend the whole search queueing
/// UI work - the grid would lag behind and the window would stop repainting. Hits accumulate and are
/// flushed on a timer, which is the difference between a responsive dialog and one that appears
/// frozen while it is in fact working perfectly.</para>
/// </summary>
public sealed class FindFilesForm : ThemedForm
{
    /// <summary>How often accumulated hits are moved into the grid. Fast enough to look live, slow
    /// enough that the UI thread is never the bottleneck.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(150);

    private readonly IFileSystem _fs;
    private readonly string _rootPath;

    private readonly TextBox _maskBox;
    private readonly TextBox _textBox;
    private readonly ThemedCheckBox _matchCaseCheck;
    private readonly ThemedCheckBox _wholeWordCheck;
    private readonly ThemedCheckBox _subdirectoriesCheck;
    private readonly ListView _results;
    private readonly Label _status;
    private readonly Button _startBtn;
    private readonly Button _goToBtn;

    private readonly List<SearchHit> _pending = [];
    private readonly object _pendingLock = new();
    private readonly System.Windows.Forms.Timer _flushTimer;

    private CancellationTokenSource? _cancellation;
    private SearchEngine.SearchProgress _progress;
    private bool _running;

    /// <summary>The file the user chose, valid after <see cref="DialogResult.OK"/>.</summary>
    public string? SelectedPath { get; private set; }

    public FindFilesForm(IFileSystem fs, string rootPath)
    {
        _fs = fs;
        _rootPath = rootPath;

        var L = LocalizationService.Current;
        Text = L.GetString("Find.Title");
        ClientSize = new Size(820, 520);
        Resizable = true;
        MinimumSize = new Size(620, 400);

        _maskBox = UiHelpers.CreateTextBox();
        _maskBox.Dock = DockStyle.Fill;
        _maskBox.Text = "*.*";

        _textBox = UiHelpers.CreateTextBox();
        _textBox.Dock = DockStyle.Fill;

        _matchCaseCheck = UiHelpers.CreateCheckBox(L.GetString("Find.MatchCase"), false);
        _wholeWordCheck = UiHelpers.CreateCheckBox(L.GetString("Find.WholeWord"), false);
        _subdirectoriesCheck = UiHelpers.CreateCheckBox(L.GetString("Find.Subdirectories"), true);

        _results = UiHelpers.CreateListView(
            (L.GetString("Find.Col.Name"), 200),
            (L.GetString("Find.Col.Folder"), 260),
            (L.GetString("Find.Col.Size"), 90),
            (L.GetString("Find.Col.Line"), 60),
            (L.GetString("Find.Col.Text"), 320));
        _results.Dock = DockStyle.Fill;
        _results.DoubleClick += (_, _) => GoToSelected();
        _results.SelectedIndexChanged += (_, _) => UpdateButtonState();

        _status = UiHelpers.CreateLabel(WhereLabel());
        _status.Dock = DockStyle.Fill;
        _status.SetRole(ThemeRole.Hint);

        _startBtn = CreateThemedButton(L.GetString("Find.Start"), accent: true);
        _startBtn.Margin = new Padding(0, 0, 8, 0);
        _startBtn.Click += (_, _) => ToggleSearch();

        _goToBtn = CreateThemedButton(L.GetString("Find.GoTo"));
        _goToBtn.Margin = new Padding(0, 0, 8, 0);
        _goToBtn.Click += (_, _) => GoToSelected();

        var closeBtn = CreateThemedButton(L.GetString("Common.Close"));
        closeBtn.Click += (_, _) => Close();

        // Dock=Fill sibling first, then every docked sibling (docking order pitfall).
        Controls.Add(BuildResultsArea());
        Controls.Add(BuildQueryArea(L));
        Controls.Add(BuildButtonBar(_startBtn, _goToBtn, closeBtn));

        AcceptButton = _startBtn;
        // Escape closes, per the convention every dialog here follows.
        CancelButton = closeBtn;

        _flushTimer = new System.Windows.Forms.Timer { Interval = (int)FlushInterval.TotalMilliseconds };
        _flushTimer.Tick += (_, _) => FlushPending();

        UpdateButtonState();
    }

    // ── Layout ──────────────────────────────────────────────────────────────────────────────

    private Control BuildQueryArea(LocalizationService L)
    {
        // A fixed height, not AutoSize. An auto-sizing Dock=Top panel settles its height after the
        // form's first layout pass, and the Dock=Fill sibling below it is measured before that
        // happens - which pushes the bottom button bar past the client area and clips the buttons.
        // Four rows of known height plus the padding is a number this dialog can simply state.
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 4,
            Height = 12 + 32 + 32 + 34 + 24 + 8,
            Padding = new Padding(16, 12, 16, 8),
        };
        layout.SetRole(ThemeRole.Background);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, 0, L.GetString("Find.Field.Mask"), _maskBox);
        AddRow(layout, 1, L.GetString("Find.Field.Text"), _textBox);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        foreach (var check in new[] { _matchCaseCheck, _wholeWordCheck, _subdirectoriesCheck })
        {
            SizeToText(check);
            check.Margin = new Padding(0, 0, 16, 0);
            options.Controls.Add(check);
        }

        layout.Controls.Add(options, 1, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.Controls.Add(_status, 1, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        return layout;
    }

    /// <summary>
    /// Widens a checkbox to fit its own caption.
    ///
    /// <para><c>ThemedCheckBox</c> is owner-drawn, so <c>AutoSize</c> has nothing to measure and the
    /// control keeps WinForms' default width - which silently truncates every caption longer than a
    /// word or two, and differs per language, so the English build looks fine while the Russian one
    /// is cut off. Measuring the text is the only thing that holds for both.</para>
    /// </summary>
    private static void SizeToText(ThemedCheckBox check)
    {
        var width = TextRenderer.MeasureText(check.Text, check.Font).Width;
        // Room for the box itself and the gap between it and the caption.
        check.Width = width + 34;
        check.Height = 28;
    }

    private void AddRow(TableLayoutPanel layout, int row, string caption, Control control)
    {
        var label = UiHelpers.CreateLabel(caption);
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
    }

    private Control BuildResultsArea()
    {
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 0) };
        host.SetRole(ThemeRole.Background);
        host.Controls.Add(_results);
        return host;
    }

    private Control BuildButtonBar(params Button[] buttons)
    {
        var group = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
        };
        foreach (var button in buttons) group.Controls.Add(button);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 56, Padding = new Padding(16, 10, 16, 10) };
        bar.SetRole(ThemeRole.HeaderBackground);
        bar.Controls.Add(group);
        return bar;
    }

    // ── Search ──────────────────────────────────────────────────────────────────────────────

    private void ToggleSearch()
    {
        if (_running)
        {
            _cancellation?.Cancel();
            return;
        }
        _ = StartSearchAsync();
    }

    private async Task StartSearchAsync()
    {
        var L = LocalizationService.Current;

        _results.Items.Clear();
        lock (_pendingLock) _pending.Clear();
        _progress = default;

        var query = new SearchQuery(
            _maskBox.Text,
            _textBox.Text,
            _matchCaseCheck.Checked,
            _wholeWordCheck.Checked,
            _subdirectoriesCheck.Checked);

        var engine = new SearchEngine(_fs, query);
        _cancellation = new CancellationTokenSource();
        _running = true;
        _flushTimer.Start();
        UpdateButtonState();

        try
        {
            // Task.Run because the walk starts with a synchronous stretch before its first await
            // (building the mask, the first enumeration on a local disk) and that would run on the
            // UI thread, freezing the dialog at the moment it is supposed to come alive.
            await Task.Run(() => engine.RunAsync(
                _rootPath,
                hit => { lock (_pendingLock) _pending.Add(hit); },
                progress => _progress = progress,
                _cancellation.Token), _cancellation.Token);

            _status.Text = engine.WasTruncated
                ? L.GetString("Find.Truncated", SearchEngine.MaxResults)
                : L.GetString("Find.Done", _progress.FilesExamined, _results.Items.Count + PendingCount());
        }
        catch (OperationCanceledException)
        {
            _status.Text = L.GetString("Find.Stopped", _results.Items.Count + PendingCount());
        }
        catch (Exception ex)
        {
            LogService.Error("Search failed", ex);
            _status.Text = ex.Message;
        }
        finally
        {
            _running = false;
            _flushTimer.Stop();
            FlushPending();          // whatever arrived since the last tick
            _cancellation?.Dispose();
            _cancellation = null;
            UpdateButtonState();
        }
    }

    private int PendingCount()
    {
        lock (_pendingLock) return _pending.Count;
    }

    /// <summary>Moves everything the engine has found since the last tick into the grid, in one
    /// batch. <c>BeginUpdate</c> matters here: adding rows one at a time to a visible ListView
    /// repaints per row, which is what makes a results list crawl.</summary>
    private void FlushPending()
    {
        List<SearchHit> batch;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                UpdateRunningStatus();
                return;
            }
            batch = [.. _pending];
            _pending.Clear();
        }

        _results.BeginUpdate();
        try
        {
            foreach (var hit in batch)
            {
                var item = new ListViewItem(hit.Entry.Name) { Tag = hit.Entry.FullPath };
                item.SubItems.Add(VfsPath.GetParent(hit.Entry.FullPath));
                item.SubItems.Add(Utils.FormatUtils.FormatSize(hit.Entry.Size));
                item.SubItems.Add(hit.LineNumber > 0 ? hit.LineNumber.ToString() : "");
                item.SubItems.Add(hit.Line);
                _results.Items.Add(item);
            }
        }
        finally
        {
            _results.EndUpdate();
        }

        UpdateRunningStatus();
        UpdateButtonState();
    }

    private void UpdateRunningStatus()
    {
        if (!_running) return;
        _status.Text = LocalizationService.Current.GetString(
            "Find.Progress", _progress.FilesExamined, _results.Items.Count);
    }

    private void GoToSelected()
    {
        if (_results.SelectedItems.Count == 0) return;

        SelectedPath = _results.SelectedItems[0].Tag as string;
        if (string.IsNullOrEmpty(SelectedPath)) return;

        _cancellation?.Cancel();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void UpdateButtonState()
    {
        var L = LocalizationService.Current;
        _startBtn.Text = _running ? L.GetString("Find.Stop") : L.GetString("Find.Start");
        _goToBtn.Enabled = _results.SelectedItems.Count > 0;
    }

    private string WhereLabel() =>
        LocalizationService.Current.GetString("Find.Where", _rootPath);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // A search still walking a network share must not outlive the window that owns it - its
        // callbacks would keep touching a disposed grid.
        _cancellation?.Cancel();
        _flushTimer.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _flushTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
