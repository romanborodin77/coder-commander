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
public class DuplicateFinderForm : ThemedForm
{
    private readonly IFileSystem _fs;
    private readonly string _rootPath;
    private readonly ListView _resultList;
    private readonly Button _scanBtn;
    private readonly Button _deleteBtn;
    private readonly Button _gotoBtn;
    private readonly Button _closeBtn;
    private readonly Label _statusLabel;
    private CancellationTokenSource? _cts;
    private List<(DuplicateGroup Group, int FileIndex)> _allRows = new();

    /// <summary>Raised when "Go to" is clicked — navigates the panel to the file's directory.</summary>
    public event EventHandler<string>? GoToFileRequested;

    /// <summary>Raised when "Delete" is clicked — MainForm handles the actual deletion via
    /// <c>DeleteOperation</c> with confirmation.</summary>
    public event EventHandler<IReadOnlyList<string>>? DeleteRequested;

    /// <param name="fs">Filesystem to search.</param>
    /// <param name="rootPath">Root directory to scan recursively.</param>
    public DuplicateFinderForm(IFileSystem fs, string rootPath)
    {
        _fs = fs;
        _rootPath = rootPath;

        var L = LocalizationService.Current;
        Text = L.GetString("Dup.Title");
        ClientSize = new Size(700, 520);
        Resizable = true;
        MinimumSize = new Size(480, 360);

        var p = ThemeService.Current;

        _resultList = UiHelpers.CreateListView(
            (L.GetString("Dup.ColName"), 280),
            (L.GetString("Dup.ColSize"), 100),
            (L.GetString("Dup.ColPath"), 280));
        _resultList.Dock = DockStyle.Fill;
        _resultList.CheckBoxes = true;
        _resultList.FullRowSelect = true;

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

        _gotoBtn = ThemedForm.CreateThemedButton(L.GetString("Dup.GoTo"));
        _gotoBtn.Margin = new Padding(0, 0, 8, 0);
        _gotoBtn.Enabled = false;
        _gotoBtn.Click += (_, _) => OnGoTo();

        _deleteBtn = ThemedForm.CreateThemedButton(L.GetString("Dup.Delete"));
        _deleteBtn.Margin = new Padding(0, 0, 8, 0);
        _deleteBtn.Enabled = false;
        _deleteBtn.Click += (_, _) => OnDelete();

        _scanBtn = ThemedForm.CreateThemedButton(L.GetString("Dup.Scan"), accent: true);
        _scanBtn.Margin = new Padding(0);
        _scanBtn.Click += (_, _) => _ = ScanAsync();

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
        rightGroup.Controls.Add(_gotoBtn);
        rightGroup.Controls.Add(_deleteBtn);
        rightGroup.Controls.Add(_scanBtn);

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };
        bottomPanel.Controls.Add(_statusLabel);
        bottomPanel.Controls.Add(rightGroup);

        Controls.Add(_resultList);
        Controls.Add(bottomPanel);

        CancelButton = _closeBtn;
        _resultList.ItemSelectionChanged += (_, _) => UpdateButtonStates();
        Load += (_, _) => _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        var L = LocalizationService.Current;
        _cts?.Cancel();
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

            if (ct.IsCancellationRequested) return;

            foreach (var group in groups)
            {
                // Group header row — uncheckable separator.
                var header = new ListViewItem(L.GetString("Dup.GroupHeader", group.Files.Count, UiHelpers.FormatSize(group.Size)))
                {
                    BackColor = ThemeService.Current.HeaderBackground,
                    ForeColor = ThemeService.Current.HeaderForeground,
                    Font = new Font(ThemeService.Current.GridFont, FontStyle.Bold)
                };
                header.SubItems.Add("");
                header.SubItems.Add("");
                _resultList.Items.Add(header);

                foreach (var file in group.Files)
                {
                    var idx = _allRows.Count;
                    _allRows.Add((group, idx));

                    var dir = Path.GetDirectoryName(file.FullPath) ?? file.FullPath;
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
        _ = ScanAsync(); // refresh
    }
}
