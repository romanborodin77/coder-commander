using CoderCommander.Models;
using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Modern copy/move confirmation dialog with file list preview, destination picker,
/// overwrite policy, and attribute options.
/// </summary>
public class CopyMoveDialogForm : ThemedForm
{
    private readonly TextBox _destBox;
    private readonly ThemedComboBox _overwriteCombo;
    private readonly ThemedCheckBox _copyAttrsCheck;
    private readonly ThemedCheckBox _copyTsCheck;
    private readonly Button _okBtn;
    private readonly Button _cancelBtn;
    private readonly Label _fileCountLabel;
    private readonly Label _totalSizeLabel;
    private readonly ListView _fileList;

    /// <summary>Selected destination path.</summary>
    public string DestinationPath => _destBox.Text;

    /// <summary>Selected overwrite policy (maps to OverwriteAction enum).</summary>
    public int OverwritePolicyIndex => _overwriteCombo.SelectedIndex;

    /// <summary>Whether to copy file attributes (read-only, hidden, etc.).</summary>
    public bool CopyAttributes => _copyAttrsCheck.Checked;

    /// <summary>Whether to preserve original timestamps.</summary>
    public bool CopyTimestamps => _copyTsCheck.Checked;

    /// <param name="items">Files to copy/move.</param>
    /// <param name="defaultDest">Default destination path.</param>
    /// <param name="isMove">True for Move, false for Copy.</param>
    public CopyMoveDialogForm(IReadOnlyList<FileSystemItem> items, string defaultDest, bool isMove)
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        Text = isMove ? L.GetString("CopyMove.Title.Move") : L.GetString("CopyMove.Title.Copy");
        ClientSize = new Size(560, 460);
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = p.Background;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            BackColor = p.Background
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        // ─ Header ──
        var headerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.HeaderBackground,
            Tag = ThemeRole.HeaderBackground,
            Padding = new Padding(20, 12, 20, 12)
        };

        var iconBox = new PictureBox
        {
            Size = new Size(32, 32),
            Location = new Point(20, 12),
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.Transparent
        };
        iconBox.Paint += (_, e) => DrawTransferIcon(e.Graphics, isMove, p.Accent);

        var headerLabel = new Label
        {
            Text = isMove ? L.GetString("CopyMove.Title.Move") : L.GetString("CopyMove.Title.Copy"),
            Font = p.SubtitleFont,
            ForeColor = p.Foreground,
            Location = new Point(64, 12),
            AutoSize = true,
            Tag = ThemeRole.Subtitle
        };

        headerPanel.Controls.Add(iconBox);
        headerPanel.Controls.Add(headerLabel);
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // ── File list ─
        var fileListPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = p.PanelBackground,
            Tag = ThemeRole.PanelBackground,
            Padding = new Padding(20, 8, 20, 8)
        };

        _fileList = new ListView
        {
            View = View.Details,
            FullRowSelect = false,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.None,
            BorderStyle = BorderStyle.None,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            Font = p.GridFont,
            Dock = DockStyle.Fill,
            MultiSelect = false,
            Scrollable = true
        };
        _fileList.Columns.Add(L.GetString("CopyMove.Col.Name"), 320, HorizontalAlignment.Left);
        _fileList.Columns.Add(L.GetString("CopyMove.Col.Size"), 100, HorizontalAlignment.Right);
        _fileList.Columns.Add(L.GetString("CopyMove.Col.Type"), 80, HorizontalAlignment.Left);

        var totalSize = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
        var displayCount = Math.Min(items.Count, 50);

        for (int i = 0; i < displayCount; i++)
        {
            var item = items[i];
            var lvi = new ListViewItem(item.Name)
            {
                ForeColor = item.IsDirectory ? p.DirectoryColor : p.Foreground
            };
            lvi.SubItems.Add(item.IsDirectory ? "" : UiHelpers.FormatSize(item.Size));
            lvi.SubItems.Add(item.IsDirectory ? L.GetString("Common.Folder") : item.Extension.ToUpperInvariant().TrimStart('.'));
            _fileList.Items.Add(lvi);
        }

        if (items.Count > displayCount)
        {
            _fileList.Items.Add(new ListViewItem(L.GetString("CopyMove.MoreFiles", items.Count - displayCount))
            {
                ForeColor = p.DimForeground
            });
        }

        fileListPanel.Controls.Add(_fileList);
        mainLayout.Controls.Add(fileListPanel, 0, 1);

        // ── Options ──
        var optionsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(20, 12, 20, 12),
            BackColor = p.Background
        };
        optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        optionsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++)
            optionsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        optionsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        int row = 0;

        // Destination
        var destLabel = UiHelpers.CreateLabel(L.GetString("CopyMove.Destination"), bold: true);
        destLabel.Dock = DockStyle.Fill;
        destLabel.TextAlign = ContentAlignment.MiddleLeft;
        optionsPanel.Controls.Add(destLabel, 0, row);

        // Margin = 0: same TableLayoutPanel-cell trap as CreateBottomPanel (see ThemedForm.cs) -
        // this row is RowStyle(Absolute, 32), and the default 3px Control.Margin would shrink
        // the cell's usable height to 26px, 6px short of browseBtn's 32px CreateThemedButton
        // height (confirmed via check_layout()'s inconsistent_button_size finding + the exact
        // Bounds numbers from the internal dump before attributing the cause).
        var destPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _destBox = UiHelpers.CreateTextBox(defaultDest);
        _destBox.Dock = DockStyle.Fill;
        var browseBtn = ThemedForm.CreateThemedButton("...");
        browseBtn.Dock = DockStyle.Right;
        browseBtn.Width = 40;
        browseBtn.Margin = new Padding(4, 0, 0, 0);
        browseBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _destBox.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _destBox.Text = dlg.SelectedPath;
        };
        // Dock=Fill must be added before any Dock=Top/Bottom/Left/Right sibling - WinForms
        // lays out docked children from the last-added index down to the first, so adding
        // browseBtn (Dock=Right) before _destBox (Dock=Fill) let Fill claim the whole panel
        // first and be laid out last (painted last / on top potentially, and can affect the
        // Right-docked sibling's measured size) - see CLAUDE.md's "Docking order pitfall".
        destPanel.Controls.Add(_destBox);
        destPanel.Controls.Add(browseBtn);
        optionsPanel.Controls.Add(destPanel, 1, row);
        row++;

        // File count + size
        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = p.Background
        };
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        infoPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _fileCountLabel = UiHelpers.CreateLabel(L.GetString("CopyMove.Files", items.Count));
        _fileCountLabel.Dock = DockStyle.Fill;
        _fileCountLabel.TextAlign = ContentAlignment.MiddleLeft;
        _totalSizeLabel = UiHelpers.CreateLabel(L.GetString("CopyMove.TotalSize", UiHelpers.FormatSize(totalSize)));
        _totalSizeLabel.Dock = DockStyle.Fill;
        _totalSizeLabel.TextAlign = ContentAlignment.MiddleLeft;
        infoPanel.Controls.Add(_fileCountLabel, 0, 0);
        infoPanel.Controls.Add(_totalSizeLabel, 1, 0);
        optionsPanel.Controls.Add(infoPanel, 1, row);
        row++;

        // Overwrite combo
        var owLabel = UiHelpers.CreateLabel(L.GetString("CopyMove.OverwritePolicy"), bold: true);
        owLabel.Dock = DockStyle.Fill;
        owLabel.TextAlign = ContentAlignment.MiddleLeft;
        optionsPanel.Controls.Add(owLabel, 0, row);

        _overwriteCombo = new ThemedComboBox { Dock = DockStyle.Fill };
        _overwriteCombo.AddItems(
            L.GetString("Overwrite.Ask"),
            L.GetString("Overwrite.Overwrite"),
            L.GetString("Overwrite.Skip"),
            L.GetString("Overwrite.OverwriteOlder"),
            L.GetString("Overwrite.OverwriteAll"),
            L.GetString("Overwrite.SkipAll"),
            L.GetString("Overwrite.Rename"));
        _overwriteCombo.SelectedIndex = 0;
        optionsPanel.Controls.Add(_overwriteCombo, 1, row);
        row++;

        // Checkboxes
        var checksFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = p.Background,
            Padding = new Padding(0, 4, 0, 0)
        };

        var s = SettingsService.Load();
        _copyAttrsCheck = UiHelpers.CreateCheckBox(L.GetString("CopyMove.CopyAttributes"), s.CopyAttributes);
        _copyAttrsCheck.AutoSize = true;
        checksFlow.Controls.Add(_copyAttrsCheck);

        _copyTsCheck = UiHelpers.CreateCheckBox(L.GetString("CopyMove.CopyTimestamps"), s.CopyTimestamps);
        _copyTsCheck.AutoSize = true;
        checksFlow.Controls.Add(_copyTsCheck);

        optionsPanel.Controls.Add(checksFlow, 1, row);

        mainLayout.Controls.Add(optionsPanel, 0, 2);

        // ── Buttons ──
        // CreateBottomPanel lays the buttons out in a right-aligned FlowLayoutPanel instead of
        // computing pixel Locations from btnPanel.Width here in the constructor, before the
        // panel has actually been laid out (that Width was always the design-time default, not
        // the real one - correctness depended entirely on the Resize handler firing before the
        // first paint).
        _okBtn = ThemedForm.CreateThemedButton(L.GetString("Common.OK"), accent: true);
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.Size = new Size(100, 32);

        _cancelBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Cancel"), accent: false);
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Size = new Size(100, 32);

        var btnPanel = CreateBottomPanel(_okBtn, _cancelBtn);
        mainLayout.Controls.Add(btnPanel, 0, 3);

        Controls.Add(mainLayout);

        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;
    }

    private static void DrawTransferIcon(Graphics g, bool isMove, Color accent)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(accent, 2f);
        using var brush = new SolidBrush(accent);

        if (isMove)
        {
            g.DrawLine(pen, 4, 16, 24, 16);
            g.FillPolygon(brush, new[] { new Point(20, 10), new Point(28, 16), new Point(20, 22) });
        }
        else
        {
            g.DrawRectangle(pen, 4, 4, 16, 16);
            g.DrawRectangle(pen, 10, 10, 16, 16);
        }
    }
}
