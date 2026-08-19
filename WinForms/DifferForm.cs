using CoderCommander.FileSystem;
using CoderCommander.Services;
using CoderCommander.Utils;
using System.Globalization;

namespace CoderCommander.WinForms;

/// <summary>
/// Shows two files side-by-side with line-by-line highlighting of differences.
/// </summary>
public sealed class DifferForm : ThemedForm
{
    /// <summary>Above this (per file), reading the whole file into memory to diff it line-by-line
    /// is large enough to freeze the UI thread for seconds or throw
    /// <see cref="OutOfMemoryException"/> comparing two multi-GB files. Same threshold
    /// <see cref="ViewerForm"/> uses for its own text mode.</summary>
    private const long LargeFileConfirmBytes = 16 * 1024 * 1024;

    private readonly TextBox _leftBox;
    private readonly TextBox _rightBox;
    private readonly Label _statusLabel;
    private readonly Button _closeBtn;
    private readonly Button _leftBrowseBtn;
    private readonly Button _rightBrowseBtn;
    private readonly TextBox _leftPathBox;
    private readonly TextBox _rightPathBox;

    // Each side starts on the file system the panel selection came from (so a file inside an
    // archive or on an SFTP/FTP/WebDAV connection can actually be diffed), but Browse... only
    // knows how to pick a real local file - picking one switches that side to LocalFileSystem
    // independently of the other side, which is why these are two separate mutable fields rather
    // than one shared "current file system" for the whole dialog.
    private IFileSystem _leftFs;
    private IFileSystem _rightFs;

    /// <summary>
    /// Initializes a new instance of the <see cref="DifferForm"/> class, optionally pre-filling both file paths.
    /// </summary>
    /// <param name="leftPath">Path to the left-side file, or <c>null</c> for an empty field.</param>
    /// <param name="rightPath">Path to the right-side file, or <c>null</c> for an empty field.</param>
    /// <param name="fileSystem">File system both initial paths live on - normally the active
    /// panel's <c>CurrentFileSystem</c>, since both selected files come from the same panel.</param>
    public DifferForm(string? leftPath, string? rightPath, IFileSystem fileSystem)
    {
        _leftFs = fileSystem;
        _rightFs = fileSystem;
        var L = LocalizationService.Current;
        Text = L.GetString("Differ.Title");
        ClientSize = new Size(900, 616); // +16 to match the top bar's 72→88 growth below
        Resizable = true;
        MinimumSize = new Size(560, 400);

        var p = ThemeService.Current;

        // Top bar: two path inputs
        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 2,
            // 2 rows * 32 + Padding(8+8) = 72 exactly - zero slack for any font/DPI growth.
            Height = 88,
            BackColor = p.Background,
            Padding = new Padding(16, 8, 16, 8)
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        // Wide enough for the localized "Browse…" text - CreateThemedButton's own auto-sizing
        // would give it ~100px, and this column used to be 60 (RoundedButton's EndEllipsis then
        // silently truncated it to "Bro...").
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        topBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        topBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        topBar.Controls.Add(UiHelpers.CreateLabel(L.GetString("Differ.Left")), 0, 0);
        _leftPathBox = UiHelpers.CreateTextBox(leftPath ?? "");
        _leftPathBox.Dock = DockStyle.Fill;
        topBar.Controls.Add(_leftPathBox, 1, 0);
        _leftBrowseBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Browse"));
        _leftBrowseBtn.Dock = DockStyle.Fill;
        _leftBrowseBtn.Click += (_, _) => Browse(_leftPathBox, fs => _leftFs = fs);
        topBar.Controls.Add(_leftBrowseBtn, 2, 0);

        topBar.Controls.Add(UiHelpers.CreateLabel(L.GetString("Differ.Right")), 0, 1);
        _rightPathBox = UiHelpers.CreateTextBox(rightPath ?? "");
        _rightPathBox.Dock = DockStyle.Fill;
        topBar.Controls.Add(_rightPathBox, 1, 1);
        _rightBrowseBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Browse"));
        _rightBrowseBtn.Dock = DockStyle.Fill;
        _rightBrowseBtn.Click += (_, _) => Browse(_rightPathBox, fs => _rightFs = fs);
        topBar.Controls.Add(_rightBrowseBtn, 2, 1);

        // Split view
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 4,
            BackColor = p.GridLine,
            BorderStyle = BorderStyle.None
        };

        _leftBox = CreateTextBox();
        _rightBox = CreateTextBox();
        split.Panel1.Controls.Add(_leftBox);
        split.Panel2.Controls.Add(_rightBox);

