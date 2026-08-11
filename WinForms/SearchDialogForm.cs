using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
using System.Text.RegularExpressions;

namespace CoderCommander.WinForms;

/// <summary>
/// File search dialog with name masks, content search, regex, subdirs.
/// </summary>
/// <remarks>
/// <para><b>No longer reachable from the UI.</b> Both the Commands menu entry and the toolbar
/// button now open <see cref="FindFilesForm"/>, which searches through <c>IFileSystem</c> and so
/// works inside an archive and on a connection as well as on a local disk, streams file contents
/// instead of reading them line by line with no binary check, and batches its results instead of
/// marshalling one callback per hit.</para>
///
/// <para>The file is kept rather than deleted because it still has one thing the replacement does
/// not: regular-expression matching, for both the name and the content. Whether that is worth
/// porting - content regex over a stream is a different problem from substring search, since a
/// match has no bounded length - is a decision for its own change, not a side effect of removing a
/// duplicated menu entry.</para>
/// </remarks>
public class SearchDialogForm : ThemedForm
{
    private readonly TextBox _pathBox;
    private readonly TextBox _patternBox;
    private readonly TextBox _contentBox;
    private readonly ThemedCheckBox _caseCheck;
    private readonly ThemedCheckBox _regexCheck;
    private readonly ThemedCheckBox _subdirsCheck;
    private readonly Button _searchBtn;
    private readonly Button _closeBtn;
    private readonly ListView _resultsList;
    private readonly Label _statusLabel;
    private CancellationTokenSource? _cts;

