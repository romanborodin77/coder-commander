using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Compares two directories by relative path + size + timestamp, lets the user
/// queue copy operations to bring them in line.
/// </summary>
public class SyncDirsForm : ThemedForm
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

    /// <summary>Raised when the user initiates a copy operation. The event data contains the direction and file queue.</summary>
    public event EventHandler<SyncCopyRequest>? CopyRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncDirsForm"/> class with the two directories to compare.
    /// </summary>
    /// <param name="leftPath">Path to the left (source) directory.</param>
    /// <param name="rightPath">Path to the right (destination) directory.</param>
    public SyncDirsForm(string leftPath, string rightPath)
    {
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
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        // Wide enough for the localized "Browse…" text - CreateThemedButton's own auto-sizing
        // would give it ~100px, and this used to be 80 (RoundedButton's EndEllipsis then silently
        // truncated it to "Brows..."). 100 wasn't enough once this same column started also
        // holding _compareBtn ("Compare" - longer than "Browse…"), which silently truncated to
        // "Comp..." the same way (found via the dotnet-debugger MCP server's check_layout(),
        // confirmed by looking at the actual screenshot - text truncation isn't something a
        // Bounds-based checker catches on its own).
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        for (int i = 0; i < 4; i++) top.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        top.Controls.Add(UiHelpers.CreateLabel(L.GetString("SyncDirs.Left")), 0, 0);
        _leftBox = UiHelpers.CreateTextBox(leftPath);
        _leftBox.Dock = DockStyle.Fill;
        top.Controls.Add(_leftBox, 1, 0);

        var leftBrowse = ThemedForm.CreateThemedButton(L.GetString("Common.Browse"));
        leftBrowse.Dock = DockStyle.Fill;
        leftBrowse.Margin = new Padding(8, 0, 0, 0);
        leftBrowse.Click += (_, _) => Browse(_leftBox);
        top.Controls.Add(UiHelpers.CreateLabel(""), 2, 0);
        top.Controls.Add(leftBrowse, 3, 0);

        top.Controls.Add(UiHelpers.CreateLabel(L.GetString("SyncDirs.Right")), 0, 1);
        _rightBox = UiHelpers.CreateTextBox(rightPath);
        _rightBox.Dock = DockStyle.Fill;
        top.Controls.Add(_rightBox, 1, 1);

        var rightBrowse = ThemedForm.CreateThemedButton(L.GetString("Common.Browse"));
        rightBrowse.Dock = DockStyle.Fill;
        rightBrowse.Margin = new Padding(8, 0, 0, 0);
        rightBrowse.Click += (_, _) => Browse(_rightBox);
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

    private void Browse(TextBox box)
    {
        using var dlg = new FolderBrowserDialog
        {
            SelectedPath = box.Text,
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            box.Text = dlg.SelectedPath;
    }

    private async Task CompareAsync()
    {
        var L = LocalizationService.Current;
        var left = _leftBox.Text.Trim();
        var right = _rightBox.Text.Trim();
        if (string.IsNullOrEmpty(left) || !Directory.Exists(left) ||
            string.IsNullOrEmpty(right) || !Directory.Exists(right))
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

            var leftMap = await Task.Run(() => BuildMap(left, subdirs));
            var rightMap = await Task.Run(() => BuildMap(right, subdirs));
            if (IsDisposed || !IsHandleCreated) return;

            var paths = leftMap.Keys.Union(rightMap.Keys).OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

            int leftOnly = 0, rightOnly = 0, diff = 0, equal = 0;

            foreach (var path in paths)
            {
                var l = leftMap.GetValueOrDefault(path);
                var r = rightMap.GetValueOrDefault(path);

                SyncStatus status;
                if (l == null) { status = SyncStatus.RightOnly; rightOnly++; }
                else if (r == null) { status = SyncStatus.LeftOnly; leftOnly++; }
                else if (l.IsDir != r.IsDir) { status = SyncStatus.TypeDiffers; diff++; }
                else if (l.IsDir) { status = SyncStatus.Equal; equal++; }
                else if (l.Size != r.Size) { status = SyncStatus.SizeDiffers; diff++; }
                else if (!ignoreTime && l.Modified != r.Modified) { status = SyncStatus.TimeDiffers; diff++; }
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
        lvi.SubItems.Add(entry.Left is { IsDir: false } ? UiHelpers.FormatSize(entry.Left.Size) : "—");
        lvi.SubItems.Add(entry.Right is { IsDir: false } ? UiHelpers.FormatSize(entry.Right.Size) : "—");
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

    private void IssueCopy(SyncDirection dir)
    {
        var left = _leftBox.Text.Trim();
        var right = _rightBox.Text.Trim();
        var queue = new List<(string source, string destination)>();
        var destRoot = dir == SyncDirection.LeftToRight ? right : left;
        var sourceRoot = dir == SyncDirection.LeftToRight ? left : right;

        foreach (ListViewItem lvi in _diffList.Items)
        {
            if (!lvi.Checked) continue;
            if (lvi.Tag is not SyncEntry entry) continue;

            var include = dir == SyncDirection.LeftToRight
                ? entry.Status is SyncStatus.LeftOnly or SyncStatus.SizeDiffers or SyncStatus.TimeDiffers or SyncStatus.TypeDiffers
                : entry.Status is SyncStatus.RightOnly or SyncStatus.SizeDiffers or SyncStatus.TimeDiffers or SyncStatus.TypeDiffers;

            if (!include) continue;

            var source = Path.Combine(sourceRoot, entry.RelativePath);
            var dest = Path.Combine(destRoot, entry.RelativePath);
            queue.Add((source, dest));
        }

        if (queue.Count == 0)
        {
            var L = LocalizationService.Current;
            StyledMessageBox.Show(L.GetString("SyncDirs.NothingToCopy"),
                L.GetString("SyncDirs.Title"), MsgBoxButtons.OK, MsgBoxIcon.Information, this);
            return;
        }

        CopyRequested?.Invoke(this, new SyncCopyRequest(dir, queue));
        Close();
    }

    private static Dictionary<string, FileSnapshot> BuildMap(string root, bool subdirs)
    {
        var map = new Dictionary<string, FileSnapshot>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(root));

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            try
            {
                foreach (var sub in dir.EnumerateDirectories())
                {
                    if ((sub.Attributes & FileAttributes.Hidden) != 0) continue;
                    if (!subdirs) continue;
                    var rel = Path.GetRelativePath(root, sub.FullName);
                    map[rel] = new FileSnapshot(sub.FullName, rel, true, 0, sub.LastWriteTimeUtc);
                    stack.Push(sub);
                }
                foreach (var f in dir.EnumerateFiles())
                {
                    if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
                    var rel = Path.GetRelativePath(root, f.FullName);
                    map[rel] = new FileSnapshot(f.FullName, rel, false, f.Length, f.LastWriteTimeUtc);
                }
            }
            catch (Exception ex)
            {
                LogService.Warning($"SyncDirs: cannot enumerate {dir.FullName}: {ex.Message}");
            }
        }

        return map;
    }
}

/// <summary>Represents the comparison status of a single file or directory between left and right.</summary>
public enum SyncStatus { Equal, SizeDiffers, TimeDiffers, TypeDiffers, LeftOnly, RightOnly }
/// <summary>Indicates the direction of a copy operation requested by the user.</summary>
public enum SyncDirection { LeftToRight, RightToLeft }

/// <summary>Snapshot of a file or directory entry captured during directory comparison.</summary>
/// <param name="FullPath">Absolute path on disk.</param>
/// <param name="RelPath">Path relative to the comparison root.</param>
/// <param name="IsDir"><c>true</c> if this entry is a directory.</param>
/// <param name="Size">File size in bytes; zero for directories.</param>
/// <param name="Modified">Last-write timestamp in UTC.</param>
public sealed record FileSnapshot(string FullPath, string RelPath, bool IsDir, long Size, DateTime Modified);

/// <summary>A single row in the sync-differences list, pairing the relative path with left/right snapshots and status.</summary>
/// <param name="RelativePath">Path relative to the comparison root.</param>
/// <param name="Left">Snapshot from the left directory, or <c>null</c> if absent.</param>
/// <param name="Right">Snapshot from the right directory, or <c>null</c> if absent.</param>
/// <param name="Status">Comparison result for this entry.</param>
public sealed record SyncEntry(string RelativePath, FileSnapshot? Left, FileSnapshot? Right, SyncStatus Status);

/// <summary>Event data for <see cref="SyncDirsForm.CopyRequested"/>, describing which files to copy and in which direction.</summary>
/// <param name="Direction">Copy direction (left-to-right or right-to-left).</param>
/// <param name="Items">Ordered list of (source, destination) path pairs.</param>
public sealed record SyncCopyRequest(SyncDirection Direction, List<(string Source, string Destination)> Items);
