using System.Text;
using System.Threading;
using CoderCommander.Services;
using CoderCommander.Viewers;
using CoderCommander.WinForms;

namespace CoderCommander.WinForms.Viewers;

/// <summary>
/// CSV/TSV content: a themed <see cref="ListView"/> table. Re-parsing from bytes only happens on
/// a full reload (delimiter change, via <see cref="ViewerContentContext.Reload"/>); toggling
/// "first row is header" re-interprets the already-parsed <see cref="CsvPayload.Rows"/> in place,
/// no reload needed.
///
/// Also doubles as the <see cref="IViewerSearchTarget"/> the shared find bar drives - row-level
/// only (a plain <see cref="ListView"/> has no per-cell selection to highlight an exact match
/// within a row), searching only what's actually visible/selectable (respects
/// <see cref="MaxDisplayRows"/> the same way the grid itself does).
/// </summary>
internal sealed class CsvViewerContent : IViewerContent, IViewerSearchTarget
{
    private const int MaxDisplayRows = 5000;

    private readonly ListView _listView;
    private readonly AppSettings _settings;
    private readonly ViewerContentContext _ctx;
    private readonly ToolStripButton _findBtn;
    private readonly ToolStripButton _hasHeaderBtn;
    private readonly ToolStripButton _autosizeBtn;
    private readonly ToolStripDropDownButton _delimiterBtn;
    private readonly List<(ToolStripMenuItem Item, string Value)> _delimiterItems = new();
    private readonly ToolStripItem[] _toolbarItems;
    private ListViewScrollbarOverlay? _overlay;

    private IReadOnlyList<string[]> _rows = [];
    private char _delimiter = ',';

    // Built lazily from the ListView (not from _rows directly) so a search match always
    // corresponds exactly to what the user can actually see and have selected - cleared whenever
    // PopulateListView rebuilds the grid.
    private string? _searchText;
    private readonly List<int> _rowStartOffsets = new();

    public Control View => _listView;
    public IReadOnlyList<ToolStripItem> ToolbarItems => _toolbarItems;
    public IViewerSearchTarget? SearchTarget => this;
    public string? StatusText { get; private set; }
    public event EventHandler? StatusChanged { add { } remove { } } // never changes outside RenderAsync

    public CsvViewerContent(ViewerContentContext ctx)
    {
        _ctx = ctx;
        _settings = ctx.Settings;
        var p = ThemeService.Current;
        var L = LocalizationService.Current;

        _listView = new ListView
        {
            Dock = DockStyle.Fill,
            // Fully qualified: this class's own IViewerContent.View property would otherwise
            // shadow the System.Windows.Forms.View enum type in simple-name lookup here.
            View = System.Windows.Forms.View.Details,
            FullRowSelect = true,
            HideSelection = false,
            GridLines = true,
            BorderStyle = BorderStyle.None,
            Font = p.GridFont,
            BackColor = p.PanelBackground,
            ForeColor = p.Foreground,
            Visible = false
        };
        // ListViewScrollbarOverlay positions its scrollbars relative to the ListView's Parent,
        // which is still null at construction time (ViewerForm parents this.View only after
        // GetOrCreateContent returns) - attaching here would silently produce dead scrollbars
        // (see ListViewScrollbarOverlay's own constructor: it no-ops when Parent is null).
        // Defer until the ListView is actually parented.
        _listView.ParentChanged += (_, _) =>
        {
            if (_listView.Parent != null && _overlay == null)
                _overlay = ListViewScrollbarOverlay.Attach(_listView);
        };

        _findBtn = ViewerToolbarFactory.CreateToolButton("View.Search", "search", (_, _) => ctx.ShowFindBar());

        _hasHeaderBtn = new ToolStripButton(L.GetString("View.Csv.HasHeader"))
        {
            CheckOnClick = true,
            Checked = _settings.ViewerCsvHasHeader
        };
        _hasHeaderBtn.Click += (_, _) =>
        {
            _settings.ViewerCsvHasHeader = _hasHeaderBtn.Checked;
            SettingsService.Save(_settings);
            PopulateListView();
        };

        _autosizeBtn = ViewerToolbarFactory.CreateToolButton("View.Csv.AutoFit", "fit_columns", (_, _) => AutosizeColumns());

        _delimiterBtn = new ToolStripDropDownButton(L.GetString("View.Csv.Delimiter"));
        AddDelimiterOption(L, "View.Csv.Delimiter.Auto", "auto");
        AddDelimiterOption(L, "View.Csv.Delimiter.Comma", ",");
        AddDelimiterOption(L, "View.Csv.Delimiter.Semicolon", ";");
        AddDelimiterOption(L, "View.Csv.Delimiter.Tab", "\t");
        AddDelimiterOption(L, "View.Csv.Delimiter.Pipe", "|");
        RefreshDelimiterChecks();

        _toolbarItems = [_findBtn, _hasHeaderBtn, _autosizeBtn, _delimiterBtn];
    }