    /// <summary>Raised when a result item is double-clicked (navigate to it).</summary>
    public event EventHandler<FileSystemItem>? ResultActivated;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchDialogForm"/> class, starting the
    /// search from the specified directory.
    /// </summary>
    /// <param name="startPath">Root directory for the file search.</param>
    public SearchDialogForm(string startPath)
    {
        var L = LocalizationService.Current;
        Text = L.GetString("Search.Title");
        ClientSize = new Size(720, 556); // +16 to match the criteria panel's 176→192 growth below
        Resizable = true;
        MinimumSize = new Size(560, 400);

        var p = ThemeService.Current;

        // Top criteria panel
        var criteriaPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 5,
            // 5 rows * 32 + Padding(8+8) = 176 exactly - zero slack for any font/DPI growth.
            // 192 leaves ~16px of breathing room.
            Height = 192,
            BackColor = p.Background,
            Padding = new Padding(16, 8, 16, 8)
        };
        criteriaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        criteriaPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++)
            criteriaPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        int r = 0;
        criteriaPanel.Controls.Add(UiHelpers.CreateLabel(L.GetString("Search.Path")), 0, r);
        _pathBox = UiHelpers.CreateTextBox(startPath);
        var browseBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Browse"));
        // Reserve the button's own auto-computed width (from CreateThemedButton, based on its
        // text) plus a small gap - a plain Panel ignores Margin on docked children, so without
        // this wrapper the textbox and button would render flush against each other with no
        // visible separation, and a hardcoded narrower Width here would clip "Browse…"'s text.
        var browseWrap = new Panel { Dock = DockStyle.Right, Width = browseBtn.Width + 6, Padding = new Padding(6, 0, 0, 0) };
        browseBtn.Dock = DockStyle.Fill;
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _pathBox.Text };
            if (dlg.ShowDialog() == DialogResult.OK)
                _pathBox.Text = dlg.SelectedPath;
        };
        browseWrap.Controls.Add(browseBtn);
        // Margin = 0: pathPanel sits directly in criteriaPanel's TableLayoutPanel cell
        // (RowStyle Absolute 32) - the default 3px-per-side Control.Margin would shrink its
        // rendered height to 26px, 6px short of browseBtn's 32px CreateThemedButton height
        // (same trap as CreateBottomPanel/CopyMoveDialogForm's destPanel - confirmed via
        // check_layout()'s inconsistent_button_size finding + the exact Bounds numbers).
        var pathPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _pathBox.Dock = DockStyle.Fill;
        // Fill added first (docks last, gets whatever's left after browseWrap's Right claim) -
        // added in the opposite order, browseWrap's Right dock would be processed after Fill had
        // already claimed the whole pathPanel, and the two would overlap instead of sitting
        // side by side.
        pathPanel.Controls.Add(_pathBox);
        pathPanel.Controls.Add(browseWrap);
        criteriaPanel.Controls.Add(pathPanel, 1, r);
        r++;

        criteriaPanel.Controls.Add(UiHelpers.CreateLabel(L.GetString("Search.Pattern")), 0, r);
        _patternBox = UiHelpers.CreateTextBox("*.*");
        _patternBox.Dock = DockStyle.Fill;
        criteriaPanel.Controls.Add(_patternBox, 1, r);
        r++;

        criteriaPanel.Controls.Add(UiHelpers.CreateLabel(L.GetString("Search.Content")), 0, r);
        _contentBox = UiHelpers.CreateTextBox("");
        _contentBox.Dock = DockStyle.Fill;
        criteriaPanel.Controls.Add(_contentBox, 1, r);
        r++;

        // Checkboxes row. FlowLayoutPanel instead of two Dock.Left checkboxes: same-side Dock
        // stacks from the last-added control outward, so the old order actually rendered Regex
        // to the left of Case (reversed from reading order) - and Dock.Left ignores Margin
        // regardless, so the 8px gap between them was never really there either.
        _caseCheck = UiHelpers.CreateCheckBox(L.GetString("Search.CaseSensitive"));
        _caseCheck.Width = 140;
        _caseCheck.Margin = new Padding(0, 0, 8, 0);
        _regexCheck = UiHelpers.CreateCheckBox(L.GetString("Search.UseRegex"));
        _regexCheck.Width = 140;
        _regexCheck.Margin = new Padding(0);
        // Margin = 0: same TableLayoutPanel-cell trap as pathPanel above - without it, this
        // panel's coded Height=32 rendered as 26px, shorter than _caseCheck/_regexCheck's own
        // height, so the checkboxes extended past their parent's reported bounds.
        var checkPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = 32,
            Margin = new Padding(0),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        checkPanel.Controls.Add(_caseCheck);
        checkPanel.Controls.Add(_regexCheck);
        criteriaPanel.Controls.Add(checkPanel, 1, r);
        r++;

        _subdirsCheck = UiHelpers.CreateCheckBox(L.GetString("Search.Subdirs"), true);
        _subdirsCheck.Dock = DockStyle.Fill;
        criteriaPanel.Controls.Add(_subdirsCheck, 1, r);
        r++;

        // Results list
        _resultsList = UiHelpers.CreateListView(
            (L.GetString("Panel.Name"), 300),
            (L.GetString("Panel.Size"), 100),
            (L.GetString("Panel.Modified"), 150),
            (L.GetString("Panel.Path"), 250));
        _resultsList.Dock = DockStyle.Fill;
        _resultsList.DoubleClick += OnResultDoubleClick;

        // Status + buttons
        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = p.HeaderBackground, Tag = ThemeRole.HeaderBackground, Padding = new Padding(16, 8, 16, 8) };
        _statusLabel = new Label { Dock = DockStyle.Fill, ForeColor = p.DimForeground, Font = p.GridFont, TextAlign = ContentAlignment.MiddleLeft, Tag = ThemeRole.Muted };
        _searchBtn = ThemedForm.CreateThemedButton(L.GetString("Search.Start"), accent: true);
        _searchBtn.Margin = new Padding(0);
        _searchBtn.Click += (_, _) => _ = StartSearch();
        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        _closeBtn.Margin = new Padding(0, 0, 8, 0);
        _closeBtn.Click += (_, _) => Close();

        // Fill added first (docks last, gets the remainder), then the buttons in a right-aligned
        // FlowLayoutPanel - Dock.Right ignores Margin entirely, which is what silently collapsed
        // the gap between these two buttons before.
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
        rightGroup.Controls.Add(_searchBtn);

        bottomPanel.Controls.Add(_statusLabel);
        bottomPanel.Controls.Add(rightGroup);

        Controls.Add(criteriaPanel);
        Controls.Add(bottomPanel);
        Controls.Add(_resultsList);
    }

    private void OnResultDoubleClick(object? sender, EventArgs e)
    {
        if (_resultsList.SelectedItems.Count > 0)
        {
            if (_resultsList.SelectedItems[0].Tag is FileSystemItem item)
                ResultActivated?.Invoke(this, item);
        }
    }

    private async Task StartSearch()
    {
        var L = LocalizationService.Current;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        _searchBtn.Text = L.GetString("Search.Stop");
        _statusLabel.Text = L.GetString("Search.Searching");
        _resultsList.Items.Clear();

        try
        {
            var path = _pathBox.Text;
            var pattern = _patternBox.Text;
            var content = _contentBox.Text;
            var subdirs = _subdirsCheck.Checked;
            var caseSensitive = _caseCheck.Checked;
            var useRegex = _regexCheck.Checked;
            var ct = _cts.Token;

            // ReparsePointGuard.SkipRecursion: the SearchOption shorthand has no way to skip
            // reparse points, so this is EnumerationOptions instead - without it a subdirectory
            // search followed a junction and reported matches from outside the searched folder.
            var searchOpts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = subdirs,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | ReparsePointGuard.SkipRecursion
            };
            var dir = new DirectoryInfo(path);
            int found = 0;

            await Task.Run(() =>
            {
                foreach (var fsi in dir.EnumerateFileSystemInfos("*", searchOpts))
                {
                    ct.ThrowIfCancellationRequested();

                    bool nameMatch = string.IsNullOrEmpty(pattern) || pattern == "*.*" ||
                        MatchesPattern(fsi.Name, pattern, caseSensitive, useRegex);

                    if (!nameMatch) continue;

                    bool contentMatch = true;
                    if (!string.IsNullOrEmpty(content) && fsi is FileInfo fi)
                    {
                        contentMatch = FileContainsText(fi.FullName, content, caseSensitive, useRegex);
                    }

                    if (contentMatch)
                    {
                        found++;
                        if (IsDisposed || !IsHandleCreated) continue;
                        try
                        {
                            BeginInvoke(() =>
                            {
                                var entry = FileEntry.FromFileSystemInfo(fsi.FullName, fsi);
                                var item = new FileSystemItem(entry);
                                var lvi = new ListViewItem(item.Name);
                                lvi.SubItems.Add(item.SizeDisplay);
                                lvi.SubItems.Add(item.ModifiedDisplay);
                                lvi.SubItems.Add(Path.GetDirectoryName(fsi.FullName) ?? "");
                                lvi.Tag = item;
                                _resultsList.Items.Add(lvi);
                                _statusLabel.Text = L.GetString("Search.Found", found);
                            });
                        }
                        catch (InvalidOperationException) { }
                    }
                }
            }, ct);

            if (found == 0 && !IsDisposed && IsHandleCreated)
                _statusLabel.Text = L.GetString("Search.NoResults");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = ex.Message;
        }
        finally
        {
            if (!IsDisposed && IsHandleCreated)
                _searchBtn.Text = L.GetString("Search.Start");
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        base.OnFormClosed(e);
    }

    private static bool MatchesPattern(string name, string pattern, bool caseSensitive, bool useRegex)
    {
        if (useRegex)
        {
            try
            {
                var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                return Regex.IsMatch(name, pattern, opts);
            }
            catch { return false; }
        }

        var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        var opts2 = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
        return Regex.IsMatch(name, regexPattern, opts2);
    }

    private static bool FileContainsText(string path, string searchText, bool caseSensitive, bool useRegex)
    {
        try
        {
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            using var reader = new StreamReader(path);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (useRegex)
                {
                    var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    if (Regex.IsMatch(line, searchText, opts, TimeSpan.FromSeconds(1)))
                        return true;
                }
                else
                {
                    if (line.Contains(searchText, comparison))
                        return true;
                }
            }
            return false;
        }
        catch { return false; }
    }
}
