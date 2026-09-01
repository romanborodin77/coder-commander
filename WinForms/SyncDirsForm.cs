using CoderCommander.FileSystem;
using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Compares two directories by relative path + size + timestamp, lets the user
/// queue copy operations to bring them in line.
/// </summary>
public sealed partial class SyncDirsForm : ThemedForm
{
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
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _leftFs = leftFs;
        _rightFs = rightFs;

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colStatus.Text = L.GetString("SyncDirs.Status");
        _colPath.Text = L.GetString("SyncDirs.Path");
        _colLeftSize.Text = L.GetString("SyncDirs.LeftSize");
        _colRightSize.Text = L.GetString("SyncDirs.RightSize");
        _colAction.Text = L.GetString("SyncDirs.Action");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _leftBox.Text = leftPath;
        _rightBox.Text = rightPath;
        _subdirsCheck.Checked = true;

        _leftBrowse.Click += (_, _) => Browse(_leftBox, fs => _leftFs = fs);
        _rightBrowse.Click += (_, _) => Browse(_rightBox, fs => _rightFs = fs);
        _compareBtn.Click += (_, _) => _ = CompareAsync();
        _closeBtn.Click += (_, _) => Close();
        _copyRightBtn.Click += (_, _) => IssueCopy(SyncDirection.LeftToRight);
        _copyLeftBtn.Click += (_, _) => IssueCopy(SyncDirection.RightToLeft);
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
