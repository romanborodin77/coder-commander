using CoderCommander.Models;
using CoderCommander.Services;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CoderCommander.WinForms;

/// <summary>
/// Multi-rename dialog: batch-rename selected files using a pattern with placeholders.
/// Supported placeholders: [N] name, [E] extension, [N1-5] first N chars of name,
/// [N-5] last N chars, [C] counter, [C10] counter starting at 10, [C2:10] counter step 2 start 10,
/// [D] date (yyyy-MM-dd), [T] time (HHmmss), [P] parent directory name.
/// </summary>
public class MultiRenameForm : ThemedForm
{
    private readonly IReadOnlyList<FileSystemItem> _items;
    private readonly string _sourcePath;

    private TextBox _patternBox = null!;
    private TextBox _extBox = null!;
    private NumericUpDown _startIndex = null!;
    private NumericUpDown _stepIndex = null!;
    private ListView _previewList = null!;
    private Button _okBtn = null!;
    private Button _cancelBtn = null!;
    private Button _resetBtn = null!;
    private Label _hintLabel = null!;

    /// <summary>Results: pairs of (oldFullPath, newFullPath).</summary>
    public List<(string oldPath, string newPath)> Results { get; } = [];

    /// <param name="items">Files to rename.</param>
    /// <param name="sourcePath">Working directory containing the files.</param>
    public MultiRenameForm(IReadOnlyList<FileSystemItem> items, string sourcePath)
    {
        _items = items;
        _sourcePath = sourcePath;
        Resizable = true;

        var L = LocalizationService.Current;
        Text = L.GetString("MultiRename.Title");
        ClientSize = new Size(720, 520);
        MinimumSize = new Size(600, 420);

        BuildControls();
    }

    /// <summary>Builds all UI controls for the rename dialog.</summary>
    private void BuildControls()
    {
        var L = LocalizationService.Current;
        var p = ThemeService.Current;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Padding = new Padding(16, 16, 16, 8),
            BackColor = p.Background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        int row = 0;

        // Pattern
        var lblPattern = UiHelpers.CreateLabel(L.GetString("MultiRename.Pattern"), bold: true);
        lblPattern.Dock = DockStyle.Fill;
        lblPattern.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(lblPattern, 0, row);

        _patternBox = UiHelpers.CreateTextBox("[N]");
        _patternBox.Dock = DockStyle.Fill;
        _patternBox.TextChanged += (_, _) => UpdatePreview();
        layout.Controls.Add(_patternBox, 1, row);
        row++;

        // Extension
        var lblExt = UiHelpers.CreateLabel(L.GetString("MultiRename.Extension"), bold: true);
        lblExt.Dock = DockStyle.Fill;
        lblExt.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(lblExt, 0, row);

        _extBox = UiHelpers.CreateTextBox("[E]");
        _extBox.Dock = DockStyle.Fill;
        _extBox.TextChanged += (_, _) => UpdatePreview();
        layout.Controls.Add(_extBox, 1, row);
        row++;

        // Start index
        var lblStart = UiHelpers.CreateLabel(L.GetString("MultiRename.StartAt"), bold: true);
        lblStart.Dock = DockStyle.Fill;
        lblStart.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(lblStart, 0, row);

        // A FlowLayoutPanel, not a plain Panel - a plain Panel doesn't position undocked
        // children at all, so all three used to land on top of each other at (0,0). Margin
        // (below) only takes effect on children of a layout panel, same rule as
        // StyledMessageBoxForm.BuildButtonBar's button row.
        var counterPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        _startIndex = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 999999,
            Value = 1,
            Width = 80,
            Margin = new Padding(0, 5, 0, 0),
            Font = p.GridFont,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            BorderStyle = BorderStyle.FixedSingle
        };
        _startIndex.ValueChanged += (_, _) => UpdatePreview();

        var lblStep = UiHelpers.CreateLabel(L.GetString("MultiRename.Step"), false);
        lblStep.Margin = new Padding(16, 9, 4, 0);
        lblStep.TextAlign = ContentAlignment.MiddleLeft;

