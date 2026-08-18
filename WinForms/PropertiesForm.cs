using CoderCommander.FileSystem;
using CoderCommander.Models;
using CoderCommander.Services;
using System.Globalization;

namespace CoderCommander.WinForms;

/// <summary>
/// File/directory properties dialog. Read-only summary plus an editable
/// Supports single item and multi-selection.
/// </summary>
public class PropertiesForm : ThemedForm
{
    private static readonly FileAttributes[] EditableFlags =
    {
        FileAttributes.ReadOnly,
        FileAttributes.Hidden,
        FileAttributes.System,
        FileAttributes.Archive
    };

    private readonly IReadOnlyList<FileSystemItem> _items;
    private readonly IFileSystem _fs;
    private readonly bool _isSingle;
    private readonly bool _isDirectory;

    // For single-directory recursive scans.
    private CancellationTokenSource? _cts;
    private Label? _filesLabel;
    private Label? _subdirsLabel;
    private Label? _totalSizeLabel;

    // Four attribute checkboxes (three-state) + matching status labels.
    private readonly ThemedCheckBox[] _attrCheckboxes = new ThemedCheckBox[4];
    private readonly Label[] _attrStatusLabels = new Label[4];

    // Three timestamp checkboxes (two-state) + pickers (single item only).
    private readonly ThemedCheckBox[] _timeCheckboxes = new ThemedCheckBox[3];
    private readonly DateTimePicker?[] _timePickers = new DateTimePicker?[3];
    private readonly string[] _timeKeys = { "Props.Modified", "Props.Created", "Props.Accessed" };

    private ThemedCheckBox? _recursiveCheckbox;
    private Label? _statusLabel;

    // Snapshot of the original attribute per item, used by Reset and by Indeterminate semantics.
    private readonly FileAttributes[] _originalAttributes;

    // Snapshot of original timestamps for the single item, used by Reset.
    private DateTime _origModified;
    private DateTime _origCreated;
    private DateTime _origAccessed;

    /// <summary>
    /// Initializes the properties dialog for one or more selected files/directories.
    /// Shows read-only info, editable attributes, and (for single items) editable timestamps.
    /// </summary>
    /// <param name="items">Selected filesystem items to display/edit properties for.</param>
    /// <param name="fs">The filesystem the items belong to — needed for VFS-aware size calculation
    /// and attribute changes (archive/remote providers).</param>
    public PropertiesForm(IFileSystem fs, IReadOnlyList<FileSystemItem> items)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _isSingle = items.Count == 1;
        _isDirectory = _isSingle && items[0].IsDirectory;
        _originalAttributes = new FileAttributes[items.Count];
        for (int i = 0; i < items.Count; i++)
            _originalAttributes[i] = items[i].Attributes;

        Resizable = true;
        MaximizeBox = false;
        MinimizeBox = false;
        // 540, not 520: the content column below is Absolute 480 plus Padding(20,_,20,_) = 520,
        // which is exactly the old window width with nothing left over for the AutoScroll
        // panel's ~17px vertical scrollbar - the header/info labels lost their last ~15-30px to
        // AutoEllipsis as a result.
        MinimumSize = new Size(540, 360);
        MaximumSize = new Size(540, 1200);

        var L = LocalizationService.Current;
        var p = ThemeService.Current;
        Text = _isSingle
            ? $"{L.GetString("Props.Title")} — {_items[0].Name}"
            : string.Format(CultureInfo.InvariantCulture, L.GetString("Props.MultiTitle"), items.Count);

