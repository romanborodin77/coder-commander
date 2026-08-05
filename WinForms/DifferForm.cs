using CoderCommander.Services;

namespace CoderCommander.WinForms;

/// <summary>
/// Shows two files side-by-side with line-by-line highlighting of differences.
/// </summary>
public class DifferForm : ThemedForm
{
    private readonly TextBox _leftBox;
    private readonly TextBox _rightBox;
    private readonly Label _statusLabel;
    private readonly Button _closeBtn;
    private readonly Button _leftBrowseBtn;
    private readonly Button _rightBrowseBtn;
    private readonly TextBox _leftPathBox;
    private readonly TextBox _rightPathBox;

    /// <summary>
    /// Initializes a new instance of the <see cref="DifferForm"/> class, optionally pre-filling both file paths.
    /// </summary>
    /// <param name="leftPath">Path to the left-side file, or <c>null</c> for an empty field.</param>
    /// <param name="rightPath">Path to the right-side file, or <c>null</c> for an empty field.</param>
    public DifferForm(string? leftPath = null, string? rightPath = null)
    {
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
        _leftBrowseBtn.Click += (_, _) => Browse(_leftPathBox);
        topBar.Controls.Add(_leftBrowseBtn, 2, 0);

        topBar.Controls.Add(UiHelpers.CreateLabel(L.GetString("Differ.Right")), 0, 1);
        _rightPathBox = UiHelpers.CreateTextBox(rightPath ?? "");
        _rightPathBox.Dock = DockStyle.Fill;
        topBar.Controls.Add(_rightPathBox, 1, 1);
        _rightBrowseBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Browse"));
        _rightBrowseBtn.Dock = DockStyle.Fill;
        _rightBrowseBtn.Click += (_, _) => Browse(_rightPathBox);
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
        split.SplitterDistance = (ClientSize.Width - 4) / 2;

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
        compareBtn.Click += (_, _) => CompareFiles();

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

    private void Browse(TextBox box)
    {
        using var dlg = new OpenFileDialog
        {
            Filter = LocalizationService.Current.GetString("Differ.FilterAll")
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            box.Text = dlg.FileName;
    }

    private void CompareFiles()
    {
        var L = LocalizationService.Current;
        var left = _leftPathBox.Text.Trim();
        var right = _rightPathBox.Text.Trim();

        if (string.IsNullOrEmpty(left) || !File.Exists(left) ||
            string.IsNullOrEmpty(right) || !File.Exists(right))
        {
            _statusLabel.Text = L.GetString("Differ.FilesNotFound");
            return;
        }

        try
        {
            var leftLines = File.ReadAllLines(left);
            var rightLines = File.ReadAllLines(right);
            var maxLines = Math.Max(leftLines.Length, rightLines.Length);

            int diffCount = 0;
            var sbLeft = new System.Text.StringBuilder();
            var sbRight = new System.Text.StringBuilder();

            for (int i = 0; i < maxLines; i++)
            {
                var l = i < leftLines.Length ? leftLines[i] : "";
                var r = i < rightLines.Length ? rightLines[i] : "";
                var lineNum = (i + 1).ToString().PadLeft(5);

                if (string.Equals(l, r, StringComparison.Ordinal))
                {
                    sbLeft.AppendLine($" {lineNum}: {l}");
                    sbRight.AppendLine($" {lineNum}: {r}");
                }
                else
                {
                    sbLeft.AppendLine($">{lineNum}: {l}");
                    sbRight.AppendLine($">{lineNum}: {r}");
                    diffCount++;
                }
            }

            _leftBox.Text = sbLeft.ToString();
            _rightBox.Text = sbRight.ToString();
            _leftBox.SelectionStart = 0;
            _rightBox.SelectionStart = 0;

            _statusLabel.Text = L.GetString("Differ.Summary", leftLines.Length, rightLines.Length, diffCount);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = ex.Message;
            LogService.Error("Differ compare failed", ex);
        }
    }
}