        _stepIndex = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 999,
            Value = 1,
            Width = 60,
            Margin = new Padding(0, 5, 0, 0),
            Font = p.GridFont,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            BorderStyle = BorderStyle.FixedSingle
        };
        _stepIndex.ValueChanged += (_, _) => UpdatePreview();

        // Add order is the visual left-to-right order in a FlowLayoutPanel (unlike Dock, which
        // goes from the highest Controls index down).
        counterPanel.Controls.Add(_startIndex);
        counterPanel.Controls.Add(lblStep);
        counterPanel.Controls.Add(_stepIndex);
        layout.Controls.Add(counterPanel, 1, row);
        row++;

        // Hint
        _hintLabel = UiHelpers.CreateLabel(L.GetString("MultiRename.Hint"), false);
        _hintLabel.Dock = DockStyle.Fill;
        _hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        _hintLabel.ForeColor = p.DimForeground;
        layout.Controls.Add(_hintLabel, 0, row);
        layout.SetColumnSpan(_hintLabel, 2);
        row++;

        // Spacer
        var spacer = new Panel { Dock = DockStyle.Fill, BackColor = p.Background };
        layout.Controls.Add(spacer, 0, row);
        layout.SetColumnSpan(spacer, 2);
        row++;

        // Preview list
        _previewList = UiHelpers.CreateListView(
            (L.GetString("MultiRename.OldName"), 260),
            (L.GetString("MultiRename.NewName"), 260),
            (L.GetString("MultiRename.Status"), 80));
        _previewList.Dock = DockStyle.Fill;
        layout.Controls.Add(_previewList, 0, row);
        layout.SetColumnSpan(_previewList, 2);

        // Buttons
        _okBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Rename"), accent: true);
        _okBtn.DialogResult = DialogResult.OK;
        _okBtn.Width = 100;

        _cancelBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Cancel"), accent: false);
        _cancelBtn.DialogResult = DialogResult.Cancel;
        _cancelBtn.Width = 100;

        _resetBtn = ThemedForm.CreateThemedButton(L.GetString("Common.Reset"), accent: false);
        _resetBtn.Width = 100;
        _resetBtn.Click += (_, _) =>
        {
            _patternBox.Text = "[N]";
            _extBox.Text = "[E]";
            _startIndex.Value = 1;
            _stepIndex.Value = 1;
        };

        var bottomPanel = CreateBottomPanel(_okBtn, _cancelBtn, _resetBtn);

        // Dock=Fill must be added before Dock=Bottom/Top/Left/Right siblings (see
        // WinForms/DirectoryTreeForm.cs for the full explanation).
        Controls.Add(layout);
        Controls.Add(bottomPanel);

        AcceptButton = _okBtn;
        CancelButton = _cancelBtn;

        UpdatePreview();
    }

    /// <summary>Refreshes the preview list based on the current pattern and settings.</summary>
    private void UpdatePreview()
    {
        if (!IsHandleCreated) return;

        _previewList.BeginUpdate();
        _previewList.Items.Clear();

        var pattern = _patternBox.Text;
        var extPattern = _extBox.Text;
        var startValue = (int)_startIndex.Value;
        var step = (int)_stepIndex.Value;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var (newName, newExt) = ApplyPattern(pattern, extPattern, item, i, startValue, step);
            var fullNewName = newExt.Length > 0 && !item.IsDirectory
                ? $"{newName}.{newExt}"
                : newName;

            var status = fullNewName == item.Name
                ? "="
                : IsValidFileName(fullNewName) ? "->" : "!";

            var lvi = new ListViewItem(item.Name) { UseItemStyleForSubItems = false };
            lvi.SubItems.Add(fullNewName);
            lvi.SubItems.Add(status);

            if (status == "!")
                lvi.ForeColor = ThemeService.Current.Danger;
            else if (status == "->")
                lvi.ForeColor = ThemeService.Current.Accent;

            _previewList.Items.Add(lvi);
        }

        _previewList.EndUpdate();
    }

    /// <summary>Applies the name and extension patterns to a single item, returning the new name components.</summary>
    private (string name, string ext) ApplyPattern(
        string pattern, string extPattern, FileSystemItem item,
        int index, int startValue, int step)
    {
        var baseName = item.IsDirectory ? item.Name : item.NameWithoutExtension;
        var baseExt = item.IsDirectory ? "" : item.Extension.TrimStart('.');

        var name = ReplacePlaceholders(pattern, baseName, baseExt, item, index, startValue, step);
        var ext = ReplacePlaceholders(extPattern, baseName, baseExt, item, index, startValue, step);

        return (name, ext);
    }

    /// <summary>Replaces all recognized placeholders in a pattern string with their computed values.</summary>
    private static string ReplacePlaceholders(
        string pattern, string name, string ext, FileSystemItem item,
        int index, int startValue, int step)
    {
        if (string.IsNullOrEmpty(pattern)) return "";

        var result = Regex.Replace(pattern, @"\[([NEPCDT])((-?\d+)(?::(-?\d+))?)?\]", m =>
        {
            var tag = m.Groups[1].Value[0];
            var num1Str = m.Groups[3].Success ? m.Groups[3].Value : null;
            var num2Str = m.Groups[4].Success ? m.Groups[4].Value : null;

            return tag switch
            {
                'N' => num1Str != null
                    ? SubstringSafe(name, ParseIntSafe(num1Str))
                    : name,
                'E' => ext,
                'P' => Path.GetFileName(Path.GetDirectoryName(item.FullPath)) ?? "",
                'C' => ComputeCounter(num1Str, num2Str, startValue, step, index),
                'D' => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                'T' => DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture),
                _ => m.Value
            };
        });

        return result;
    }

    /// <summary>Returns a substring of <paramref name="s"/> by count, safely handling short strings.
    /// Positive count takes from the start; negative count takes from the end.</summary>
    private static string SubstringSafe(string s, int count)
    {
        if (count >= 0)
            return s.Length > count ? s[..count] : s;
        var abs = -count;
        return s.Length > abs ? s[^abs..] : s;
    }

    /// <summary>Parses a placeholder's digit-run capture (e.g. from <c>[N12]</c>/<c>[C2:10]</c>)
    /// into an int, falling back to 0 instead of throwing. The capturing regex group allows a
    /// digit run of unbounded length, so a value typed directly into the pattern textbox (e.g.
    /// <c>[C99999999999]</c>) can exceed <see cref="int.MaxValue"/> - previously this reached a bare
    /// <see cref="int.Parse(string)"/> with no try/catch anywhere on the path from
    /// <c>TextChanged</c>, crashing the app on an ordinary typo.</summary>
    private static int ParseIntSafe(string s) => int.TryParse(s, out var n) ? n : 0;

    /// <summary>Computes the counter value for the given index, with optional width and start parameters.</summary>
    private static string ComputeCounter(string? num1, string? num2, int startValue, int step, int index)
    {
        int width = 0;
        int start = startValue;

        if (num1 != null && num2 != null)
        {
            width = ParseIntSafe(num1);
            start = ParseIntSafe(num2);
        }
        else if (num1 != null)
        {
            start = ParseIntSafe(num1);
        }

        var value = start + index * step;

        return width > 0
            ? value.ToString($"D{width}", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Returns <c>true</c> if the name contains no invalid filename characters and
    /// isn't the reserved "." or ".." (which <see cref="Path.GetInvalidFileNameChars"/> alone
    /// doesn't reject) - a pattern that evaluates to exactly ".." would otherwise resolve to the
    /// parent directory via <c>Path.Combine(dir, "..")</c>.</summary>
    private static bool IsValidFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name is "." or "..") return false;
        var invalid = Path.GetInvalidFileNameChars();
        return !name.Any(c => invalid.Contains(c));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK)
        {
            Results.Clear();

            var pattern = _patternBox.Text;
            var extPattern = _extBox.Text;
            var startValue = (int)_startIndex.Value;
            var step = (int)_stepIndex.Value;

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var (newName, newExt) = ApplyPattern(pattern, extPattern, item, i, startValue, step);
                var fullNewName = newExt.Length > 0 && !item.IsDirectory
                    ? $"{newName}.{newExt}"
                    : newName;

                if (fullNewName == item.Name || !IsValidFileName(fullNewName))
                    continue;

                var dir = Path.GetDirectoryName(item.FullPath) ?? "";
                var newPath = Path.Combine(dir, fullNewName);
                Results.Add((item.FullPath, newPath));
            }

            if (Results.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            // Check for conflicts
            var duplicates = Results
                .GroupBy(r => r.newPath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Count > 0)
            {
                var L = LocalizationService.Current;
                StyledMessageBox.Show(
                    L.GetString("MultiRename.ErrDuplicate"),
                    L.GetString("MultiRename.Title"),
                    MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
                e.Cancel = true;
                return;
            }
        }

        base.OnFormClosing(e);
    }
}
