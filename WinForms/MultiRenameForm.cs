using CoderCommander.FileSystem;
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
public sealed partial class MultiRenameForm : ThemedForm
{
    private readonly IReadOnlyList<FileSystemItem> _items;
    private readonly string _sourcePath;

    /// <summary>Results: pairs of (oldFullPath, newFullPath).</summary>
    public List<(string oldPath, string newPath)> Results { get; } = [];

    /// <param name="items">Files to rename.</param>
    /// <param name="sourcePath">Working directory containing the files.</param>
    public MultiRenameForm(IReadOnlyList<FileSystemItem> items, string sourcePath)
    {
        InitializeComponent();
        _uiMetadata.ApplyLocalization();

        _items = items;
        _sourcePath = sourcePath;

        var L = LocalizationService.Current;
        // A ColumnHeader is not a Control and cannot carry a LocalizationKey.
        _colOldName.Text = L.GetString("MultiRename.OldName");
        _colNewName.Text = L.GetString("MultiRename.NewName");
        _colStatus.Text = L.GetString("MultiRename.Status");

        // Set here rather than in the designer: ThemedForm.Resizable is this app's own property,
        // applied in OnLoad rather than a real FormBorderStyle the designer could round-trip.
        Resizable = true;

        _patternBox.TextChanged += (_, _) => UpdatePreview();
        _extBox.TextChanged += (_, _) => UpdatePreview();
        _startIndex.ValueChanged += (_, _) => UpdatePreview();
        _stepIndex.ValueChanged += (_, _) => UpdatePreview();
        _findBox.TextChanged += (_, _) => UpdatePreview();
        _replaceBox.TextChanged += (_, _) => UpdatePreview();
        _regexCheck.CheckedChanged += (_, _) => UpdatePreview();

        _resetBtn.Click += (_, _) =>
        {
            _patternBox.Text = "[N]";
            _extBox.Text = "[E]";
            _startIndex.Value = 1;
            _stepIndex.Value = 1;
            _findBox.Text = "";
            _replaceBox.Text = "";
            _regexCheck.Checked = false;
        };

        // UpdatePreview() in the constructor is a no-op (IsHandleCreated == false). Load fires
        // after the handle is created, so the preview fills on first show.
        Load += (_, _) => UpdatePreview();
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
        var now = DateTime.Now;

        const int MaxPreviewItems = 200;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var (newName, newExt) = ApplyPattern(pattern, extPattern, item, i, startValue, step, now);
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
                lvi.ForeColor = DesignerSafeThemeService.Current.Danger;
            else if (status == "->")
                lvi.ForeColor = DesignerSafeThemeService.Current.Accent;

            _previewList.Items.Add(lvi);

            if (i + 1 >= MaxPreviewItems && _items.Count > MaxPreviewItems)
            {
                var moreLvi = new ListViewItem($"… {_items.Count - MaxPreviewItems} more") { ForeColor = DesignerSafeThemeService.Current.DimForeground };
                moreLvi.SubItems.Add("");
                moreLvi.SubItems.Add("");
                _previewList.Items.Add(moreLvi);
                break;
            }
        }

