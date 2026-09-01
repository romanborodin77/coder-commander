using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
using CoderCommander.Services.Search;

namespace CoderCommander.WinForms;

using DuplicateGroup = CoderCommander.Services.Search.DuplicateFinder.DuplicateGroup;

/// <summary>
/// Duplicate file finder dialog: scans a directory tree for files with identical content (same size
/// + same CRC32), displays them grouped, and lets the user delete selected duplicates or navigate
/// to them in the panel.
///
/// <para><b>VFS-aware.</b> Works through <see cref="IFileSystem"/> + <see cref="DuplicateFinder"/>,
/// so duplicates can be found inside archives and remote connections, not only on local paths.</para>
/// </summary>
public sealed partial class DuplicateFinderForm : ThemedForm
{
    private readonly IFileSystem _fs;
    private readonly string _rootPath;
    private CancellationTokenSource? _cts;
    private List<(DuplicateGroup Group, int FileIndex)> _allRows = new();
    private Font? _boldFont;

    /// <summary>Raised when "Go to" is clicked — navigates the panel to the file's directory.</summary>
    public event EventHandler<string>? GoToFileRequested;

    /// <summary>Raised when "Delete" is clicked — MainForm handles the actual deletion via
    /// <c>DeleteOperation</c> with confirmation.</summary>
    public event EventHandler<IReadOnlyList<string>>? DeleteRequested;

    /// <param name="fs">Filesystem to search.</param>
    /// <param name="rootPath">Root directory to scan recursively.</param>
    public DuplicateFinderForm(IFileSystem fs, string rootPath)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _fs = fs;
        _rootPath = rootPath;

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colName.Text = L.GetString("Dup.ColName");
        _colSize.Text = L.GetString("Dup.ColSize");
        _colPath.Text = L.GetString("Dup.ColPath");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _closeBtn.Click += (_, _) => Close();
        _gotoBtn.Click += (_, _) => OnGoTo();
        _deleteBtn.Click += (_, _) => OnDelete();
        _scanBtn.Click += (_, _) => _ = ScanAsync();

