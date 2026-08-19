using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Compares two directories by relative path + size + timestamp, lets the user
/// queue copy operations to bring them in line.
/// </summary>
public sealed class SyncDirsForm : ThemedForm
{
    private readonly TextBox _leftBox;
    private readonly TextBox _rightBox;
    private readonly ThemedCheckBox _subdirsCheck;
    private readonly ThemedCheckBox _ignoreTimeCheck;
    private readonly ListView _diffList;
    private readonly Label _statusLabel;
    private readonly Button _compareBtn;
    private readonly Button _copyLeftBtn;
    private readonly Button _copyRightBtn;
    private readonly Button _closeBtn;
    private readonly List<SyncEntry> _entries = new();

    // Each side starts on the file system the panel it came from is browsing (local disk,
    // archive, or SFTP/FTP/WebDAV connection), but Browse... only knows how to pick a real local
    // folder - picking one switches that side to LocalFileSystem independently of the other side,
    // which is why these are two separate mutable fields rather than one shared file system.
    private IFileSystem _leftFs;
    private IFileSystem _rightFs;

    /// <summary>Raised when the user initiates a copy operation. The event data contains the direction and file queue.</summary>
    public event EventHandler<SyncCopyRequest>? CopyRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncDirsForm"/> class with the two directories to compare.
    /// </summary>
    /// <param name="leftPath">Path to the left (source) directory.</param>
    /// <param name="rightPath">Path to the right (destination) directory.</param>
    /// <param name="leftFs">File system the left path lives on (normally the left panel's <c>CurrentFileSystem</c>).</param>
    /// <param name="rightFs">File system the right path lives on (normally the right panel's <c>CurrentFileSystem</c>).</param>
    public SyncDirsForm(string leftPath, string rightPath, IFileSystem leftFs, IFileSystem rightFs)
    {
        _leftFs = leftFs;
        _rightFs = rightFs;
        var L = LocalizationService.Current;
        Text = L.GetString("SyncDirs.Title");
        ClientSize = new Size(880, 620); // +20 to match the top panel's 132→152 growth below
        Resizable = true;
        MinimumSize = new Size(600, 420);

        var p = ThemeService.Current;

        // Path panel
        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 4,
            // 4 rows * 32 + Padding(12+12) = 152 - the previous 132 left the last row (the
            // Compare button) squeezed to ~12px tall.
            Height = 152,
            BackColor = p.Background,
            Padding = new Padding(16, 12, 16, 12)
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        // 80 wasn't enough once this column started also holding _ignoreTimeCheck's column-span
        // (with column 3 below) on row 2 - the Russian "Игнорировать время (только размер)" is
        // wider than the English "Ignore time (size only)" and the checkbox was hard-clipping it
        // (LayoutAuditTests' TextOverflow detector, F122). 130 gives the combined span (this
        // column + the 120px one below) enough room for the longer translation.
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        // Wide enough for the localized "Browse…" text - CreateThemedButton's own auto-sizing
        // would give it ~100px, and this used to be 80 (RoundedButton's EndEllipsis then silently
        // truncated it to "Brows..."). 100 wasn't enough once this same column started also
        // holding _compareBtn ("Compare" - longer than "Browse…"), which silently truncated to
        // "Comp..." the same way - text truncation isn't something a Bounds-based check catches
        // on its own, only an actual rendered screenshot shows it.
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        for (int i = 0; i < 4; i++) top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        top.Controls.Add(UiHelpers.CreateLabel(L.GetString("SyncDirs.Left")), 0, 0);
        _leftBox = UiHelpers.CreateTextBox(leftPath);
        _leftBox.Dock = DockStyle.Fill;
        top.Controls.Add(_leftBox, 1, 0);

        var leftBrowse = ThemedForm.CreateThemedButton(L.GetString("Common.Browse"));
        leftBrowse.Dock = DockStyle.Fill;
        leftBrowse.Margin = new Padding(8, 0, 0, 0);
        leftBrowse.Click += (_, _) => Browse(_leftBox, fs => _leftFs = fs);
        top.Controls.Add(UiHelpers.CreateLabel(""), 2, 0);
        top.Controls.Add(leftBrowse, 3, 0);

        top.Controls.Add(UiHelpers.CreateLabel(L.GetString("SyncDirs.Right")), 0, 1);
        _rightBox = UiHelpers.CreateTextBox(rightPath);
        _rightBox.Dock = DockStyle.Fill;
        top.Controls.Add(_rightBox, 1, 1);

        var rightBrowse = ThemedForm.CreateThemedButton(L.GetString("Common.Browse"));
        rightBrowse.Dock = DockStyle.Fill;
        rightBrowse.Margin = new Padding(8, 0, 0, 0);
        rightBrowse.Click += (_, _) => Browse(_rightBox, fs => _rightFs = fs);
        top.Controls.Add(UiHelpers.CreateLabel(""), 2, 1);
        top.Controls.Add(rightBrowse, 3, 1);

        _subdirsCheck = UiHelpers.CreateCheckBox(L.GetString("SyncDirs.Subdirs"), true);
        _subdirsCheck.Dock = DockStyle.Fill;
        top.Controls.Add(_subdirsCheck, 1, 2);

        _ignoreTimeCheck = UiHelpers.CreateCheckBox(L.GetString("SyncDirs.IgnoreTime"), false);
        _ignoreTimeCheck.Dock = DockStyle.Fill;
        top.Controls.Add(_ignoreTimeCheck, 2, 2);
        top.SetColumnSpan(_ignoreTimeCheck, 2);

        _compareBtn = ThemedForm.CreateThemedButton(L.GetString("SyncDirs.Compare"), accent: true);
        _compareBtn.Dock = DockStyle.Fill;
        // Margin = 0: this cell's RowStyle is Absolute 32 - the default 3px-per-side
        // Control.Margin would shrink the button's rendered height to 26px (same trap as every
        // other Dock=Fill-in-a-TableLayoutPanel-cell control fixed elsewhere in this pass).
        _compareBtn.Margin = new Padding(0);
        // Row 3, not row 2 - _ignoreTimeCheck above already spans columns 2-3 on row 2, so placing
        // this in the same cell fought it for space and squeezed the button down to "Com...".
        top.Controls.Add(_compareBtn, 3, 3);
        _compareBtn.Click += (_, _) => _ = CompareAsync();

        // Diff list
        _diffList = UiHelpers.CreateListView(
            (L.GetString("SyncDirs.Status"), 60),
            (L.GetString("SyncDirs.Path"), 380),
            (L.GetString("SyncDirs.LeftSize"), 100),
            (L.GetString("SyncDirs.RightSize"), 100),
            (L.GetString("SyncDirs.Action"), 200));
        _diffList.Dock = DockStyle.Fill;
        _diffList.CheckBoxes = true;
        _diffList.FullRowSelect = true;

        // Bottom panel
        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = p.DimForeground,
            Font = p.GridFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = ThemeRole.Muted
        };

        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        _closeBtn.Margin = new Padding(0, 0, 8, 0);
        _closeBtn.Click += (_, _) => Close();