        _previewList.EndUpdate();
    }

    /// <summary>Applies the name and extension patterns to a single item, returning the new name components.</summary>
    private (string name, string ext) ApplyPattern(
        string pattern, string extPattern, FileSystemItem item,
        int index, int startValue, int step, DateTime now)
    {
        var baseName = item.IsDirectory ? item.Name : item.NameWithoutExtension;
        var baseExt = item.IsDirectory ? "" : item.Extension.TrimStart('.');

        var name = ReplacePlaceholders(pattern, baseName, baseExt, item, index, startValue, step, now);
        var ext = ReplacePlaceholders(extPattern, baseName, baseExt, item, index, startValue, step, now);

        // Find/Replace is a second pass over the already-placeholder-resolved name, not a
        // placeholder itself - lets "replace every underscore with a space" work regardless of
        // which placeholders built the name in the first place.
        name = ApplyFindReplace(name, _findBox.Text, _replaceBox.Text, _regexCheck.Checked);

        return (name, ext);
    }

    /// <summary>Applies the Find/Replace pass to <paramref name="name"/>. An empty
    /// <paramref name="find"/> is a no-op. Regex mode uses a bounded <see cref="Regex.Replace(string,string,string,RegexOptions,TimeSpan)"/>
    /// match timeout - same defensive pattern as <see cref="Services.Search.FileMask"/>'s own
    /// wildcard-to-regex compile (audit finding F002), since a pattern typed live into a text box,
    /// character by character, can transiently be catastrophically backtracking mid-edit. An
    /// invalid regex (unbalanced group, bad escape - also typed live, mid-edit) or a timeout both
    /// fall back to the unmodified name rather than crashing the preview or leaving it stuck on a
    /// stale value.</summary>
    private static string ApplyFindReplace(string name, string find, string replace, bool useRegex)
    {
        if (find.Length == 0) return name;

        if (!useRegex)
            return name.Replace(find, replace, StringComparison.Ordinal);

        try
        {
            return Regex.Replace(name, find, replace, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return name;
        }
        catch (RegexMatchTimeoutException)
        {
            return name;
        }
    }

    /// <summary>Replaces all recognized placeholders in a pattern string with their computed values.</summary>
    private static string ReplacePlaceholders(
        string pattern, string name, string ext, FileSystemItem item,
        int index, int startValue, int step, DateTime now)
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
                'P' => VfsPath.GetName(VfsPath.GetParent(item.FullPath) ?? "") ?? "",
                'C' => ComputeCounter(num1Str, num2Str, startValue, step, index),
                'D' => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                'T' => now.ToString("HHmmss", CultureInfo.InvariantCulture),
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

        var value = (long)start + (long)index * step;

        return width > 0
            ? value.ToString($"D{width}", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Returns <c>true</c> if the name contains no invalid filename characters and
    /// isn't the reserved "." or ".." (which <see cref="Path.GetInvalidFileNameChars"/> alone
    /// doesn't reject) - a pattern that evaluates to exactly ".." would otherwise resolve to the
    /// parent directory via <c>Path.Combine(dir, "..")</c>.
    /// <para>
    /// Also runs <see cref="RemotePath.IsSafeEntryName"/> - the same gap this closes for
    /// <see cref="FileSystem.VfsPath.ChangeName"/> exists here too, as an independent call site:
    /// <see cref="Path.GetInvalidFileNameChars"/> alone doesn't reject a reserved DOS device name
    /// (<c>CON</c>/<c>COM1</c>/...), a trailing dot/space that Windows silently strips (which could
    /// collapse two distinct previewed names into one on disk), or a display-spoofing bidi/
    /// zero-width character. <c>IsSafeEntryName</c> in turn doesn't reject <c>"</c>/<c>&lt;</c>/
    /// <c>&gt;</c>/<c>*</c>/<c>?</c> the way <see cref="Path.GetInvalidFileNameChars"/> does, so
    /// neither check alone is a superset of the other - both are needed.
    /// </para>
    /// </summary>
    private static bool IsValidFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name is "." or "..") return false;
        var invalid = Path.GetInvalidFileNameChars();
        if (name.Any(c => invalid.Contains(c))) return false;
        return RemotePath.IsSafeEntryName(name);
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
            var now = DateTime.Now;
            int skipped = 0;

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var (newName, newExt) = ApplyPattern(pattern, extPattern, item, i, startValue, step, now);
                var fullNewName = newExt.Length > 0 && !item.IsDirectory
                    ? $"{newName}.{newExt}"
                    : newName;

                if (fullNewName == item.Name)
                    continue;

                if (!IsValidFileName(fullNewName))
                {
                    skipped++;
                    continue;
                }

                var dir = VfsPath.GetParent(item.FullPath) ?? "";
                var newPath = VfsPath.Combine(dir, fullNewName);
                Results.Add((item.FullPath, newPath));
            }

            if (Results.Count == 0)
            {
                e.Cancel = true;
                return;
            }

            var L = LocalizationService.Current;

            // Check for conflicts: duplicates in newPath (two items resolve to the same name).
            var duplicates = Results
                .GroupBy(r => r.newPath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicates.Count > 0)
            {
                StyledMessageBox.Show(
                    L.GetString("MultiRename.ErrDuplicate"),
                    L.GetString("MultiRename.Title"),
                    MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
                e.Cancel = true;
                return;
            }

            // Check for rename chains: a newPath that matches another item's oldPath.
            var oldPaths = new HashSet<string>(_items.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
            var chains = Results.Where(r => oldPaths.Contains(r.newPath)).ToList();
            if (chains.Count > 0)
            {
                StyledMessageBox.Show(
                    L.GetString("MultiRename.ErrChain"),
                    L.GetString("MultiRename.Title"),
                    MsgBoxButtons.OK, MsgBoxIcon.Warning, this);
                e.Cancel = true;
                return;
            }

            // Warn about skipped items with invalid names.
            if (skipped > 0)
            {
                var result = StyledMessageBox.Show(
                    L.GetString("MultiRename.WarnSkipped", skipped),
                    L.GetString("MultiRename.Title"),
                    MsgBoxButtons.OKCancel, MsgBoxIcon.Warning, this);
                if (result == MsgBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }
        }

        base.OnFormClosing(e);
    }

}