        _resultList.ItemSelectionChanged += (_, _) => UpdateButtonStates();
        _resultList.ItemChecked += (_, e) =>
        {
            // Header rows (Tag == null) must never stay checked — uncheck immediately.
            if (e.Item.Tag is null && e.Item.Checked)
                e.Item.Checked = false;
        };
        Load += (_, _) => _ = ScanAsync();
        FormClosing += (_, _) =>
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _boldFont?.Dispose();
            _boldFont = null;
        };
    }

    protected override void ApplyTheme()
    {
        base.ApplyTheme();
        // Recreate _boldFont so group headers follow the new theme's font family/size.
        // Update existing items' Font to avoid using a disposed font during a theme change mid-scan.
        var oldFont = _boldFont;
        _boldFont = new Font(ThemeService.Current.GridFont, FontStyle.Bold);
        if (oldFont != null)
        {
            foreach (ListViewItem lvi in _resultList.Items)
            {
                if (ReferenceEquals(lvi.Font, oldFont))
                    lvi.Font = _boldFont;
            }
            oldFont.Dispose();
        }
    }

    private async Task ScanAsync()
    {
        var L = LocalizationService.Current;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _scanBtn.Enabled = false;
        _deleteBtn.Enabled = false;
        _gotoBtn.Enabled = false;
        _resultList.Items.Clear();
        _allRows.Clear();
        _statusLabel.Text = L.GetString("Dup.Scanning");

        try
        {
            var groups = await Task.Run(() => DuplicateFinder.FindAsync(_fs, _rootPath, ct), ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested || IsDisposed) return;

            _boldFont ??= new Font(ThemeService.Current.GridFont, FontStyle.Bold);

            foreach (var group in groups)
            {
                // Group header row — uncheckable separator.
                var header = new ListViewItem(L.GetString("Dup.GroupHeader", group.Files.Count, UiHelpers.FormatSize(group.Size)))
                {
                    BackColor = ThemeService.Current.HeaderBackground,
                    ForeColor = ThemeService.Current.HeaderForeground,
                    Font = _boldFont
                };
                header.SubItems.Add("");
                header.SubItems.Add("");
                _resultList.Items.Add(header);

                foreach (var file in group.Files)
                {
                    var idx = _allRows.Count;
                    _allRows.Add((group, idx));

                    var dir = GetParentDirectory(file.FullPath);
                    var lvi = new ListViewItem(file.Name) { Tag = file.FullPath, Checked = false };
                    lvi.SubItems.Add(UiHelpers.FormatSize(file.Size));
                    lvi.SubItems.Add(dir);
                    _resultList.Items.Add(lvi);
                }
            }

            _statusLabel.Text = groups.Count > 0
                ? L.GetString("Dup.FoundGroups", groups.Count, groups.Sum(g => g.Files.Count))
                : L.GetString("Dup.NoDuplicates");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogService.Error("Duplicate scan failed", ex);
            _statusLabel.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
                _scanBtn.Enabled = true;
        }
    }

    /// <summary>Returns the parent directory of a path, handling local, VFS (<c>|</c>), and
    /// remote (<c>scheme://host/path</c>) forms without corrupting separators.</summary>
    private static string GetParentDirectory(string fullPath)
    {
        // Remote paths: smb://host/share/file → smb://host/share
        if (RemotePath.IsRemote(fullPath))
        {
            var path = RemotePath.PathOf(fullPath);
            if (path.Length == 0) return fullPath;
            var slash = path.LastIndexOf('/');
            if (slash <= 0) return RemotePath.GetRoot(fullPath);
            return RemotePath.Combine(RemotePath.GetRoot(fullPath), path[..slash]);
        }
        // Archive paths: archive.zip|inner/file → archive.zip|inner
        if (ArchivePath.IsArchivePath(fullPath))
        {
            var (container, inner) = ArchivePath.SplitPath(fullPath);
            if (inner.Length == 0) return fullPath;
            var slash = inner.LastIndexOfAny(['/', '\\']);
            if (slash <= 0) return container;
            return container + ArchivePath.Separator + inner[..slash];
        }
        // Local paths: use Path.GetDirectoryName which handles backslashes correctly.
        return Path.GetDirectoryName(fullPath) ?? fullPath;
    }

    private void UpdateButtonStates()
    {
        var hasChecked = _resultList.CheckedItems.Count > 0;
        var hasSelected = _resultList.SelectedItems.Count > 0;
        _deleteBtn.Enabled = hasChecked;
        _gotoBtn.Enabled = hasSelected;
    }

    private void OnGoTo()
    {
        if (_resultList.SelectedItems.Count == 0) return;
        var item = _resultList.SelectedItems[0];
        if (item.Tag is not string path) return;
        GoToFileRequested?.Invoke(this, path);
        Close();
    }

    private void OnDelete()
    {
        var L = LocalizationService.Current;
        var paths = _resultList.CheckedItems
            .Cast<ListViewItem>()
            .Where(i => i.Tag is string)
            .Select(i => (string)i.Tag!)
            .ToList();

        if (paths.Count == 0) return;

        // Warn the user — at least one file in each group must survive.
        var allGroupPaths = _allRows.Select(r => r.Group.Files.Select(f => f.FullPath).ToList()).ToList();
        var wouldDeleteAll = false;
        foreach (var groupPaths in allGroupPaths)
        {
            var remaining = groupPaths.Except(paths).Count();
            if (remaining == 0) { wouldDeleteAll = true; break; }
        }

        var msg = wouldDeleteAll
            ? L.GetString("Dup.DeleteAllWarning", paths.Count)
            : L.GetString("Dup.DeleteConfirm", paths.Count);

        if (StyledMessageBox.Show(msg, L.GetString("Dup.Title"),
            MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this) != MsgBoxResult.Yes) return;

        DeleteRequested?.Invoke(this, paths);
        _ = ScanAsync(); // refresh after delete — deletion runs async via DeleteOperation; scan
                         // may see files still present, but user can re-scan manually if needed.
    }

}