        _copyRightBtn = ThemedForm.CreateThemedButton(L.GetString("SyncDirs.CopyToRight"));
        _copyRightBtn.Margin = new Padding(0, 0, 8, 0);
        _copyRightBtn.Click += (_, _) => IssueCopy(SyncDirection.LeftToRight);

        _copyLeftBtn = ThemedForm.CreateThemedButton(L.GetString("SyncDirs.CopyToLeft"));
        _copyLeftBtn.Margin = new Padding(0);
        _copyLeftBtn.Click += (_, _) => IssueCopy(SyncDirection.RightToLeft);

        // Three Dock.Right buttons ignored Margin entirely, collapsing all the gaps between
        // them - a right-aligned FlowLayoutPanel (add order = visual left-to-right order)
        // actually renders them, preserving the same Close/CopyRight/CopyLeft order the old
        // same-side-Dock stacking produced.
        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        rightGroup.Controls.Add(_closeBtn);
        rightGroup.Controls.Add(_copyRightBtn);
        rightGroup.Controls.Add(_copyLeftBtn);

        bottom.Controls.Add(_statusLabel);
        bottom.Controls.Add(rightGroup);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(_diffList);
        Controls.Add(bottom);
        Controls.Add(top);

        CancelButton = _closeBtn;
    }

    /// <summary>A native folder picker only ever browses the real local disk - picking one always
    /// switches that side to <see cref="LocalFileSystem"/>, independently of whatever it started
    /// on (a panel's archive or connection).</summary>
    private static void Browse(TextBox box, Action<IFileSystem> setFs)
    {
        using var dlg = new FolderBrowserDialog
        {
            SelectedPath = box.Text,
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            box.Text = dlg.SelectedPath;
            setFs(new LocalFileSystem());
        }
    }

    private async Task CompareAsync()
    {
        var L = LocalizationService.Current;
        var left = _leftBox.Text.Trim();
        var right = _rightBox.Text.Trim();
        var leftFs = _leftFs;
        var rightFs = _rightFs;

        var leftRoot = string.IsNullOrEmpty(left) ? null : await leftFs.GetFileInfoAsync(left).ConfigureAwait(true);
        var rightRoot = string.IsNullOrEmpty(right) ? null : await rightFs.GetFileInfoAsync(right).ConfigureAwait(true);
        if (IsDisposed || !IsHandleCreated) return;
        if (leftRoot is not { IsDirectory: true } || rightRoot is not { IsDirectory: true })
        {
            StyledMessageBox.Show(L.GetString("SyncDirs.BadPaths"),
                L.GetString("SyncDirs.Title"), MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
            return;
        }

        _compareBtn.Enabled = false;
        _diffList.Items.Clear();
        _entries.Clear();
        _statusLabel.Text = L.GetString("SyncDirs.Scanning");

        try
        {
            var ignoreTime = _ignoreTimeCheck.Checked;
            var subdirs = _subdirsCheck.Checked;

            var leftMap = await BuildMapAsync(leftFs, left, subdirs).ConfigureAwait(true);
            var rightMap = await BuildMapAsync(rightFs, right, subdirs).ConfigureAwait(true);
            if (IsDisposed || !IsHandleCreated) return;

            var paths = CombinePathKeys(leftMap, rightMap);

            int leftOnly = 0, rightOnly = 0, diff = 0, equal = 0;

            foreach (var path in paths)
            {
                var l = leftMap.GetValueOrDefault(path);
                var r = rightMap.GetValueOrDefault(path);

                SyncStatus status;
                if (l == null) { status = SyncStatus.RightOnly; rightOnly++; }
                else if (r == null) { status = SyncStatus.LeftOnly; leftOnly++; }
                else if (l.IsDirectory != r.IsDirectory) { status = SyncStatus.TypeDiffers; diff++; }
                else if (l.IsDirectory) { status = SyncStatus.Equal; equal++; }
                else if (l.Size != r.Size) { status = SyncStatus.SizeDiffers; diff++; }
                else if (!ignoreTime && l.LastWriteTimeUtc != r.LastWriteTimeUtc) { status = SyncStatus.TimeDiffers; diff++; }
                else { status = SyncStatus.Equal; equal++; }

                var entry = new SyncEntry(path, l, r, status);
                _entries.Add(entry);
                AddRow(entry);
            }

            _statusLabel.Text = L.GetString("SyncDirs.Summary", _entries.Count, equal, diff, leftOnly, rightOnly);
        }
        catch (Exception ex)
        {
            LogService.Error("SyncDirs compare failed", ex);
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
                _compareBtn.Enabled = true;
        }
    }

    private void AddRow(SyncEntry entry)
    {
        var L = LocalizationService.Current;
        var lvi = new ListViewItem(StatusGlyph(entry.Status))
        {
            Tag = entry,
            Checked = entry.Status != SyncStatus.Equal
        };
        lvi.SubItems.Add(entry.RelativePath);
        lvi.SubItems.Add(entry.Left is { IsDirectory: false } ? UiHelpers.FormatSize(entry.Left.Size) : "—");
        lvi.SubItems.Add(entry.Right is { IsDirectory: false } ? UiHelpers.FormatSize(entry.Right.Size) : "—");
        lvi.SubItems.Add(StatusLabel(L, entry.Status));
        _diffList.Items.Add(lvi);
    }

    private static string StatusGlyph(SyncStatus s) => s switch
    {
        SyncStatus.Equal => "=",
        SyncStatus.SizeDiffers => "!=",
        SyncStatus.TimeDiffers => "<>",
        SyncStatus.TypeDiffers => "?!",
        SyncStatus.LeftOnly => "L",
        SyncStatus.RightOnly => "R",
        _ => "?"
    };

    private static string StatusLabel(LocalizationService L, SyncStatus s) => s switch
    {
        SyncStatus.Equal => L.GetString("SyncDirs.StatusEqual"),
        SyncStatus.SizeDiffers => L.GetString("SyncDirs.StatusSize"),
        SyncStatus.TimeDiffers => L.GetString("SyncDirs.StatusTime"),
        SyncStatus.TypeDiffers => L.GetString("SyncDirs.StatusType"),
        SyncStatus.LeftOnly => L.GetString("SyncDirs.StatusLeftOnly"),
        SyncStatus.RightOnly => L.GetString("SyncDirs.StatusRightOnly"),
        _ => "?"
    };

    /// <summary>Whether a checked row with the given status should actually be queued for a copy
    /// in the given direction. SyncStatus.Equal is included on purpose: the list's checkboxes
    /// are enabled on every row with no restriction, so a user can deliberately check an "="
    /// row (e.g. to force a re-copy despite matching size/timestamp, if they suspect bit-rot) -
    /// excluding it here silently dropped that row from the queue with no error, even though the
    /// checkbox the user sees stays checked. Both sides exist by definition for an Equal entry,
    /// so including it is safe regardless of copy direction.</summary>
    private static bool ShouldInclude(SyncStatus status, SyncDirection dir) =>
        dir == SyncDirection.LeftToRight
            ? status is SyncStatus.LeftOnly or SyncStatus.SizeDiffers or SyncStatus.TimeDiffers or SyncStatus.TypeDiffers or SyncStatus.Equal
            : status is SyncStatus.RightOnly or SyncStatus.SizeDiffers or SyncStatus.TimeDiffers or SyncStatus.TypeDiffers or SyncStatus.Equal;

    private void IssueCopy(SyncDirection dir)
    {
        var left = _leftBox.Text.Trim();
        var right = _rightBox.Text.Trim();
        var queue = new List<FileEntry>();
        var destRoot = dir == SyncDirection.LeftToRight ? right : left;
        var sourceRoot = dir == SyncDirection.LeftToRight ? left : right;
        var sourceFs = dir == SyncDirection.LeftToRight ? _leftFs : _rightFs;
        var destFs = dir == SyncDirection.LeftToRight ? _rightFs : _leftFs;

        foreach (ListViewItem lvi in _diffList.Items)
        {
            if (!lvi.Checked) continue;
            if (lvi.Tag is not SyncEntry entry) continue;

            if (!ShouldInclude(entry.Status, dir)) continue;

            // The source side is guaranteed non-null for every status ShouldInclude admits (only
            // an entry missing entirely from the source - LeftOnly for RightToLeft, RightOnly for
            // LeftToRight - would have a null source here, and ShouldInclude already excludes it).
            var source = dir == SyncDirection.LeftToRight ? entry.Left : entry.Right;
            if (source is { IsDirectory: false }) queue.Add(source);
        }

        if (queue.Count == 0)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(L.GetString("SyncDirs.NothingToCopy"),
                L.GetString("SyncDirs.Title"), MsgBoxButtons.OK, MsgBoxIcon.Information, this);
            return;
        }

        CopyRequested?.Invoke(this, new SyncCopyRequest(dir, sourceFs, destFs, sourceRoot, destRoot, queue));
        Close();
    }

    /// <summary>Combines both sides' relative paths into one ordered, deduplicated sequence.
    /// Must use OrdinalIgnoreCase explicitly - leftMap/rightMap are themselves
    /// OrdinalIgnoreCase-keyed, but the default Union overload compares with ordinal
    /// (case-sensitive) equality instead, so a path existing on both sides but differing only by
    /// case (plausible whenever the two trees were populated independently) used to produce two
    /// distinct strings here. Each then looked up the very same entry in both dictionaries
    /// (whose own lookups ARE case-insensitive), so the same file ended up in the diff list -
    /// and counted in the summary - twice.</summary>
    private static IEnumerable<string> CombinePathKeys(
        Dictionary<string, FileEntry> leftMap, Dictionary<string, FileEntry> rightMap) =>
        leftMap.Keys.Union(rightMap.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

    /// <summary>VFS equivalent of the old <c>DirectoryInfo</c>-based walk - works against local
    /// disk, an archive, or a remote connection alike, since it only ever calls through
    /// <paramref name="fs"/>. When <paramref name="subdirs"/> is false, only <paramref name="root"/>'s
    /// own immediate files are listed (mirrors the original: subdirectories were never even pushed
    /// onto the walk stack in that case).</summary>
    private static async Task<Dictionary<string, FileEntry>> BuildMapAsync(IFileSystem fs, string root, bool subdirs)
    {
        var map = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IReadOnlyList<FileEntry> children;
            try
            {
                children = await fs.EnumerateAsync(dir, includeHidden: false).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogService.Warning($"SyncDirs: cannot enumerate {dir}: {ex.Message}");
                continue;
            }

            foreach (var entry in children)
            {
                var rel = VfsPath.GetRelative(root, entry.FullPath);
                if (entry.IsDirectory)
                {
                    if (!subdirs) continue;
                    map[rel] = entry;
                    // A junction/symlink can point back at an ancestor (e.g. a self-referencing
                    // directory junction) - pushing it onto the stack unconditionally would walk
                    // the same tree forever, growing map/stack without bound. List the reparse
                    // point itself (above) but don't descend through it. Archive/remote providers
                    // never set ReparsePoint, so this only ever filters local disk.
                    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
                        stack.Push(entry.FullPath);
                }
                else
                {
                    map[rel] = entry;
                }
            }
        }

        return map;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _leftBox?.Dispose();
            _rightBox?.Dispose();
            _diffList?.Dispose();
            _compareBtn?.Dispose();
            _copyLeftBtn?.Dispose();
            _copyRightBtn?.Dispose();
            _subdirsCheck?.Dispose();
            _ignoreTimeCheck?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Represents the comparison status of a single file or directory between left and right.</summary>
public enum SyncStatus { Equal, SizeDiffers, TimeDiffers, TypeDiffers, LeftOnly, RightOnly }
/// <summary>Indicates the direction of a copy operation requested by the user.</summary>
public enum SyncDirection { LeftToRight, RightToLeft }

/// <summary>A single row in the sync-differences list, pairing the relative path with left/right
/// <see cref="FileEntry"/> snapshots (from each side's own <see cref="IFileSystem"/>) and status.</summary>
/// <param name="RelativePath">Path relative to the comparison root.</param>
/// <param name="Left">Entry from the left directory, or <c>null</c> if absent.</param>
/// <param name="Right">Entry from the right directory, or <c>null</c> if absent.</param>
/// <param name="Status">Comparison result for this entry.</param>
public sealed record SyncEntry(string RelativePath, FileEntry? Left, FileEntry? Right, SyncStatus Status);

/// <summary>Event data for <see cref="SyncDirsForm.CopyRequested"/>, describing which files to copy,
/// in which direction, and on which file systems - a copy can cross from a local folder into an
/// archive or a remote connection (or vice versa) exactly like an ordinary panel-to-panel copy.</summary>
/// <param name="Direction">Copy direction (left-to-right or right-to-left).</param>
/// <param name="SourceFs">File system the source side lives on.</param>
/// <param name="DestFs">File system the destination side lives on.</param>
/// <param name="SourceRoot">Root path of the source side (left or right, per <paramref name="Direction"/>).</param>
/// <param name="DestRoot">Root path of the destination side.</param>
/// <param name="Items">Checked entries to copy, each resolved relative to <paramref name="SourceRoot"/>/<paramref name="DestRoot"/> by the copy operation.</param>
public sealed record SyncCopyRequest(
    SyncDirection Direction, IFileSystem SourceFs, IFileSystem DestFs, string SourceRoot, string DestRoot, List<FileEntry> Items);
