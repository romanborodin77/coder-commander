using CoderCommander.Models;
using CoderCommander.Services;
using System.Drawing.Drawing2D;

namespace CoderCommander.WinForms;

/// <summary>
/// Modern copy/move confirmation dialog with file list preview, destination picker,
/// overwrite policy, and attribute options.
/// </summary>
public sealed partial class CopyMoveDialogForm : ThemedForm
{
    /// <summary>Selected destination path.</summary>
    public string DestinationPath => _destBox.Text.Trim();

    /// <summary>Selected overwrite policy (maps to OverwriteAction enum).</summary>
    public int OverwritePolicyIndex => _overwriteCombo.SelectedIndex;

    /// <summary>Whether to copy file attributes (read-only, hidden, etc.).</summary>
    public bool CopyAttributes => _copyAttrsCheck.Checked;

    /// <summary>Whether to preserve original timestamps.</summary>
    public bool CopyTimestamps => _copyTsCheck.Checked;

    /// <summary>When true, the operation is added to the queue held (<see cref="OperationState.NotStarted"/>)
    /// rather than started immediately - the user starts it later from <c>OperationQueueForm</c>,
    /// letting several transfers be gathered first.</summary>
    public bool AddToQueue => _queueCheck.Checked;

    /// <param name="items">Files to copy/move.</param>
    /// <param name="defaultDest">Default destination path.</param>
    /// <param name="isMove">True for Move, false for Copy.</param>
    public CopyMoveDialogForm(IReadOnlyList<FileSystemItem> items, string defaultDest, bool isMove)
    {
        ArgumentNullException.ThrowIfNull(items);

        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        // Copy and Move share this dialog, so both the title and the header caption pick one of two
        // keys rather than carrying a single fixed LocalizationKey.
        Text = isMove ? L.GetString("CopyMove.Title.Move") : L.GetString("CopyMove.Title.Copy");
        _headerLabel.Text = Text;

        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colName.Text = L.GetString("CopyMove.Col.Name");
        _colSize.Text = L.GetString("CopyMove.Col.Size");
        _colType.Text = L.GetString("CopyMove.Col.Type");

        _iconBox.Paint += (_, e) => DrawTransferIcon(e.Graphics, isMove, ThemeService.Current.Accent);

        _destBox.Text = defaultDest;
        _browseBtn.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _destBox.Text };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _destBox.Text = dlg.SelectedPath;
        };

        PopulateFileList(items, p, L);

        _overwriteCombo.AddItems(
            L.GetString("Overwrite.Ask"),
            L.GetString("Overwrite.Overwrite"),
            L.GetString("Overwrite.Skip"),
            L.GetString("Overwrite.OverwriteOlder"),
            L.GetString("Overwrite.OverwriteAll"),
            L.GetString("Overwrite.SkipAll"),
            L.GetString("Overwrite.Rename"));
        _overwriteCombo.SelectedIndex = 0;

        var s = SettingsService.Load();
        _copyAttrsCheck.Checked = s.CopyAttributes;
        _copyTsCheck.Checked = s.CopyTimestamps;
    }

    /// <summary>Fills the preview list, capped at 50 rows with a "+N more" trailer - a selection of
    /// thousands of files must not turn this dialog into a scrolling wall.</summary>
    private void PopulateFileList(IReadOnlyList<FileSystemItem> items, ThemePalette p, LocalizationService L)
    {
        var totalSize = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
        var displayCount = Math.Min(items.Count, 50);

        for (var i = 0; i < displayCount; i++)
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

        _fileCountLabel.Text = L.GetString("CopyMove.Files", items.Count);
        _totalSizeLabel.Text = L.GetString("CopyMove.TotalSize", UiHelpers.FormatSize(totalSize));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Prevent OK from closing with an empty destination — the caller silently returns
        // without feedback, leaving the user wondering why nothing happened.
        if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(_destBox.Text))
        {
            e.Cancel = true;
            _destBox.Focus();
        }
        base.OnFormClosing(e);
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