        // ── Bottom button bar (added first so it docks Bottom) ──
        var bottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 54,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 10, 16, 10)
        };

        var closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        closeBtn.Margin = new Padding(0, 0, 8, 0);
        closeBtn.DialogResult = DialogResult.Cancel;
        closeBtn.Click += (_, _) => Close();

        var applyBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Apply"), accent: true);
        applyBtn.Margin = new Padding(0);
        applyBtn.Click += (_, _) => ApplyChanges();

        // Both were Dock.Right (ignores Margin, and same-side Dock stacks from the last-added
        // control outward) - that had rendered Close as the rightmost/primary-looking button
        // instead of the accent Apply button, with no visible gap between them either.
        var rightGroup = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        rightGroup.Controls.Add(closeBtn);
        rightGroup.Controls.Add(applyBtn);

        var resetBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Reset"));
        resetBtn.Dock = DockStyle.Left;
        resetBtn.Click += (_, _) => ResetToOriginal();

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = p.DimForeground,
            Font = p.GridFont,
            Text = "",
            AutoEllipsis = true,
            Tag = ThemeRole.Muted
        };

        bottom.Controls.Add(_statusLabel);
        bottom.Controls.Add(rightGroup);
        bottom.Controls.Add(resetBtn);

        AcceptButton = applyBtn;
        CancelButton = closeBtn;

        // ── Scrollable content ──
        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = p.Background
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 0,
            BackColor = p.Background,
            Padding = new Padding(20, 18, 20, 12)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 480));
        scroll.Controls.Add(root);
        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(scroll);
        Controls.Add(bottom);

        BuildHeader(root);
        BuildInfoSection(root);
        BuildAttributesSection(root);

        if (_isSingle && _isDirectory)
            BuildRecursiveCheckbox(root);

        if (_isSingle && _fs.Capabilities.HasFlag(FileSystemCapabilities.NativePaths))
            BuildTimestampSection(root);

        // Kick off async scan for single-directory case.
        if (_isSingle && _isDirectory)
            BeginScanDirectory(_items[0].FullPath);

        ComputeClientHeight();
    }

    // ── Header (large icon + name + type) ──────────────────────────────

    /// <summary>Builds the header section with large icon, name, and type label.</summary>
    private void BuildHeader(TableLayoutPanel root)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;
        FileSystemItem item = _items[0];

        FileIconType iconType;
        string typeText;
        string nameText;

        if (_isSingle)
        {
            nameText = item.Name;
            if (item.IsDirectory)
            {
                iconType = FileIconType.Folder;
                typeText = L.GetString("Props.Folder");
            }
            else
            {
                iconType = FileIcons.GetIconType(item.Extension);
                typeText = L.GetString("Props.File");
            }
        }
        else
        {
            int fc = 0, dc = 0;
            foreach (var it in _items) { if (it.IsDirectory) dc++; else fc++; }
            nameText = string.Format(CultureInfo.InvariantCulture, L.GetString("Props.MultiTitle"), _items.Count);
            typeText = string.Format(CultureInfo.InvariantCulture, L.GetString("Props.CountFilesDirs"), fc, dc);
            iconType = FileIconType.File;
        }

        var headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 56,
            BackColor = p.Background
        };

        var iconBox = new PictureBox
        {
            Image = FileIcons.Get(iconType, 48),
            SizeMode = PictureBoxSizeMode.Normal,
            Location = new Point(0, 4),
            Size = new Size(48, 48),
            BackColor = p.Background
        };

        // Width reduced from the original 418: headerPanel's actual runtime width (it's
        // Dock=Fill inside a TableLayoutPanel cell) came out a few px narrower than 418+60
        // assumed, so the label's right edge extended past headerPanel's own bounds.
        // Anchor=Left|Right was tried first but made it worse - the
        // TableLayoutPanel cell's multi-pass layout captures the anchor's distance-from-right
        // against an earlier, narrower layout pass, so the label overshot to 674px wide instead
        // of shrinking to fit. A fixed, comfortably-under-budget Width is the reliable fix here.
        var nameLabel = new Label
        {
            Text = nameText,
            Font = p.GridFontBold,
            ForeColor = p.Foreground,
            BackColor = p.Background,
            AutoEllipsis = true,
            Location = new Point(60, 4),
            Size = new Size(400, 22),
            Tag = ThemeRole.Emphasis
        };

        var typeLabel = new Label
        {
            Text = typeText,
            Font = p.GridFont,
            ForeColor = p.DimForeground,
            BackColor = p.Background,
            AutoEllipsis = true,
            Location = new Point(60, 28),
            Size = new Size(400, 22),
            Tag = ThemeRole.Muted
        };

        headerPanel.Controls.Add(iconBox);
        headerPanel.Controls.Add(nameLabel);
        headerPanel.Controls.Add(typeLabel);

        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        root.Controls.Add(headerPanel, 0, root.RowCount - 1);

        AddSpacer(root, 8);
    }

    // ── Read-only info section ─────────────────────────────────────────

    /// <summary>Builds the read-only info section (name, path, size, type, dates, directory stats).</summary>
    private void BuildInfoSection(TableLayoutPanel root)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 0,
            BackColor = p.Background
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void AddRow(string key, string value, ref Label? valueLabel)
        {
            var lbl = UiHelpers.CreateLabel(L.GetString(key), bold: true);
            lbl.Dock = DockStyle.Fill;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Height = 26;

            var val = UiHelpers.CreateLabel(string.IsNullOrEmpty(value) ? "…" : value);
            val.AutoEllipsis = true;
            val.Dock = DockStyle.Fill;
            val.TextAlign = ContentAlignment.MiddleLeft;
            val.Height = 26;
            valueLabel ??= val;

            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            grid.Controls.Add(lbl, 0, grid.RowCount - 1);
            grid.Controls.Add(val, 1, grid.RowCount - 1);
        }

        void AddPlain(string key, string value)
        {
            var lbl = UiHelpers.CreateLabel(L.GetString(key), bold: true);
            lbl.Dock = DockStyle.Fill;
            lbl.TextAlign = ContentAlignment.MiddleLeft;
            lbl.Height = 26;

            var val = UiHelpers.CreateLabel(value);
            val.AutoEllipsis = true;
            val.Dock = DockStyle.Fill;
            val.TextAlign = ContentAlignment.MiddleLeft;
            val.Height = 26;

            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            grid.Controls.Add(lbl, 0, grid.RowCount - 1);
            grid.Controls.Add(val, 1, grid.RowCount - 1);
        }

        if (_isSingle)
        {
            FileSystemItem item = _items[0];
            AddPlain("Props.Name", item.Name);
            AddPlain("Props.Path", TrimPath(item.FullPath, 60));
            AddPlain("Props.Size", UiHelpers.FormatSize(item.Size));
            AddPlain("Props.Type", item.IsDirectory ? L.GetString("Props.Folder") : L.GetString("Props.File"));
            AddPlain("Props.Modified", item.Modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AddPlain("Props.Created", item.Created.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AddPlain("Props.Accessed", LocalAccessed(item).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            AddPlain("Props.Attributes", FormatAttributes(item.Attributes));

            if (item.IsDirectory)
            {
                // Placeholders updated asynchronously by BeginScanDirectory.
                AddRow("Props.Files", "", ref _filesLabel);
                AddRow("Props.Subdirs", "", ref _subdirsLabel);
                AddRow("Props.TotalSize", "", ref _totalSizeLabel);
            }
        }
        else
        {
            long totalBytes = 0;
            int fileCount = 0, dirCount = 0;
            foreach (var it in _items)
            {
                if (it.IsDirectory) dirCount++;
                else { fileCount++; totalBytes += it.Size; }
            }

            AddPlain("Props.Type", string.Format(CultureInfo.InvariantCulture, L.GetString("Props.CountFilesDirs"), fileCount, dirCount));
            AddPlain("Props.TotalSize", UiHelpers.FormatSize(totalBytes));
            AddPlain("Props.Size", UiHelpers.FormatSize(totalBytes));
            AddPlain("Props.Name", string.Format(CultureInfo.InvariantCulture, L.GetString("Props.MultiTitle"), _items.Count));
        }

        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(grid, 0, root.RowCount - 1);

        AddSpacer(root, 10);
    }

    // ── Attributes editor (three-state per checkbox) ───────────────────

    /// <summary>Builds the three-state attribute editor (ReadOnly, Hidden, System, Archive).</summary>
    private void BuildAttributesSection(TableLayoutPanel root)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        var header = UiHelpers.CreateLabel(L.GetString("Props.EditAttributes"), bold: true);
        header.Dock = DockStyle.Top;
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.Height = 24;
        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.Controls.Add(header, 0, root.RowCount - 1);

        var hint = new Label
        {
            Text = L.GetString("Props.AttrHint"),
            Font = p.GridFont,
            ForeColor = p.DimForeground,
            BackColor = p.Background,
            AutoEllipsis = true,
            Dock = DockStyle.Top,
            Height = 18,
            Tag = ThemeRole.Muted
        };
        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        root.Controls.Add(hint, 0, root.RowCount - 1);

        var attrGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = p.PanelBackground,
            Padding = new Padding(10, 6, 10, 6),
            Tag = ThemeRole.PanelBackground
        };
        attrGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        attrGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++)
            attrGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        static ThemedCheckBox.CheckState DeriveState(bool all, bool none)
        {
            if (all) return ThemedCheckBox.CheckState.Checked;
            if (none) return ThemedCheckBox.CheckState.Unchecked;
            return ThemedCheckBox.CheckState.Indeterminate;
        }

        for (int i = 0; i < 4; i++)
        {
            var flag = EditableFlags[i];
            int all = 0, none = 0;
            foreach (var attr in _originalAttributes)
            {
                if ((attr & flag) != 0) all++; else none++;
            }
            bool allSet = all == _items.Count;
            bool noneSet = none == _items.Count;
            var state = DeriveState(allSet, noneSet);

            var cb = new ThemedCheckBox
            {
                ThreeState = true,
                State = state,
                Text = L.GetString(AttrKey(i)),
                Font = p.GridFont,
                ForeColor = p.Foreground,
                BackColor = p.PanelBackground,
                Height = 28,
                Dock = DockStyle.Fill,
                Tag = ThemeRole.PanelBackground
            };
            var status = new Label
            {
                Text = StatusTextForState(state),
                Font = p.GridFont,
                ForeColor = p.DimForeground,
                BackColor = p.PanelBackground,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Tag = ThemeRole.Muted
            };
            cb.CheckedChanged += (_, _) => status.Text = StatusTextForState(cb.State);

            _attrCheckboxes[i] = cb;
            _attrStatusLabels[i] = status;
            attrGrid.Controls.Add(cb, 0, i);
            attrGrid.Controls.Add(status, 1, i);
        }

        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(attrGrid, 0, root.RowCount - 1);

        AddSpacer(root, 8);
    }

    /// <summary>Adds the "Apply recursively" checkbox for directory items.</summary>
    private void BuildRecursiveCheckbox(TableLayoutPanel root)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        _recursiveCheckbox = new ThemedCheckBox
        {
            Text = L.GetString("Props.Recursive"),
            Font = p.GridFont,
            ForeColor = p.Foreground,
            BackColor = p.Background,
            Height = 28,
            Dock = DockStyle.Top
        };
        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.Controls.Add(_recursiveCheckbox, 0, root.RowCount - 1);

        AddSpacer(root, 8);
    }

    // ── Timestamp editor (single item) ────────────────────────────────

    /// <summary>Builds the timestamp editor section with checkboxes and DateTimePickers (single item only).</summary>
    private void BuildTimestampSection(TableLayoutPanel root)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        var header = UiHelpers.CreateLabel(L.GetString("Props.EditTimestamps"), bold: true);
        header.Dock = DockStyle.Top;
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.Height = 24;
        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.Controls.Add(header, 0, root.RowCount - 1);

        var hint = new Label
        {
            Text = L.GetString("Props.TimestampHint"),
            Font = p.GridFont,
            ForeColor = p.DimForeground,
            BackColor = p.Background,
            AutoEllipsis = true,
            Dock = DockStyle.Top,
            Height = 18,
            Tag = ThemeRole.Muted
        };
        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        root.Controls.Add(hint, 0, root.RowCount - 1);

        FileSystemItem item = _items[0];
        _origModified = item.Modified;
        _origCreated = item.Created;
        _origAccessed = LocalAccessed(item);

        var timeGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            BackColor = p.PanelBackground,
            Padding = new Padding(10, 6, 10, 6),
            Tag = ThemeRole.PanelBackground
        };
        timeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        timeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
            timeGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        DateTime[] originals = { _origModified, _origCreated, _origAccessed };

        for (int i = 0; i < 3; i++)
        {
            int idx = i; // capture per-iteration to avoid shared-loop-variable latch
            var cb = new ThemedCheckBox
            {
                Checked = false,
                Text = L.GetString(_timeKeys[idx]).TrimEnd(':'),
                Font = p.GridFont,
                ForeColor = p.Foreground,
                BackColor = p.PanelBackground,
                Height = 30,
                Dock = DockStyle.Fill,
                Tag = ThemeRole.PanelBackground
            };

            var dtp = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "yyyy-MM-dd HH:mm:ss",
                Value = ClampDateTime(originals[idx]),
                Dock = DockStyle.Fill,
                Enabled = false
            };
            void OnChecked(object? s, EventArgs e)
            {
                if (cb.Checked)
                {
                    dtp.Enabled = true;
                    dtp.Value = ClampDateTime(originals[idx]);
                }
                else
                {
                    dtp.Enabled = false;
                }
            }
            cb.CheckedChanged += OnChecked;

            _timeCheckboxes[idx] = cb;
            _timePickers[idx] = dtp;
            timeGrid.Controls.Add(cb, 0, idx);
            timeGrid.Controls.Add(dtp, 1, idx);
        }

        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(timeGrid, 0, root.RowCount - 1);

        AddSpacer(root, 8);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>Returns the localization key for the attribute at index <paramref name="i"/>.</summary>
    private static string AttrKey(int i) => i switch
    {
        0 => "Props.ReadOnly",
        1 => "Props.Hidden",
        2 => "Props.System",
        3 => "Props.Archive",
        _ => ""
    };

    /// <summary>Returns the localized status text for a three-state checkbox value.</summary>
    private string StatusTextForState(ThemedCheckBox.CheckState s)
    {
        var L = LocalizationService.Current;
        return s switch
        {
            ThemedCheckBox.CheckState.Checked => L.GetString("SelectionChanged"),
            ThemedCheckBox.CheckState.Indeterminate => L.GetString("SelectionUnchanged"),
            _ => L.GetString("SelectionCleared")
        };
    }

    /// <summary>Returns the local LastAccessTime, falling back to Created if unavailable.</summary>
    private static DateTime LocalAccessed(FileSystemItem item)
        => item.Entry.LastAccessTimeUtc != DateTime.MinValue
            ? item.Entry.LastAccessTimeUtc.ToLocalTime()
            : item.Created;

    /// <summary>Clamps a <see cref="DateTime"/> to the valid range of <see cref="DateTimePicker"/>.</summary>
    private static DateTime ClampDateTime(DateTime value)
    {
        if (value < DateTimePicker.MinimumDateTime)
            return DateTimePicker.MinimumDateTime;
        if (value > DateTimePicker.MaximumDateTime)
            return DateTimePicker.MaximumDateTime;
        return value;
    }

    /// <summary>Truncates a path from the left with an ellipsis when it exceeds <paramref name="max"/> characters.</summary>
    private static string TrimPath(string path, int max)
    {
        if (string.IsNullOrEmpty(path)) return path;
        return path.Length <= max ? path : "…" + path[^max..];
    }

    /// <summary>Formats file attributes into a compact string like "RHSA DE".</summary>
    private static string FormatAttributes(FileAttributes attr)
    {
        var sb = new System.Text.StringBuilder();
        foreach (FileAttributes f in EditableFlags)
            sb.Append((attr & f) != 0 ? f.ToString()[0] : '-');
        sb.Append(' ');
        if ((attr & FileAttributes.Directory) != 0) sb.Append('D');
        if ((attr & FileAttributes.Compressed) != 0) sb.Append('C');
        if ((attr & FileAttributes.Encrypted) != 0) sb.Append('E');
        return sb.ToString();
    }

    /// <summary>Adds a vertical spacer row of the given height.</summary>
    private void AddSpacer(TableLayoutPanel root, int h)
    {
        var sp = new Panel
        {
            Dock = DockStyle.Top,
            Height = h,
            BackColor = ThemeService.Current.Background
        };
        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        root.Controls.Add(sp, 0, root.RowCount - 1);
    }

    /// <summary>Updates the bottom status label text.</summary>
    private void SetStatus(string text)
    {
        if (_statusLabel != null) _statusLabel.Text = text;
    }

    // ── Async directory scan (single folder only) ──────────────────────

    /// <summary>Asynchronously scans a directory tree for file count, subdirectory count, and total size.</summary>
    private void BeginScanDirectory(string path)
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(async () =>
        {
            long totalSize = 0;
            int files = 0, dirs = 0;
            try
            {
                // VFS-aware deep enumeration through IFileSystem — works for archives/remote too.
                var entries = await _fs.EnumerateDeepAsync(path, includeHidden: true, token)
                    .ConfigureAwait(false);
                foreach (var entry in entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.IsDirectory) dirs++;
                    else { totalSize += entry.Size; files++; }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                LogService.Warning($"Directory scan failed for {path}: {ex.Message}");
            }

            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(() =>
                {
                    if (_filesLabel != null) _filesLabel.Text = files.ToString(CultureInfo.InvariantCulture);
                    if (_subdirsLabel != null) _subdirsLabel.Text = dirs.ToString(CultureInfo.InvariantCulture);
                    if (_totalSizeLabel != null) _totalSizeLabel.Text = UiHelpers.FormatSize(totalSize);
                });
            }
            catch (InvalidOperationException) { }
        }, token);
    }

    // ── Apply / Reset ──────────────────────────────────────────────────

    /// <summary>Applies the edited attributes and timestamps to all selected items.</summary>
    private void ApplyChanges()
    {
        var L = LocalizationService.Current;
        int failures = 0;
        int success = 0;

        // Attributes: for each item, build new mask, possibly recursive.
        for (int idx = 0; idx < _items.Count; idx++)
        {
            var original = _originalAttributes[idx];
            var target = _items[idx].FullPath;

            var newAttr = BuildAttributeMask(original);
            try
            {
                ApplyAttributeToPath(target, newAttr, original);
                success++;

                if (_isSingle && _isDirectory && _recursiveCheckbox?.Checked == true)
                {
                    // VFS-aware recursive attribute apply through IFileSystem.
                    IReadOnlyList<FileEntry> children;
                    try
                    {
                        children = _fs.EnumerateDeepAsync(target, includeHidden: true, CancellationToken.None)
                            .GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        LogService.Warning($"Recursive enumerate failed for {target}: {ex.Message}");
                        children = Array.Empty<FileEntry>();
                    }
                    foreach (var entry in children)
                    {
                        try
                        {
                            var childOrig = entry.Attributes;
                            var childNew = BuildAttributeMask(childOrig);
                            ApplyAttributeToPath(entry.FullPath, childNew, childOrig);
                            success++;
                        }
                        catch (Exception ex)
                        {
                            LogService.Warning($"Recursive attribute failed for {entry.FullPath}: {ex.Message}");
                            failures++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error($"SetAttributes failed for {target}: {ex.Message}", ex);
                failures++;
            }
        }

        // Timestamps (single item only — section hidden for multi).
        if (_isSingle)
        {
            var target = _items[0].FullPath;
            var isDir = _isDirectory;
            bool recursive = isDir && _recursiveCheckbox?.Checked == true;

            for (int i = 0; i < 3; i++)
            {
                var cb = _timeCheckboxes[i];
                var dtp = _timePickers[i];
                if (cb == null || !cb.Checked || dtp == null || !dtp.Enabled) continue;

                DateTime value = dtp.Value;
                try
                {
                    ApplyTimestamp(target, i, value, recursive && isDir);
                    success++;
                }
                catch (Exception ex)
                {
                    LogService.Warning($"SetTimestamp[{i}] failed for {target}: {ex.Message}");
                    failures++;
                }
            }
        }

        if (failures > 0)
            SetStatus($"{L.GetString("Common.Error")}: {failures} failed");
        else if (_isSingle)
            SetStatus(L.GetString("Props.Applied"));
        else
            SetStatus(string.Format(CultureInfo.InvariantCulture, L.GetString("Props.ApplyToAll"), success));
    }

    /// <summary>Builds a new attribute mask by applying three-state checkbox states to the original attributes.</summary>
    private FileAttributes BuildAttributeMask(FileAttributes original)
    {
        var result = original;
        for (int i = 0; i < 4; i++)
        {
            var flag = EditableFlags[i];
            var state = _attrCheckboxes[i].State;
            if (state == ThemedCheckBox.CheckState.Checked)
                result |= flag;
            else if (state == ThemedCheckBox.CheckState.Unchecked)
                result &= ~flag;
            // Indeterminate → leave the original bit untouched (already in result).
        }
        return result;
    }

    /// <summary>Applies a computed attribute mask to a filesystem path, preserving non-editable bits.</summary>
    private void ApplyAttributeToPath(string path, FileAttributes newAttr, FileAttributes original)
    {
        // Preserve any non-editable bits the OS may not allow changing directly.
        newAttr = (original & ~(FileAttributes.ReadOnly | FileAttributes.Hidden
                              | FileAttributes.System | FileAttributes.System | FileAttributes.Archive))
                | (newAttr & (FileAttributes.ReadOnly | FileAttributes.Hidden
                            | FileAttributes.System | FileAttributes.Archive));
        _fs.SetAttributesAsync(path, newAttr, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Sets a timestamp (modified/created/accessed) on a file, optionally recursing into directories.
    /// Only callable for NativePaths filesystems — IFileSystem has no SetLastWriteTimeAsync, so this
    /// uses System.IO directly. The timestamp section is hidden for non-native providers.</summary>
    private static void ApplyTimestamp(string path, int which, DateTime value, bool recursive)
    {
        switch (which)
        {
            case 0: File.SetLastWriteTime(path, value); break;
            case 1: File.SetCreationTime(path, value); break;
            case 2: File.SetLastAccessTime(path, value); break;
        }

        if (recursive && Directory.Exists(path))
        {
            // ReparsePointGuard.SkipRecursion: without it, applying a timestamp recursively
            // rewrote the last-write/creation/access time of files reachable only through a
            // junction inside the selected folder - confirmed with a real junction before this fix.
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = ReparsePointGuard.SkipRecursion
            };
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos("*", opts))
            {
                try
                {
                    switch (which)
                    {
                        case 0: File.SetLastWriteTime(entry.FullName, value); break;
                        case 1: File.SetCreationTime(entry.FullName, value); break;
                        case 2: File.SetLastAccessTime(entry.FullName, value); break;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warning($"Recursive timestamp failed for {entry.FullName}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>Resets all attribute checkboxes and timestamp pickers to their original values.</summary>
    private void ResetToOriginal()
    {
        for (int i = 0; i < 4; i++)
        {
            var flag = EditableFlags[i];
            int all = 0, none = 0;
            foreach (var a in _originalAttributes)
            {
                if ((a & flag) != 0) all++; else none++;
            }
            ThemedCheckBox.CheckState state;
            if (all == _items.Count) state = ThemedCheckBox.CheckState.Checked;
            else if (none == _items.Count) state = ThemedCheckBox.CheckState.Unchecked;
            else state = ThemedCheckBox.CheckState.Indeterminate;

            _attrCheckboxes[i].State = state;
            _attrStatusLabels[i].Text = StatusTextForState(state);
        }

        if (_recursiveCheckbox != null) _recursiveCheckbox.Checked = false;

        if (_isSingle)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_timeCheckboxes[i] is { } tcb) tcb.Checked = false;
                if (_timePickers[i] is { } dtp)
                {
                    dtp.Enabled = false;
                    dtp.Value = ClampDateTime(i switch
                    {
                        0 => _origModified,
                        1 => _origCreated,
                        _ => _origAccessed
                    });
                }
            }
        }

        SetStatus(LocalizationService.Current.GetString("Props.Reseted"));
    }

    // ── Height tuning ───────────────────────────────────────────────────

    /// <summary>Calculates the optimal <see cref="Form.ClientSize"/> based on visible sections.</summary>
    private void ComputeClientHeight()
    {
        int h = 18 + 60;        // padding + header
        h += 10;

        // Info section
        if (_isSingle)
        {
            int rows = 8;
            if (_isDirectory) rows += 3;
            h += rows * 26 + 10;
        }
        else
        {
            h += 4 * 26 + 10;
        }

        // Attributes section: header (24) + hint (18) + 4 rows (30) + panel padding
        h += 24 + 18 + 4 * 30 + 12 + 8;

        // Recursive checkbox (single directory only)
        if (_isSingle && _isDirectory)
            h += 28 + 8;

        // Timestamp section (single only)
        if (_isSingle)
            h += 24 + 18 + 3 * 32 + 12 + 8;

        h += 12;            // bottom padding
        h += 54;            // bottom button bar

        ClientSize = new Size(520, Math.Min(h, 1000));
        if (h > 1000)
            ClientSize = new Size(520, 1000);
    }

    // ── Disposal ───────────────────────────────────────────────────────

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _cts?.Cancel(); _cts?.Dispose(); } catch { /* best effort cleanup - form is closing regardless */ }
        _cts = null;
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _recursiveCheckbox?.Dispose();
            _statusLabel?.Dispose();
        }
        base.Dispose(disposing);
    }
}