        // Bottom
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = p.DimForeground,
            Font = p.GridFont,
            TextAlign = ContentAlignment.MiddleLeft,
            Tag = ThemeRole.Muted
        };

        var compareBtn = ThemedForm.CreateThemedButton(L.GetString("Differ.Compare"), accent: true);
        compareBtn.Margin = new Padding(0);
        compareBtn.Click += (_, _) => _ = CompareFilesAsync();

        _closeBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Close"));
        _closeBtn.Margin = new Padding(0, 0, 8, 0);
        _closeBtn.Click += (_, _) => Close();

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(16, 8, 16, 8)
        };
        // Right-aligned FlowLayoutPanel instead of Dock.Right + Margin (which Dock.Right
        // ignores) so the gap between the two buttons actually renders.
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
        rightGroup.Controls.Add(compareBtn);
        bottomPanel.Controls.Add(_statusLabel);
        bottomPanel.Controls.Add(rightGroup);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(split);
        Controls.Add(bottomPanel);
        Controls.Add(topBar);

        // Must be set only after `split` is parented and docked - SplitContainer.Width is still
        // its unparented default (150px) at construction time, so setting SplitterDistance against
        // ClientSize.Width earlier either clamps to that tiny default or gets silently orphaned
        // once the container grows to fill the real client area, leaving the splitter stuck near
        // one edge instead of centered (caught by visual inspection of a live build).
        split.SplitterDistance = (ClientSize.Width - 4) / 2;

        CancelButton = _closeBtn;

        if (!string.IsNullOrEmpty(leftPath) && !string.IsNullOrEmpty(rightPath))
        {
            _leftPathBox.Text = leftPath;
            _rightPathBox.Text = rightPath;
        }
    }

    private static TextBox CreateTextBox()
    {
        var p = ThemeService.Current;
        return new TextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Multiline = true,
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            Font = p.MonoFont,
            BorderStyle = BorderStyle.None
        };
    }

    /// <summary>A native folder/file picker only ever browses the real local disk - picking a
    /// file this way always switches that side to <see cref="LocalFileSystem"/>, independently of
    /// whatever the side started on (a panel's archive or connection).</summary>
    private static void Browse(TextBox box, Action<IFileSystem> setFs)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = LocalizationService.Current.GetString("Differ.FilterAll")
        };
        if (dlg.ShowDialog() == DialogResult.OK)
        {
            box.Text = dlg.FileName;
            setFs(new LocalFileSystem());
        }
    }

    private async Task CompareFilesAsync()
    {
        var L = LocalizationService.Current;
        var left = _leftPathBox.Text.Trim();
        var right = _rightPathBox.Text.Trim();
        var leftFs = _leftFs;
        var rightFs = _rightFs;

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right) ||
            !await leftFs.ExistsAsync(left).ConfigureAwait(true) ||
            !await rightFs.ExistsAsync(right).ConfigureAwait(true))
        {
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = L.GetString("Differ.FilesNotFound");
            return;
        }
        if (IsDisposed || !IsHandleCreated) return;

        var leftInfo = await leftFs.GetFileInfoAsync(left).ConfigureAwait(true);
        var rightInfo = await rightFs.GetFileInfoAsync(right).ConfigureAwait(true);
        if (IsDisposed || !IsHandleCreated) return;

        var largestSize = Math.Max(leftInfo?.Size ?? 0, rightInfo?.Size ?? 0);
        if (largestSize > LargeFileConfirmBytes)
        {
            var confirmed = StyledMessageBox.Show(
                L.GetString("Differ.ConfirmLargeFile", FormatUtils.FormatSize(largestSize), FormatUtils.FormatSize(LargeFileConfirmBytes)),
                L.GetString("Common.Confirm"), MsgBoxButtons.YesNo, MsgBoxIcon.Warning, this) == MsgBoxResult.Yes;
            if (!confirmed) return;
        }

        try
        {
            var leftLines = await ReadAllLinesAsync(leftFs, left).ConfigureAwait(true);
            var rightLines = await ReadAllLinesAsync(rightFs, right).ConfigureAwait(true);
            if (IsDisposed || !IsHandleCreated) return;
            var maxLines = Math.Max(leftLines.Count, rightLines.Count);

            int diffCount = 0;
            var sbLeft = new System.Text.StringBuilder();
            var sbRight = new System.Text.StringBuilder();

            for (int i = 0; i < maxLines; i++)
            {
                var l = i < leftLines.Count ? leftLines[i] : "";
                var r = i < rightLines.Count ? rightLines[i] : "";
                var lineNum = (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(5);

                if (string.Equals(l, r, StringComparison.Ordinal))
                {
                    sbLeft.AppendLine(CultureInfo.InvariantCulture, $" {lineNum}: {l}");
                    sbRight.AppendLine(CultureInfo.InvariantCulture, $" {lineNum}: {r}");
                }
                else
                {
                    sbLeft.AppendLine(CultureInfo.InvariantCulture, $">{lineNum}: {l}");
                    sbRight.AppendLine(CultureInfo.InvariantCulture, $">{lineNum}: {r}");
                    diffCount++;
                }
            }

            _leftBox.Text = sbLeft.ToString();
            _rightBox.Text = sbRight.ToString();
            _leftBox.SelectionStart = 0;
            _rightBox.SelectionStart = 0;

            _statusLabel.Text = L.GetString("Differ.Summary", leftLines.Count, rightLines.Count, diffCount);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && IsHandleCreated)
                _statusLabel.Text = ex.Message;
            LogService.Error("Differ compare failed", ex);
        }
    }

    /// <summary>Reads every line of <paramref name="path"/> through <paramref name="fs"/> - the
    /// VFS equivalent of <c>File.ReadAllLines</c>, working for a file inside an archive or on a
    /// remote connection the same way it does for a local one. Bounded by the same
    /// <see cref="LargeFileConfirmBytes"/> confirmation the caller already gates on, so this never
    /// buffers more than what the user explicitly agreed to load.</summary>
    private static async Task<List<string>> ReadAllLinesAsync(IFileSystem fs, string path)
    {
        var lines = new List<string>();
        using var stream = await fs.OpenReadAsync(path).ConfigureAwait(true);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync().ConfigureAwait(true) is { } line)
            lines.Add(line);
        return lines;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _leftPathBox?.Dispose();
            _rightPathBox?.Dispose();
            _leftBrowseBtn?.Dispose();
            _rightBrowseBtn?.Dispose();
            _leftBox?.Dispose();
            _rightBox?.Dispose();
            _statusLabel?.Dispose();
            _closeBtn?.Dispose();
        }
        base.Dispose(disposing);
    }
}