    private void AddDelimiterOption(LocalizationService L, string labelKey, string settingValue)
    {
        var item = new ToolStripMenuItem(L.GetString(labelKey));
        item.Click += (_, _) =>
        {
            _settings.ViewerCsvDelimiter = settingValue;
            SettingsService.Save(_settings);
            RefreshDelimiterChecks();
            _ctx.Reload();
        };
        _delimiterBtn.DropDownItems.Add(item);
        _delimiterItems.Add((item, settingValue));
    }

    private void RefreshDelimiterChecks()
    {
        foreach (var (item, value) in _delimiterItems)
            item.Checked = string.Equals(_settings.ViewerCsvDelimiter, value, StringComparison.Ordinal);
    }

    private void AutosizeColumns() => _listView.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);

    public Task RenderAsync(ViewerPayload payload, CancellationToken ct)
    {
        switch (payload)
        {
            case CsvPayload csv:
                _rows = csv.Rows;
                _delimiter = csv.Delimiter;
                PopulateListView();
                break;
            case ViewerErrorPayload err:
                _rows = [];
                _listView.Columns.Clear();
                _listView.Items.Clear();
                // err.Modal distinguishes an error worth an explicit dialog from a routine
                // Prev/Next landing on a file this format can't show (e.g. "too large for text
                // mode") - see the identical reasoning in ImageViewerContent.RenderAsync.
                StatusText = err.Message;
                if (err.Modal)
                {
                    StyledMessageBox.Show(err.Message, LocalizationService.Current.GetString("View.Error"),
                        MsgBoxButtons.OK, MsgBoxIcon.Error);
                }
                break;
        }
        return Task.CompletedTask;
    }

    /// <summary>Rebuilds columns/items from <see cref="_rows"/> - called after a fresh
    /// <see cref="RenderAsync"/> and again whenever the has-header toggle flips, without
    /// re-parsing anything.</summary>
    private void PopulateListView()
    {
        var L = LocalizationService.Current;
        _searchText = null;
        _rowStartOffsets.Clear();
        _listView.BeginUpdate();
        _listView.Items.Clear();
        _listView.Columns.Clear();

        var hasHeader = _settings.ViewerCsvHasHeader && _rows.Count > 0;
        var header = hasHeader ? _rows[0] : null;

        var columnCount = 0;
        foreach (var row in _rows)
            if (row.Length > columnCount) columnCount = row.Length;
        if (columnCount == 0) columnCount = 1;

        for (var c = 0; c < columnCount; c++)
        {
            var title = header != null && c < header.Length && header[c].Length > 0
                ? header[c]
                : L.GetString("View.Csv.Column", c + 1);
            _listView.Columns.Add(title, 100);
        }

        var startIndex = hasHeader ? 1 : 0;
        var shown = 0;
        for (var r = startIndex; r < _rows.Count && shown < MaxDisplayRows; r++, shown++)
        {
            var row = _rows[r];
            var item = new ListViewItem(row.Length > 0 ? row[0] : "");
            for (var c = 1; c < columnCount; c++)
                item.SubItems.Add(c < row.Length ? row[c] : "");
            _listView.Items.Add(item);
        }

        _listView.EndUpdate();

        var totalDataRows = _rows.Count - startIndex;
        var truncated = totalDataRows > MaxDisplayRows;
        var baseStatus = L.GetString("View.CsvMode", totalDataRows, columnCount, DelimiterDisplay(_delimiter));
        StatusText = truncated ? $"{baseStatus} — {L.GetString("View.Csv.Truncated", MaxDisplayRows)}" : baseStatus;
    }

    private static string DelimiterDisplay(char d) => d switch
    {
        ',' => ",",
        ';' => ";",
        '\t' => "Tab",
        '|' => "|",
        _ => d.ToString(),
    };

    public void ApplyTheme()
    {
        var p = ThemeService.Current;
        _listView.BackColor = p.PanelBackground;
        _listView.ForeColor = p.Foreground;
        NativeControlThemer.ThemeListView(_listView);
        NativeControlThemer.ThemeListViewHeader(_listView);
    }

    // ── IViewerSearchTarget ─────────────────────────────────────────────────────────────────
    // Cells within a row are tab-joined and rows are newline-joined, mirroring plain-text CSV
    // shape closely enough that a search for e.g. "42,Smith" still reads naturally, without
    // actually needing to match the file's real delimiter.

    public string GetSearchText()
    {
        if (_searchText != null) return _searchText;

        var sb = new StringBuilder();
        _rowStartOffsets.Clear();
        foreach (ListViewItem item in _listView.Items)
        {
            _rowStartOffsets.Add(sb.Length);
            sb.Append(item.Text);
            for (var i = 1; i < item.SubItems.Count; i++)
            {
                sb.Append('\t');
                sb.Append(item.SubItems[i].Text);
            }
            sb.Append('\n');
        }
        _searchText = sb.ToString();
        return _searchText;
    }

    /// <summary>Start offset of the currently selected row, so re-running a search after the user
    /// manually clicked a different row continues from there - same "resume from the caret" idea
    /// <see cref="TextViewerContent"/> gets for free from <c>RichTextBox.SelectionStart</c>, just
    /// keyed off row selection instead of a text caret.</summary>
    public int CurrentOffset
    {
        get
        {
            GetSearchText(); // ensures _rowStartOffsets is populated even if never queried yet
            var selected = _listView.SelectedIndices.Count > 0 ? _listView.SelectedIndices[0] : -1;
            return selected >= 0 && selected < _rowStartOffsets.Count ? _rowStartOffsets[selected] : 0;
        }
    }

    public void SelectRange(int start, int length)
    {
        GetSearchText(); // ensures _rowStartOffsets matches the text these offsets were found in
        if (_rowStartOffsets.Count == 0) return;

        // _rowStartOffsets is sorted ascending by construction (each row starts after the
        // previous one ends) - binary search for the last row whose start is <= start.
        var idx = _rowStartOffsets.BinarySearch(start);
        if (idx < 0) idx = ~idx - 1;
        if (idx < 0 || idx >= _listView.Items.Count) return;

        _listView.SelectedIndices.Clear();
        var item = _listView.Items[idx];
        item.Selected = true;
        item.Focused = true;
        item.EnsureVisible();
    }

    public void FocusContent() => _listView.Focus();

    // ── Disposal ─────────────────────────────────────────────────────────────────────────────
    // _listView/toolbar items are owned transitively by ViewerForm's own Controls/ToolStrip.Items
    // collections (same accepted CA2213 pattern as the other contents). _overlay is a free-standing
    // IDisposable (its scrollbars are siblings of the ListView, not children of it), so it must be
    // disposed explicitly.
    public void Dispose()
    {
        _overlay?.Dispose();
        _listView.Dispose();
        _findBtn.Dispose();
        _autosizeBtn.Dispose();
        _hasHeaderBtn.Dispose();
        _delimiterBtn.Dispose();
        GC.SuppressFinalize(this);
    }
}
