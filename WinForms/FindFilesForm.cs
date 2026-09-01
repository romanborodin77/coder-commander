using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Services.Search;
using System.Globalization;

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
public sealed partial class FindFilesForm : ThemedForm
{
    /// <summary>How often accumulated hits are moved into the grid. Fast enough to look live, slow
    /// enough that the UI thread is never the bottleneck.</summary>
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(150);

    private readonly IFileSystem _fs;
    private readonly string _rootPath;

    private readonly List<SearchHit> _pending = [];
    private readonly object _pendingLock = new();

    private CancellationTokenSource? _cancellation;
    private SearchEngine.SearchProgress _progress;
    private readonly object _progressLock = new();
    private bool _running;

    /// <summary>The file the user chose, valid after <see cref="DialogResult.OK"/>.</summary>
    public string? SelectedPath { get; private set; }

    public FindFilesForm(IFileSystem fs, string rootPath)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _fs = fs;
        _rootPath = rootPath;

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colName.Text = L.GetString("Find.Col.Name");
        _colFolder.Text = L.GetString("Find.Col.Folder");
        _colSize.Text = L.GetString("Find.Col.Size");
        _colLine.Text = L.GetString("Find.Col.Line");
        _colText.Text = L.GetString("Find.Col.Text");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _status.Text = WhereLabel();
        _startBtn.Text = L.GetString("Find.Start");

        // Only after ApplyLocalization has put the real captions in place - see SizeToText.
        foreach (var check in new[] { _matchCaseCheck, _wholeWordCheck, _subdirectoriesCheck, _regexCheck })
            SizeToText(check);

        _results.DoubleClick += (_, _) => GoToSelected();
        _results.SelectedIndexChanged += (_, _) => UpdateButtonState();
        _startBtn.Click += (_, _) => ToggleSearch();
        _goToBtn.Click += (_, _) => GoToSelected();
        _closeBtn.Click += (_, _) => Close();

        _flushTimer.Interval = (int)FlushInterval.TotalMilliseconds;
        _flushTimer.Tick += (_, _) => FlushPending();

        UpdateButtonState();
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
            _subdirectoriesCheck.Checked,
            _regexCheck.Checked);

        var engine = new SearchEngine(_fs, query);

        // Checked before starting rather than left to silently find nothing - see
        // FileMask.IsValid's own doc comment for why an invalid regex matches nothing instead of
        // everything, which would otherwise look identical to "search ran, found no matches".
        if (!engine.IsNameMaskValid)
        {
            _status.Text = L.GetString("Find.InvalidMaskRegex");
            return;
        }
        if (engine.ContentRegexInvalid)
        {
            _status.Text = L.GetString("Find.InvalidContentRegex");
            return;
        }

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
                progress => { lock (_progressLock) _progress = progress; },
                _cancellation.Token), _cancellation.Token);

            if (IsDisposed || !IsHandleCreated) return;

            // The engine's own counters, not the last progress report: progress is reported when a
            // directory is opened, before its files are scanned, so the last report is always short
            // by the contents of the last directory.
            _status.Text = engine.WasTruncated
                ? L.GetString("Find.Truncated", SearchEngine.MaxResults)
                : L.GetString("Find.Done", engine.FilesExamined, engine.Hits);
        }
        catch (OperationCanceledException)
        {
            if (IsDisposed || !IsHandleCreated) return;
            _status.Text = L.GetString("Find.Stopped", engine.Hits);
        }
        catch (Exception ex)
        {
            LogService.Error("Search failed", ex);
            if (IsDisposed || !IsHandleCreated) return;
            _status.Text = ex.Message;
        }
        finally
        {
            _running = false;
            if (!IsDisposed && IsHandleCreated)
            {
                _flushTimer.Stop();
                FlushPending();          // whatever arrived since the last tick
                UpdateButtonState();
            }
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>Moves everything the engine has found since the last tick into the grid, in one
    /// batch. <c>BeginUpdate</c> matters here: adding rows one at a time to a visible ListView
    /// repaints per row, which is what makes a results list crawl.</summary>
    private void FlushPending()
    {
        if (IsDisposed || !IsHandleCreated) return;
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
                item.SubItems.Add(hit.LineNumber > 0 ? hit.LineNumber.ToString(CultureInfo.InvariantCulture) : "");
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
        if (!_running || IsDisposed || !IsHandleCreated) return;
        SearchEngine.SearchProgress snapshot;
        lock (_progressLock) snapshot = _progress;
        _status.Text = LocalizationService.Current.GetString(
            "Find.Progress", snapshot.FilesExamined, _results.Items.Count);
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

}
